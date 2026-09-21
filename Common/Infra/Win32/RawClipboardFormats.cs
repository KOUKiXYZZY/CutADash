using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static WinAPI.WinUser;

namespace Common.Infra.Win32
{
    /// <summary>
    /// クリップボードに乗っているすべてのフォーマットを、生バイト列のまま丸ごと
    /// 読み書きする。Excelの図形などOLEオブジェクトを構成する非標準フォーマット
    /// (Embed Source/Object Descriptor/Biff12等)は、WinRTのClipboard/DataPackageでは
    /// 読み書きできないため、貼り付け先で編集可能な状態のまま復元したい場合は
    /// このクラスでWin32のクリップボードAPIを直接使う。
    /// </summary>
    public static class RawClipboardFormats
    {
        // COM経由のライブなポインタ/ハンドルを指すだけで、単なるバイト列としての
        // 保存・復元ができない(かつ復元しても無意味・有害な)フォーマット
        private static readonly HashSet<string> ExcludedFormatNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "DataObject",
            "Ole Private Data",
        };

        // 標準の定義済みフォーマットのうち、hDataがHGLOBALではなくGDI等の別種のハンドル
        // (HBITMAP/HPALETTE/HENHMETAFILE)になっているもの。これらにGlobalLock/GlobalSizeを
        // 呼ぶと不正なメモリ扱いになりヒープ破損でクラッシュする(実際に発生した)ため、
        // 名前を引く前に数値IDの時点で除外する。CF_HDROP(15)はファイルとして別途
        // 扱っているため、ここでも対象外にする。
        private static readonly HashSet<uint> ExcludedFormatIds = new()
        {
            2,  // CF_BITMAP (HBITMAP)
            3,  // CF_METAFILEPICT (中身のHMETAFILEはプロセス固有で復元しても無意味)
            9,  // CF_PALETTE (HPALETTE)
            14, // CF_ENHMETAFILE (HENHMETAFILE)
            15, // CF_HDROP (StorageItems側で別途扱う)
        };

