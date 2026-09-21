using Common.Infra.Win32;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using WinAPI;

using global::CutADash.Models;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Windows.ApplicationModel.DataTransfer;


namespace CutADash.Infra.Win32 {
    public class ClipboardMonitor : IDisposable
    {
        private readonly IntPtr _hWnd;
        private readonly WindowMessageDispatcher _dispatcher;
        private readonly List<Action<ClipboardItem>> _callbacks = new();

        // 履歴からOSクリップボードへ書き戻した直後の変化を、新規コピーとして
        // 取り込んでしまわないよう、次の1回だけ無視するためのフラグ
        // 検索欄でCtrl+Cした時のように、書き込むのが自分ではなく完了を検知できない場合に使う。
        // 次の1回だけ無視する
        private volatile bool _suppressNext;

        // 自分でクリップボードへ書き戻す場合の抑制。
        // WinRTのClipboard.SetContentは内部でクリップボードを作り直すため、1回の書き込みで
        // WM_CLIPBOARDUPDATEが複数回来る。「次の1回だけ無視」では2回目が履歴に入ってしまい、
        // 書式なしで貼り付けるとプレーンテキストの項目が増えることになる。
        //
        // 時間窓で誤魔化すと、窓の中に入った本物のコピーを取りこぼす。そこで
        // GetClipboardSequenceNumber(クリップボードが変わるたびに増える通し番号)を使い、
        // 「自分の書き込み完了時点の番号以下なら自分由来」と確定的に判定する。
        private volatile bool _isWritingOwnContent;
        private uint _ownWriteSequence;

        // Start/Stopの多重呼び出しでAddClipboardFormatListener等を二重登録しないためのフラグ
        private bool _isMonitoring;

        // Excel等、1回のコピー操作でクリップボードを複数回書き換えるアプリ対策。
        // 短時間に連続してWM_CLIPBOARDUPDATEが来た場合、直前の処理をキャンセルして
        // 最後の1回だけを処理する(デバウンス)。これをしないと、フォーマットが
        // まだ揃っていない途中の状態と、揃った後の状態の両方が別々の履歴として
        // 登録されてしまう(例: Shape判定前のImageと、Shape判定後のImageが二重に入る)
        private const int DebounceMilliseconds = 300;
        private CancellationTokenSource? _debounceCts;

        /// <summary>現在クリップボードを監視中かどうか。トレイメニュー等の表示切り替えに使う。</summary>
        public bool IsMonitoring => _isMonitoring;

        public ClipboardMonitor(IntPtr hWnd, WindowMessageDispatcher dispatcher) {
            _hWnd = hWnd;
            _dispatcher = dispatcher;

            Start();
        }

        /// <summary>クリップボードの監視を開始する。既に開始済みなら何もしない。</summary>
        public void Start() {
            if (_isMonitoring)
                return;

            WinAPI.WinUser.ClipboardNative.AddClipboardFormatListener(_hWnd);

            _dispatcher.AddHandler(WindowMessages.WM_CLIPBOARDUPDATE,
                                    OnClipboardMessage);

            _isMonitoring = true;
        }

        /// <summary>クリップボードの監視を停止する。既に停止済みなら何もしない。</summary>
        public void Stop() {
            if (!_isMonitoring)
                return;

            WinAPI.WinUser.ClipboardNative.RemoveClipboardFormatListener(_hWnd);

            _dispatcher.RemoveHandler(WindowMessages.WM_CLIPBOARDUPDATE, OnClipboardMessage);

            _isMonitoring = false;
        }

        public void Register(Action<ClipboardItem> callback) {
            _callbacks.Add(callback);
        }

        public void Unregister(Action<ClipboardItem> callback) {
            _callbacks.Remove(callback);
        }

        /// <summary>次に発生するクリップボード変更通知を1回だけ無視する。
        /// 履歴の項目をOSクリップボードへ書き戻す(ペースト用)際に、
        /// それ自体が新規コピーとして履歴に再登録されるのを防ぐために使う。</summary>
        /// <summary>
        /// 次の1回の変更通知を無視する。書き込みを自分で行わず、完了も検知できない場合
        /// (検索欄のCtrl+Cなど)に使う。
        /// </summary>
        public void SuppressNextChange() {
            _suppressNext = true;
        }

        /// <summary>自分でクリップボードへ書き戻す直前に呼ぶ。EndOwnWriteと必ず対で使う。</summary>
        public void BeginOwnWrite() {
            _isWritingOwnContent = true;
        }

        /// <summary>
        /// 書き戻しが終わった直後に呼ぶ。この時点の通し番号を控え、
        /// それ以下の通知は自分由来として無視する。
        /// </summary>
        public void EndOwnWrite() {
            _ownWriteSequence = WinAPI.WinUser.ClipboardNative.GetClipboardSequenceNumber();
            _isWritingOwnContent = false;
        }

        private bool OnClipboardMessage(IntPtr wParam,IntPtr lParam) {
            if (_suppressNext) {
                _suppressNext = false;
                return false;
            }

            // 書き込みの最中、および自分の書き込み完了時点までの通知は自分由来なので無視する
            if (_isWritingOwnContent
                || WinAPI.WinUser.ClipboardNative.GetClipboardSequenceNumber() <= _ownWriteSequence) {
                return false;
            }

            // GetClipboardOwnerは、実際にSetClipboardData(クリップボードへの書き込み)を
            // 行ったウィンドウを返すため、フォアグラウンドウィンドウの推測よりも
            // 正確にコピー元アプリを特定できる
            var ownerWindow = WinAPI.WinUser.GetClipboardOwner();

            // 直前の(まだ処理されていない)変更をキャンセルし、今回の変更だけを
            // 少し待ってから処理する。DebounceMilliseconds以内に次の変更が来なければ
            // そのまま処理される
            _debounceCts?.Cancel();
            var cts = new CancellationTokenSource();
            _debounceCts = cts;

            _ = DebouncedHandleClipboardChangedAsync(ownerWindow, cts.Token);

            return false;
        }

