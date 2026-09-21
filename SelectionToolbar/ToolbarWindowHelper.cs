using Common.Extension;
using Microsoft.UI.Xaml;
using System;
using WinAPI;
using WinRT.Interop;
using WinUIEx;
using Windows.Graphics;
using static WinAPI.WinUser;
using ShowWindowCommands = WinAPI.ShowWindowCommands;

namespace SelectionToolbar
{
    /// <summary>
    /// SelectionToolbarWindowで使う、フォーカスを奪わない(NOACTIVATE)・
    /// 常に最前面(TOPMOST)での表示・外側クリックでの自動クローズ・無操作での自動クローズを
    /// まとめたヘルパー。継承ではなく、ウィンドウのコンストラクタから生成して使う
    /// (WindowEx派生かつXAMLルートを共通の中間基底クラスにする方式はXamlCompilerとの
    /// 相性リスクがあるため避けている)。
    ///
    /// サイズはコンテンツに応じた自動計算(ResizeToContent)を試したが見た目が安定しなかった
    /// ため固定サイズにした。角丸(DWMのSetWindowCornerPreference)も試したが安定せず、
    /// 見た目のメリットに見合わなかったため、四角形のまま何もしていない。
    /// </summary>
    public sealed class ToolbarWindowHelper
    {
        // マウス位置からのオフセット(px)。選択直後のカーソルに指が被らないように少しずらす
        private const int Offset = 12;

        private readonly WindowEx _window;
        private readonly IntPtr _hWnd;
        private readonly int _widthDip;
        private readonly int _heightDip;
        private readonly bool _showAbove;
        private readonly OutsideClickWatcher _outsideClickWatcher = new();
        private readonly DispatcherTimer _autoHideTimer = new();
        private bool _isPointerOver;

        // ShowAtのたびにMove直後のDPIで計算し直す(コンストラクタ時点ではまだどの
        // モニタにも実際に配置されていないため、そこでのDPIを信用できない)
        private int _heightPx;

        /// <param name="showAbove">
        /// trueの場合、指定座標の下ではなく上に表示する(SelectionToolbarWindowで使用)。
        /// </param>
        public ToolbarWindowHelper(WindowEx window, FrameworkElement rootElement, int widthDip, int heightDip, bool showAbove = false)
        {
            _window = window;
            _hWnd = WindowNative.GetWindowHandle(_window);
            _showAbove = showAbove;

            var exStyle = GetWindowLong(_hWnd, GWL_EXSTYLE);
            SetWindowLong(_hWnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);

            // IsTitleBarVisible=Falseにしてもキャプション/枠自体は残っており、透明な
            // ウィンドウの外側に細い枠線が見えてしまっていたため、明示的に取り除く
            var style = GetWindowLong(_hWnd, GWL_STYLE);
            style &= ~(int)(WindowStyles.WS_CAPTION | WindowStyles.WS_THICKFRAME
                | WindowStyles.WS_BORDER | WindowStyles.WS_DLGFRAME);
            SetWindowLong(_hWnd, GWL_STYLE, style);

            // 上のWin32スタイル除去だけでは、WinAppSDKのコンポジション層が持つ
            // タイトルバー領域の予約(ボタン下の余白として見えていた)までは消えない。
            // MainWindowと同じAppWindow.TitleBarベースの方法で確実に取り除く
            _window.RemoveTitleBar();

            // ウィンドウ自体を完全透明にし、角丸のBorderだけが見えるようにする。
            // 他の透明化/クリッピング手段(カラーキー・SetRegion・DWM角丸)と併用して
            // 上手くいかなかったことがあるため、今回はこれ単体だけを使う
            _window.SystemBackdrop = new TransparentTintBackdrop();

            _widthDip = widthDip;
            _heightDip = heightDip;

            _outsideClickWatcher.ClickedOutside += (_, _) => HideNoActivate();

            _autoHideTimer.Tick += (_, _) =>
            {
                _autoHideTimer.Stop();
                HideNoActivate();
            };

            // マウスがツールバーの上に乗っている間は自動非表示を止め、離れたら
            // そこから秒数を数え直す
            rootElement.PointerEntered += (_, _) =>
            {
                _isPointerOver = true;
                _autoHideTimer.Stop();
            };
            rootElement.PointerExited += (_, _) =>
            {
                _isPointerOver = false;
                RestartAutoHideTimer();
            };
        }

        public void ShowAt(int screenX, int screenY)
        {
            // 表示先モニタのDPIは、実際にそこへ配置してからでないと正しく取れない
            // (コンストラクタ時点ではまだどの画面にも属していない既定のDPIになってしまう)。
            // そのため、まず前回分かっている高さ(初回は0)でおおまかに配置し、
            // 実際のDPIを取得し直してからサイズと位置を確定する
            var provisionalY = _showAbove ? screenY - _heightPx - Offset : screenY + Offset;
            _window.AppWindow.Move(new PointInt32(screenX, provisionalY));

            var dpi = WinUser.Dpi.GetDpiForWindow(_hWnd);
            var scale = dpi / 96.0;
            var widthPx = (int)(_widthDip * scale);
            _heightPx = (int)(_heightDip * scale);
            _window.AppWindow.Resize(new SizeInt32(widthPx, _heightPx));

            var y = _showAbove ? screenY - _heightPx - Offset : screenY + Offset;
            _window.AppWindow.Move(new PointInt32(screenX, y));

            ShowWindow(_hWnd, ShowWindowCommands.SW_SHOWNOACTIVATE);
            // NOACTIVATEでフォーカスを奪わないまま、常に最前面(TOPMOST)へ出す
            SetWindowPos(_hWnd, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

            _outsideClickWatcher.Start(_hWnd);

            _isPointerOver = false;
            RestartAutoHideTimer();
        }

        private void RestartAutoHideTimer()
        {
            _autoHideTimer.Stop();

            if (_isPointerOver)
                return;

            var autoHideSeconds = Preferences.PreferencesGateway.GetSelectionToolbarAutoHideSeconds();
            if (autoHideSeconds > 0)
            {
                _autoHideTimer.Interval = TimeSpan.FromSeconds(autoHideSeconds);
                _autoHideTimer.Start();
            }
        }

        public void HideNoActivate()
        {
            _outsideClickWatcher.Stop();

            // アプリ終了処理(AppDomain.CurrentDomain.ProcessExit)からここへ到達すると、
            // その時点でUIスレッドのCOM apartmentが既に不安定になっており、
            // DispatcherTimer.Stop()/Window.Hide()等のWinRT呼び出しがCOMException
            // (0x8001010E、RPC_E_WRONG_THREAD)を投げることがある(実際に踏んだ不具合)。
            // プロセスごと終了する間際のベストエフォート処理のため、失敗しても実害は無く握りつぶす
            try
            {
                _autoHideTimer.Stop();
                _window.Hide();
            }
            catch (System.Runtime.InteropServices.COMException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ToolbarWindowHelper] HideNoActivate中にCOMExceptionを無視しました: {ex.Message}");
            }
        }
    }
}
