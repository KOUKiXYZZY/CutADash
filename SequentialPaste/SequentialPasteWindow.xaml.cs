using Common.Extension;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using WinAPI;
using WinRT.Interop;
using WinUIEx;
using Windows.Graphics;
using static WinAPI.WinUser;
using ShowWindowCommands = WinAPI.ShowWindowCommands;

namespace SequentialPaste
{
    /// <summary>
    /// キューの中身を表示し、削除/開始・停止を行う常設パネル。
    /// SelectionToolbarのポップアップ(選択直後だけ出て自動的に隠れる)とは異なり、
    /// 明示的にトレイメニューから閉じるまで表示され続ける。
    ///
    /// 「開始」を押すと、以降はどのアプリ上で物理的にCtrl+Vを押しても
    /// (SequentialPasteHotkeyWatcher経由で検知)、そのたびにキューの先頭を1件ずつ
    /// 消費して貼り付ける。都度このウィンドウのボタンを押す必要はない。
    /// RegisterHotKeyではなく低レベルキーボードフック(WH_KEYBOARD_LL)を使う理由は
    /// SequentialPasteHotkeyWatcherのクラスコメントを参照(RegisterHotKeyは本物と合成の
    /// 入力を区別できず、貼り付け用の合成Ctrl+Vまで横取りしてしまうため)。
    ///
    /// WS_EX_NOACTIVATE(SelectionToolbar/MainWindowと同じ仕組み)を付けているため、
    /// このウィンドウをクリックしてもフォアグラウンドは変わらない。そのため貼り付け時の
    /// GetForegroundWindow()が、常に実際にペーストしたい相手のウィンドウになる
    /// (MainWindowのPreviousForegroundWindowのような記憶が不要)。
    /// </summary>
    public sealed partial class SequentialPasteWindow : WindowEx
    {
        private readonly SequentialPasteQueueViewModel _viewModel;
        private readonly IntPtr _hWnd;

        // MainWindow/PreferenceWindowと同じwindow_settings.jsonを使うため、キーを分ける
        private const string WindowSettingsKey = "SequentialPasteWindow";

        // ドラッグ/リサイズ中はAppWindow.Changedが連続で飛んでくるため、都度書き込むと
        // 負荷が大きい上、トレイの「終了」(Environment.Exit)で即座にプロセスが終わると
        // HideNoActivate側の保存すら間に合わない。そのため動きが止まってから書き込む
        // (MainWindowの_windowSettingsSaveTimerと同じ仕組み)
        private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _windowSettingsSaveTimer;
        private const int WindowSettingsSaveDelayMs = 500;

        public SequentialPasteWindow(SequentialPasteQueueViewModel viewModel)
        {
            InitializeComponent();

            _viewModel = viewModel;
            RootGrid.DataContext = _viewModel;
            _viewModel.QueueCompleted += () => DispatcherQueue.TryEnqueue(ShowQueueCompletedDialog);

            _hWnd = WindowNative.GetWindowHandle(this);

            var exStyle = GetWindowLong(_hWnd, GWL_EXSTYLE);
            SetWindowLong(_hWnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);

            _windowSettingsSaveTimer = DispatcherQueue.CreateTimer();
            _windowSettingsSaveTimer.Interval = TimeSpan.FromMilliseconds(WindowSettingsSaveDelayMs);
            _windowSettingsSaveTimer.IsRepeating = false;
            _windowSettingsSaveTimer.Tick += (s, e) =>
                this.SaveWindowSize<Common.Models.WindowSize>(key: WindowSettingsKey, savePosition: true);

            this.Title = "シーケンシャルペースト";

            // MainWindowと見た目を揃える(タイトルバー削除・角丸・設定画面と連動する背景素材)
            this.DwmTransitions(true);
            this.SetWindowCornerPreference(ResolveWindowCornerPreference());
            ApplyBackdropWithTintOverlay(Preferences.PreferencesGateway.GetWindowBackdrop());
            Preferences.PreferencesGateway.WindowBackdropChanged += kind =>
                this.DispatcherQueue.TryEnqueue(() => ApplyBackdropWithTintOverlay(kind));
            // ティントは明/暗で色を変えているため、実行中にシステムのテーマが変わったら選び直す
            RootGrid.ActualThemeChanged += (s, e) =>
                ApplyBackdropWithTintOverlay(Preferences.PreferencesGateway.GetWindowBackdrop());
            this.RemoveTitleBar();

            // ドラッグハンドルでウィンドウを移動できるようにする。サイズが変わった時も
            // ヒットテスト領域を更新する必要がある(MainWindowと同じ仕組み)
            DragHandle.Loaded += (s, e) => this.UpdateDragRegions(DragHandle);
            DragHandle.SizeChanged += (s, e) => this.UpdateDragRegions(DragHandle);
            AppWindow.Changed += (s, e) =>
            {
                if (e.DidSizeChange)
                    this.UpdateDragRegions(DragHandle);

                if (e.DidSizeChange || e.DidPositionChange)
                {
                    _windowSettingsSaveTimer.Stop();
                    _windowSettingsSaveTimer.Start();
                }
            };

            // 前回のサイズ・位置を復元する。保存が無ければ既定サイズにする
            var restored = this.RestoreWindowSize<Common.Models.WindowSize>(WindowSettingsKey);
            if (restored is null)
            {
                var dpi = WinUser.Dpi.GetDpiForWindow(_hWnd);
                var scale = dpi / 96.0;
                this.AppWindow.Resize(new SizeInt32((int)(320 * scale), (int)(400 * scale)));
            }

            // タイトルバーを消したため既定の閉じるボタンは無い。ESCキーとCloseButton_Click
            // (下記)からの経路だけで閉じる。AppWindow.Closingは念のため引き続きキャンセルし、
            // 完全に破棄されず隠すだけになるようにしておく(次回Toggle()でまた開ける)
            this.Content.KeyDown += (sender, e) =>
            {
                if (e.Key == Windows.System.VirtualKey.Escape)
                    HideNoActivate();
            };

            this.AppWindow.Closing += (s, e) =>
            {
                e.Cancel = true;
                this.SaveWindowSize<Common.Models.WindowSize>(key: WindowSettingsKey, savePosition: true);
                s.Hide();
            };
        }

