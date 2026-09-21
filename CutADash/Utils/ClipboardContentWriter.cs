using CutADash.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

namespace CutADash.Utils
{
    /// <summary>
    /// 履歴上のClipboardItemを、OSのクリップボードへ書き戻す。
    /// </summary>
    public static class ClipboardContentWriter
    {
        /// <param name="plainTextOnly">
        /// trueの場合、Rtf/Htmlを保持していてもプレーンテキストとしてのみ貼り付ける
        /// (Contents画面の「プレーンテキストとして貼り付け」用)。
        /// </param>
        public static async Task SetClipboardContentAsync(ClipboardItem item, bool plainTextOnly = false)
        {
            var package = new DataPackage();

            switch (item.Type)
            {
                case ClipboardContentType.Text:
                    if (item.Text is not null)
                        package.SetText(item.Text);

                    // リッチテキストがあれば書式付きでも提供する。貼り付け先アプリは
                    // 対応している一番リッチな形式を優先して使うため、Text/Rtf/Htmlを
                    // すべて併せて提供しておいて問題ない
                    if (!plainTextOnly)
                    {
                        if (item.Rtf is not null)
                            package.SetRtf(item.Rtf);

                        if (item.Html is not null)
                            package.SetHtmlFormat(item.Html);
                    }

                    break;

                case ClipboardContentType.Image:
                    // Excelの図形など、コピー時の全フォーマットを生のまま保存できている場合は、
                    // そちらをWin32のクリップボードAPIでそのまま書き戻す方が、貼り付け先で
                    // 編集可能な状態のまま復元できる可能性が高い。WinRTのDataPackage経由の
                    // SetContentは内部でクリップボードを作り直す(EmptyClipboard相当)ため、
                    // 生フォーマットの書き戻しと両立できず、どちらか一方しか使えない。
                    // GIFは下のIsGif分岐(ファイルとしての提供)を優先したいため対象外にする
                    if (!item.IsGif)
                    {
                        var rawFormats = item.RawFormats ?? await TryReadRawFormatsFileAsync(item.RawFormatsFilePath);
                        if (rawFormats is not null && Common.Infra.Win32.RawClipboardFormats.WriteAll(rawFormats))
                            return;
                    }

                    // フルサイズはDBに入れずファイルに書き出しているため、取り込み直後で
                    // まだItem.Imageが残っている場合を除き、ImageFilePathから読み込む
                    var imageBytes = item.Image ?? await TryReadImageFileAsync(item.ImageFilePath);
                    if (imageBytes is not null)
                    {
                        var stream = new InMemoryRandomAccessStream();
                        await stream.WriteAsync(imageBytes.AsBuffer());
                        stream.Seek(0);
                        package.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
                    }

                    // GIFはSetBitmap(Bitmapフォーマット)経由だと貼り付け先が先頭フレームだけを
                    // デコードしてしまい、アニメーションが失われて静止画に見える。
                    // ファイルとしても提供することで、ファイル貼り付けに対応するアプリでは
                    // GIFのままアニメーション付きで貼り付けられるようにする。
                    // 元のファイルは暗号化されているため、そのまま渡さず、一時フォルダへ
                    // 復号したコピーを作ってそちらを渡す
                    if (item.IsGif && !string.IsNullOrEmpty(item.ImageFilePath) && File.Exists(item.ImageFilePath))
                    {
                        try
                        {
                            var decryptedBytes = imageBytes ?? await Common.Db.ImageFileCipher.ReadDecryptedFileAsync(item.ImageFilePath);
                            var tempPath = Path.Combine(Path.GetTempPath(), $"CutADash_{Guid.NewGuid():N}.gif");
                            await File.WriteAllBytesAsync(tempPath, decryptedBytes);

                            var storageFile = await StorageFile.GetFileFromPathAsync(tempPath);
                            package.SetStorageItems(new List<IStorageItem> { storageFile });

                            // 貼り付け先が読み終えるまで待ってから、一時ファイルを掃除する
                            _ = Task.Delay(TimeSpan.FromMinutes(5)).ContinueWith(_ =>
                            {
                                try { File.Delete(tempPath); } catch { /* 掃除の失敗は無視する */ }
                            });
                        }
                        catch
                        {
                            // ファイルとしての提供に失敗しても、Bitmapとしての貼り付けは維持する
                        }
                    }
                    break;

                case ClipboardContentType.Files:
                    if (item.Files is not null)
                    {
                        var storageItems = new List<IStorageItem>();
                        foreach (var path in item.Files)
                        {
                            try
                            {
                                storageItems.Add(await StorageFile.GetFileFromPathAsync(path));
                            }
                            catch
                            {
                                // 元ファイルが削除・移動済みの場合はスキップする
                            }
                        }

                        if (storageItems.Count > 0)
                            package.SetStorageItems(storageItems);
                    }
                    break;
            }

            PasteDiagnosticsLog.Write($"[Writer] SetContent開始 Type={item.Type} HasText={item.Text is not null} HasRtf={item.Rtf is not null} HasHtml={item.Html is not null} plainTextOnly={plainTextOnly} 所有者={PasteDiagnosticsLog.Describe(WinAPI.WinUser.GetClipboardOwner())}");

            Clipboard.SetContent(package);
            PasteDiagnosticsLog.Write("[Writer] SetContent成功");

            // SetContentだけだとデータは自プロセスが保持したままで、貼り付け先から
            // 要求された時に渡す遅延提供になる。直後にCtrl+Vを送ると、こちらのUIスレッドが
            // 塞がっているタイミングで受け渡しに失敗し、以前の内容が貼られることがある。
            // Flushでクリップボードへ実体を渡し切り、自プロセスと切り離しておく。
            //
            // Flushは追加の変更通知を発生させるが、ClipboardMonitorはクリップボードの
            // 通し番号で自分由来かどうかを判定しているため、履歴には登録されない。
            try
            {
                Clipboard.Flush();
                PasteDiagnosticsLog.Write($"[Writer] Flush成功 所有者={PasteDiagnosticsLog.Describe(WinAPI.WinUser.GetClipboardOwner())}");
            }
            catch (Exception ex)
            {
                // 他アプリがクリップボードを掴んでいると失敗することがある。
                // その場合も遅延提供のままにはなるので、貼り付け自体は続行する
                PasteDiagnosticsLog.Write($"[Writer] Flush失敗: {ex.GetType().Name}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[ClipboardContentWriter] Flush失敗: {ex.Message}");
            }
        }

        private static async Task<System.Collections.Generic.Dictionary<string, byte[]>?> TryReadRawFormatsFileAsync(string? rawFormatsFilePath)
        {
            if (string.IsNullOrEmpty(rawFormatsFilePath) || !File.Exists(rawFormatsFilePath))
                return null;

            try
            {
                var decrypted = await Common.Db.ImageFileCipher.ReadDecryptedFileAsync(rawFormatsFilePath);
                return Common.Infra.Win32.RawClipboardFormats.Deserialize(decrypted);
            }
            catch
            {
                return null;
            }
        }

        private static async Task<byte[]?> TryReadImageFileAsync(string? imageFilePath)
        {
            if (string.IsNullOrEmpty(imageFilePath) || !File.Exists(imageFilePath))
                return null;

            try
            {
                return await Common.Db.ImageFileCipher.ReadDecryptedFileAsync(imageFilePath);
            }
            catch
            {
                return null;
            }
        }
    }
}
