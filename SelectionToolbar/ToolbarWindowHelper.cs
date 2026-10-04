using Common.Extension;
using Common.Models;
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
        private int _widthDip;
        private readonly int _heightDip;
        private readonly OutsideClickWatcher _outsideClickWatcher = new();
        private readonly EscapeKeyWatcher _escapeKeyWatcher = new();
        private readonly DispatcherTimer _autoHideTimer = new();
        private bool _isPointerOver;

        // ShowAtのたびにMove直後のDPIで計算し直す(コンストラクタ時点ではまだどの
        // モニタにも実際に配置されていないため、そこでのDPIを信用できない)
        private int _widthPx;
        private int _heightPx;

        public ToolbarWindowHelper(WindowEx window, FrameworkElement rootElement, int widthDip, int heightDip)
        {
            _window = window;
            _hWnd = WindowNative.GetWindowHandle(_window);

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
            _escapeKeyWatcher.EscapePressed += (_, _) => HideNoActivate();

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

        /// <param name="widthDip">
        /// このタイミングでの表示幅(DIP)。省略時はコンストラクタで渡した幅、または
        /// 前回のShowAtで指定した幅のまま(SelectionToolbarWindowのように、ボタンの
        /// 表示有無に応じて呼び出し側が毎回幅を変えたい場合に指定する)。
        /// </param>
        public void ShowAt(int screenX, int screenY, int? widthDip = null)
        {
            if (widthDip is { } w)
                _widthDip = w;

            // 表示先モニタのDPIは、実際にそこへ配置してからでないと正しく取れない
            // (コンストラクタ時点ではまだどの画面にも属していない既定のDPIになってしまう)。
            // そのため、まず前回分かっている高さ(初回は0)でおおまかに配置し、
            // 実際のDPIを取得し直してからサイズと位置を確定する
            var placement = Preferences.PreferencesGateway.GetSelectionToolbarPlacement();

            _window.AppWindow.Move(CalculatePosition(placement, screenX, screenY, _widthPx, _heightPx));

            var dpi = WinUser.Dpi.GetDpiForWindow(_hWnd);
            var scale = dpi / 96.0;
            _widthPx = (int)(_widthDip * scale);
            _heightPx = (int)(_heightDip * scale);
            _window.AppWindow.Resize(new SizeInt32(_widthPx, _heightPx));

            _window.AppWindow.Move(CalculatePosition(placement, screenX, screenY, _widthPx, _heightPx));

            ShowWindow(_hWnd, ShowWindowCommands.SW_SHOWNOACTIVATE);
            // NOACTIVATEでフォーカスを奪わないまま、常に最前面(TOPMOST)へ出す
            SetWindowPos(_hWnd, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

            _outsideClickWatcher.Start(_hWnd);
            _escapeKeyWatcher.Start();

            _isPointerOver = false;
            RestartAutoHideTimer();
        }

        // 指定座標(マウスを離した位置)から見て、どちら側に出すかで左上の位置を決める。
        // 左右の時は、ツールバーの縦の中心を指定座標の高さに揃える
        private static PointInt32 CalculatePosition(ToolbarPlacement placement, int anchorX, int anchorY, int widthPx, int heightPx)
        {
            return placement switch
            {
                ToolbarPlacement.Below => new PointInt32(anchorX, anchorY + Offset),
                ToolbarPlacement.Left => new PointInt32(anchorX - widthPx - Offset, anchorY - heightPx / 2),
                ToolbarPlacement.Right => new PointInt32(anchorX + Offset, anchorY - heightPx / 2),
                _ => new PointInt32(anchorX, anchorY - heightPx - Offset),
            };
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
            _escapeKeyWatcher.Stop();

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