        /// <summary>
        /// バックドロップを適用し、Blur選択時だけルート要素に半透明のティントを重ねる。
        /// MainWindow.ApplyBackdropWithTintOverlayと同じ理由(Blurは生のぼかしだけで
        /// 色味が無く、背後が白い時などに文字が見えなくなることがあるため)。
        /// </summary>
        private void ApplyBackdropWithTintOverlay(Common.Models.WindowBackdropKind kind)
        {
            this.ApplyBackdrop(kind, alwaysActive: true);

            // Application.Current.RequestedThemeは起動時の既定値のまま更新されず、実行中の
            // システムテーマ変更に追従しない(MainWindowと同じ理由)。ActualThemeを見る
            var isDark = RootGrid.ActualTheme == ElementTheme.Dark;

            RootGrid.Background = kind switch
            {
                Common.Models.WindowBackdropKind.Blur => isDark
                    ? new SolidColorBrush(Windows.UI.Color.FromArgb(80, 32, 32, 32))
                    : new SolidColorBrush(Windows.UI.Color.FromArgb(80, 243, 243, 243)),

                Common.Models.WindowBackdropKind.Cat
                    => new SolidColorBrush(Windows.UI.Color.FromArgb(190, 255, 213, 179)),

                // Mica/Acrylic: ライトの時だけ薄く白を重ねて、もう少し明るく見せる
                _ when !isDark => new SolidColorBrush(Windows.UI.Color.FromArgb(60, 255, 255, 255)),

                _ => null
            };
        }

        /// <summary>
        /// ウィンドウの角丸(DWM_WINDOW_CORNER_PREFERENCE)をApp.xamlのAppWindowCornerPreference
        /// (文字列リソース。メンバー名をそのまま指定する)から読み取る。MainWindowと同じ定義を
        /// 共有する(Applicationは1プロセスに1つなので、別プロジェクトのこのウィンドウからでも
        /// CutADash本体のApp.xamlのリソースを読める)。未定義や不正な値なら角を丸くしない。
        /// </summary>
        private static DwmAPI.DWM_WINDOW_CORNER_PREFERENCE ResolveWindowCornerPreference()
        {
            const DwmAPI.DWM_WINDOW_CORNER_PREFERENCE fallback = DwmAPI.DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_DONOTROUND;

            if (Application.Current.Resources.TryGetValue("AppWindowCornerPreference", out var value)
                && value is string name
                && Enum.TryParse<DwmAPI.DWM_WINDOW_CORNER_PREFERENCE>(name, out var preference))
            {
                return preference;
            }

            return fallback;
        }

        // キューの末尾まで貼り付け終えたことを知らせるダイアログ(NOACTIVATEでも
        // フォアグラウンドを奪わないウィンドウなので、こちらから見えるように出す)
        private async void ShowQueueCompletedDialog()
        {
            var dialog = new ContentDialog
            {
                Title = "完了",
                Content = "キューの末尾まで貼り付けました。",
                CloseButtonText = "OK",
                XamlRoot = RootGrid.XamlRoot
            };

            await dialog.ShowAsync();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => HideNoActivate();

        public void ShowNoActivate()
        {
            ShowWindow(_hWnd, ShowWindowCommands.SW_SHOWNOACTIVATE);
            SetWindowPos(_hWnd, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        public void HideNoActivate()
        {
            _windowSettingsSaveTimer.Stop();
            this.SaveWindowSize<Common.Models.WindowSize>(key: WindowSettingsKey, savePosition: true);
            this.Hide();
        }

        public bool IsVisible => this.AppWindow.IsVisible;
    }
}