        private async Task DebouncedHandleClipboardChangedAsync(IntPtr ownerWindow, CancellationToken token) {
            try {
                await Task.Delay(DebounceMilliseconds, token);
            }
            catch (TaskCanceledException) {
                return; // 待っている間により新しい変更が来たので、この回は処理しない
            }

            if (token.IsCancellationRequested)
                return;

            await HandleClipboardChangedAsync(ownerWindow);
        }

        private async Task HandleClipboardChangedAsync(IntPtr ownerWindow) {
            try
            {
                var sourceAppName = GetProcessName(ownerWindow);

                // 設定で除外されているアプリからのコピーは、履歴に一切登録しない
                // (コールバックを呼ばずここで打ち切る)
                if (Preferences.PreferencesGateway.IsAppExcluded(sourceAppName))
                    return;

                // Excelの図形などOLEオブジェクトを構成する非標準フォーマットも合わせて
                // スナップショットしておく。遅延レンダリングするアプリ(Excel等)が相手だと
                // ハングする可能性があるため、内部でタイムアウト付きの別スレッドに逃がしている
                var rawFormats = await Common.Infra.Win32.RawClipboardFormats.CaptureAllAsync();

                var data = Clipboard.GetContent();

                ClipboardItem item = new ClipboardItem {
                    Timestamp = DateTime.Now,
                    SourceAppName = sourceAppName
                };

                // Text。ExcelはセルコピーでもフォールバックのBitmap(図として貼り付け用)を
                // 一緒に乗せてくるため、Bitmapの有無より先に「実際に中身のあるテキストが
                // あるかどうか」を確認する。CF_TEXTはあっても空文字のことがあるので、
                // 中身が空ならテキストとして扱わずImage判定へ進む
                string? text = null;
                if (data.Contains(StandardDataFormats.Text)) {
                    text = await data.GetTextAsync();
                }

                if (!string.IsNullOrEmpty(text)) {
                    item.Type = ClipboardContentType.Text;
                    item.Text = text;

                    // リッチテキスト。取得に失敗してもリッチテキストだけ諦める(プレーンテキストとしては使える)
                    if (data.Contains(StandardDataFormats.Rtf)) {
                        try { item.Rtf = await data.GetRtfAsync(); }
                        catch (Exception ex) { Debug.WriteLine(ex); }
                    }

                    if (data.Contains(StandardDataFormats.Html)) {
                        try { item.Html = await data.GetHtmlFormatAsync(); }
                        catch (Exception ex) { Debug.WriteLine(ex); }
                    }
                }
                // Image。ブラウザなどで画像をコピーすると、画像本体(Bitmap)と一緒に
                // 説明用のHTML(<img>タグ等)が乗ってくることが多いが、中身のあるTextを
                // 伴わないことがほとんどなので、上のText判定で弾かれなかった場合だけ見る
                else if (data.Contains(StandardDataFormats.Bitmap)) {
                    item.Type = ClipboardContentType.Image;
                    var bitmap = await data.GetBitmapAsync();
                    using var stream = await bitmap.OpenReadAsync();

                    using var memory = new MemoryStream();

                    await stream.AsStreamForRead().CopyToAsync(memory);

                    item.Image = memory.ToArray();
                    item.IsGif = IsGifImage(item.Image);

                    // Excelの図形など、コピー元でOLEオブジェクトとして提供されている場合、
                    // 単純なビットマップ以外の生フォーマットも一緒に保持しておくことで、
                    // 貼り戻し時に編集可能な状態のまま復元できるようにする
                    item.RawFormats = rawFormats;
                    item.IsShape = Common.Infra.Win32.RawClipboardFormats.LooksLikeShape(rawFormats);
                }
                // Files
                else if (data.Contains(StandardDataFormats.StorageItems)) {
                    item.Type = ClipboardContentType.Files;
                    var items = await data.GetStorageItemsAsync();

                    item.Files = items.Select(x => x.Path).ToList();
                }
                else {
                    item.Type = ClipboardContentType.Unknown;
                }

                foreach (var callback in _callbacks) {
                    try {
                        callback(item);
                    }
                    catch (Exception ex) {
                        Debug.WriteLine(ex);
                    }
                }
            }
            catch (Exception ex) {
                Debug.WriteLine(ex);
            }
        }

        // GIFはマジックバイト("GIF87a"/"GIF89a")の先頭3バイト"GIF"で判定する
        private static bool IsGifImage(byte[] bytes) {
            return bytes.Length >= 3
                && bytes[0] == (byte)'G'
                && bytes[1] == (byte)'I'
                && bytes[2] == (byte)'F';
        }

        // 指定したウィンドウのプロセス名を取得する。取得できなければnull
        private static string? GetProcessName(IntPtr hWnd) {
            try {
                if (hWnd == IntPtr.Zero)
                    return null;

                WinAPI.WinUser.GetWindowThreadProcessId(hWnd, out var processId);
                if (processId == 0)
                    return null;

                using var process = Process.GetProcessById((int)processId);
                return process.ProcessName;
            }
            catch (Exception ex) {
                Debug.WriteLine(ex);
                return null;
            }
        }

        public void Dispose() {
            Stop();

            GC.SuppressFinalize(this);
        }

        ~ClipboardMonitor() {
            Dispose();
        }
    }
}