        // Excel/PowerPoint等が、図形やセル範囲をOLEオブジェクトとしてコピーした際に
        // 乗せてくる目印フォーマット。これらのいずれかが含まれていれば、単純な画像コピー
        // ではなく「図形」としてコピーされたものとみなす。
        // 実際にExcelのシェイプコピーで確認できた名前(--dump-rawformatsで検証済み):
        // "Excel 2007 Internal Shape" / "Art::GVML ClipFormat" / "ShadowWorkbook"。
        // "Embed Source"等の一般的なOLEオブジェクト形式は、Excelのシェイプコピーでは
        // 実際には乗ってこなかった。
        private static readonly HashSet<string> ShapeMarkerFormatNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Excel 2007 Internal Shape",
            "Art::GVML ClipFormat",
            "ShadowWorkbook",
            "Embed Source",
            "Object Descriptor",
            "Link Source",
            "Link Source Descriptor",
            "Native",
            "Biff12",
            "Biff8",
            "Biff5",
            "Xml Spreadsheet",
        };

        /// <summary>
        /// 取得したフォーマット一式に、Excel/PowerPoint等のOLEオブジェクト(図形)コピーを
        /// 示す目印フォーマットが含まれているかどうか。
        /// </summary>
        public static bool LooksLikeShape(Dictionary<string, byte[]>? formats)
        {
            if (formats is null)
                return false;

            foreach (var name in formats.Keys)
            {
                if (ShapeMarkerFormatNames.Contains(name))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// GetClipboardDataを呼ぶ既定のタイムアウト(ミリ秒)。Excelなど遅延レンダリングを
        /// 使うアプリは、コピー元アプリが応答するまでGetClipboardDataがブロックし続ける
        /// ことがある(通常は一瞬だが、コピー元アプリがモーダルダイアログ等で固まっていると
        /// 応答しないまま永久に返ってこないおそれがある)。このタイムアウトを超えたら
        /// 取得を諦めて処理を続行させる。
        /// </summary>
        private const int DefaultCaptureTimeoutMs = 800;

        /// <summary>
        /// 現在クリップボードにあるすべてのフォーマットを、フォーマット名(または
        /// 名前を持たない定義済み形式は"#<数値ID>")をキーにした生バイト列として取得する。
        /// 取得に失敗した場合や、クリップボードが開けなかった場合はnullを返す。
        ///
        /// 遅延レンダリングによるハングを避けるため、実際の取得は専用のバックグラウンド
        /// スレッドで行い、タイムアウトしたら(呼び出し元は)待たずに諦めてnullを返す。
        /// バックグラウンドスレッド自体は残り続けるが、いずれコピー元アプリが応答すれば
        /// 自然にCloseClipboardまで完了して終了する(ThreadPoolを塞がないよう専用スレッドにする)。
        /// </summary>
        public static async Task<Dictionary<string, byte[]>?> CaptureAllAsync(int timeoutMs = DefaultCaptureTimeoutMs)
        {
            var tcs = new TaskCompletionSource<Dictionary<string, byte[]>?>();

            var thread = new Thread(() =>
            {
                try
                {
                    tcs.TrySetResult(CaptureAllCore());
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[RawClipboardFormats] CaptureAllに失敗しました: {ex}");
                    tcs.TrySetResult(null);
                }
            })
            {
                IsBackground = true,
                Name = "ClipboardRawCapture",
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            var winner = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
            if (winner != tcs.Task)
            {
                Debug.WriteLine("[RawClipboardFormats] CaptureAllがタイムアウトしました" +
                    "(遅延レンダリングによるハングの可能性)。取得を諦めます");
                return null;
            }

            return await tcs.Task;
        }

        private static Dictionary<string, byte[]>? CaptureAllCore()
        {
            if (!ClipboardNative.OpenClipboard(IntPtr.Zero))
                return null;

            try
            {
                var result = new Dictionary<string, byte[]>();
                uint format = 0;

                while ((format = ClipboardNative.EnumClipboardFormats(format)) != 0)
                {
                    if (ExcludedFormatIds.Contains(format))
                        continue;

                    var name = GetFormatName(format);
                    if (ExcludedFormatNames.Contains(name))
                        continue;

                    var handle = ClipboardNative.GetClipboardData(format);
                    if (handle == IntPtr.Zero)
                        continue;

                    var bytes = ReadGlobalMemory(handle);
                    if (bytes is not null)
                        result[name] = bytes;
                }

                return result.Count > 0 ? result : null;
            }
            finally
            {
                ClipboardNative.CloseClipboard();
            }
        }

        /// <summary>
        /// CaptureAllで取得したフォーマット一式を、OSクリップボードへそのまま書き戻す。
        /// EmptyClipboardしてから書き込むため、呼び出し前の内容は失われる。
        /// </summary>
        public static bool WriteAll(Dictionary<string, byte[]> formats)
        {
            if (formats.Count == 0)
                return false;

            if (!ClipboardNative.OpenClipboard(IntPtr.Zero))
                return false;

            try
            {
                ClipboardNative.EmptyClipboard();

                foreach (var (name, bytes) in formats)
                {
                    var format = ResolveFormatId(name);
                    if (format == 0)
                        continue;

                    var handle = AllocGlobalMemory(bytes);
                    if (handle == IntPtr.Zero)
                        continue;

                    // SetClipboardDataが成功すると、以降のメモリ解放はOS側の責務になる。
                    // 失敗した場合は自分で確保したメモリを解放する
                    if (ClipboardNative.SetClipboardData(format, handle) == IntPtr.Zero)
                        ClipboardNative.GlobalFree(handle);
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RawClipboardFormats] WriteAllに失敗しました: {ex}");
                return false;
            }
            finally
            {
                ClipboardNative.CloseClipboard();
            }
        }

        // 名前を持つ定義済み/独自フォーマットはそのまま名前を、CF_BITMAPのように
        // 名前を持たない標準フォーマットは"#<ID>"を返す(クリップボードビューアの慣例に合わせる)
        private static string GetFormatName(uint format)
        {
            var buffer = new StringBuilder(256);
            var length = ClipboardNative.GetClipboardFormatName(format, buffer, buffer.Capacity);
            return length > 0 ? buffer.ToString() : $"#{format}";
        }

        private static uint ResolveFormatId(string name)
        {
            if (name.StartsWith('#') && uint.TryParse(name.AsSpan(1), out var id))
                return id;

            return ClipboardNative.RegisterClipboardFormat(name);
        }

        private static byte[]? ReadGlobalMemory(IntPtr handle)
        {
            var ptr = ClipboardNative.GlobalLock(handle);
            if (ptr == IntPtr.Zero)
                return null;

            try
            {
                var size = (int)ClipboardNative.GlobalSize(handle);
                if (size <= 0)
                    return null;

                var bytes = new byte[size];
                System.Runtime.InteropServices.Marshal.Copy(ptr, bytes, 0, size);
                return bytes;
            }
            finally
            {
                ClipboardNative.GlobalUnlock(handle);
            }
        }

        private static IntPtr AllocGlobalMemory(byte[] bytes)
        {
            var handle = ClipboardNative.GlobalAlloc(ClipboardNative.GMEM_MOVEABLE, (UIntPtr)bytes.Length);
            if (handle == IntPtr.Zero)
                return IntPtr.Zero;

            var ptr = ClipboardNative.GlobalLock(handle);
            if (ptr == IntPtr.Zero)
            {
                ClipboardNative.GlobalFree(handle);
                return IntPtr.Zero;
            }

            try
            {
                System.Runtime.InteropServices.Marshal.Copy(bytes, 0, ptr, bytes.Length);
            }
            finally
            {
                ClipboardNative.GlobalUnlock(handle);
            }

            return handle;
        }

        /// <summary>
        /// フォーマット一式を、1つのバイト列にシリアライズする(ファイル保存用)。
        /// 形式: [4バイト件数][各エントリ: 4バイト名前長 + 名前(UTF8) + 4バイトデータ長 + データ]
        /// </summary>
        public static byte[] Serialize(Dictionary<string, byte[]> formats)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.UTF8);

            writer.Write(formats.Count);
            foreach (var (name, bytes) in formats)
            {
                var nameBytes = Encoding.UTF8.GetBytes(name);
                writer.Write(nameBytes.Length);
                writer.Write(nameBytes);
                writer.Write(bytes.Length);
                writer.Write(bytes);
            }

            return stream.ToArray();
        }

        public static Dictionary<string, byte[]> Deserialize(byte[] blob)
        {
            var result = new Dictionary<string, byte[]>();

            using var stream = new MemoryStream(blob);
            using var reader = new BinaryReader(stream, Encoding.UTF8);

            var count = reader.ReadInt32();
            for (var i = 0; i < count; i++)
            {
                var nameLength = reader.ReadInt32();
                var name = Encoding.UTF8.GetString(reader.ReadBytes(nameLength));
                var dataLength = reader.ReadInt32();
                var data = reader.ReadBytes(dataLength);
                result[name] = data;
            }

            return result;
        }
    }
}
