
using Common.Extension;
using CutADash.Infra.Win32;
using CutADash.Utils;
using CutADash.ViewModels;
using CutADash.Views.Emoji;
using CutADash.Views.Contents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using WinAPI;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;
using Windows.UI.ApplicationSettings;
using WinRT.Interop;
using WinUIEx;


// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CutADash.Views
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : WindowEx
    {
        private Dictionary<string, Type> pages = new() {
            { "history", typeof(Contents.Contents) },
            { "favorite", typeof(Contents.Contents) },
            { "emoji", typeof(Emoji.Emoji) }
        };

        // タブごとにListFrameへ表示する一覧ページを割り当てる
        private Dictionary<string, Type> listPages = new() {
            { "history", typeof(ListFrame.ListFrame) },
            { "favorite", typeof(ListFrame.FavoriteListFrame) },
            { "emoji", typeof(ListFrame.EmojiListFrame) },
        };

        public bool AllowClose { get; set; } = false;

        // App.xaml.cs側でDIコンテナ構築後にセットされる。ClipboardListViewModelなど
        // 複数箇所で共有したいサービスをここから解決する。
        public ServiceProvider? Provider { get; set; }

        // このウィンドウをアクティブ化する直前にフォアグラウンドだったウィンドウ。
        // History一覧でEnterを押したときのペースト先として使う(App.xaml.cs側でセットする)。
        public IntPtr PreviousForegroundWindow { get; set; }

        /// <summary>
        /// PreviousForegroundWindow内で実際にキーボードフォーカスを持っていた子コントロール
        /// (MDI/タブ構成のアプリではフレームウィンドウ自体とは別)。サクラエディタ等、
        /// フレームをフォアグラウンド化しただけでは編集領域にフォーカスが戻らないアプリのために、
        /// ペースト時に明示的にSetFocusし直す(Dittoのm_focusWndと同じ発想)
        /// </summary>
        public IntPtr PreviousFocusWindow { get; set; }

        // ContentFrame表示中の最小幅(DIP)。これを下回ったらListFrame+NavigationViewのみの
        // コンパクトレイアウトに切り替える
        private const double CompactWidthThreshold = 500;
        private bool _isCompact;

        // ContentSizerでドラッグして付いたListFrame.Widthの明示値。
        // コンパクト表示への切り替え中はこれを退避してWidthをクリアし、全幅表示できるようにする。
        private double? _savedListFrameWidth;

        private string? _currentTag;

        // ウィンドウ移動中はAppWindow.Changedが連続で飛んでくるため、その都度
        // ディスクへ書き込むと負荷が大きい。動きが止まってから書き込む(遅延書き込み)
        private readonly DispatcherQueueTimer _windowSettingsSaveTimer;
        private const int WindowSettingsSaveDelayMs = 500;

        public MainWindow()
        {
            InitializeComponent();

            _windowSettingsSaveTimer = DispatcherQueue.CreateTimer();
            _windowSettingsSaveTimer.Interval = TimeSpan.FromMilliseconds(WindowSettingsSaveDelayMs);
            _windowSettingsSaveTimer.IsRepeating = false;
            _windowSettingsSaveTimer.Tick += (s, e) => SaveWindowSettings();

            HistoryNavItem.Content = Common.Utils.AppStrings.Get("Nav_History");
            FavoriteNavItem.Content = Common.Utils.AppStrings.Get("Nav_Favorite");
            EmojiNavItem.Content = Common.Utils.AppStrings.Get("Nav_Emoji");

            // ウィンドウデザインのカスタマイズ

            this.AppWindow.IsShownInSwitchers = false; // Alt+Tab �Ŕ�\��

            // 最大化を無効化する(Windowsの「端へドラッグすると広がる」スナップ機能は、
            // 最大化可能なウィンドウにのみ働くため、これで対象外にできる)。
            // リサイズ自体は引き続き可能なままにしたいのでIsResizableはtrueのままにする
            if (this.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            {
                presenter.IsMaximizable = false;
            }

            this.DwmTransitions(true); // ウィンドウの表示/非表示にアニメーションをつける
            this.SetWindowCornerPreference(ResolveWindowCornerPreference()); // 角丸(App.xamlのAppWindowCornerPreferenceで変更可)
            // this.DisableAccentBorder(); // アクティブウィンドウの境界線を消す

           // 種類(Mica/Acrylic/Blur)はPreferenceWindowの「テーマ」タブで選べる
            ApplyBackdropWithTintOverlay(Preferences.PreferencesGateway.GetWindowBackdrop());
            Preferences.PreferencesGateway.WindowBackdropChanged += kind =>
                this.DispatcherQueue.TryEnqueue(() => ApplyBackdropWithTintOverlay(kind));
            this.RemoveTitleBar(); // タイトルバーを消す

            this.Activated += MainWindow_Activated;
            this.Closed += MainWindow_Hide;

            // GPUデバイスロスト等でコンポジション面が失われると、既にデコード済みの
            // BitmapImage/SoftwareBitmapSource(絵文字・Navアイコンとも)がキャッシュには
            // 残ったまま描画だけ消えてしまう不具合があった(実際に踏んだ不具合)。
            // SurfaceContentsLostを受けてキャッシュを破棄し、Navアイコンと表示中のEmojiページの
            // スプライト画像を読み直させる
            Microsoft.UI.Xaml.Media.CompositionTarget.SurfaceContentsLost += (_, _) =>
            {
                Utils.EmojiSpriteAssets.ClearCache();
                UpdateNavIcons();
                if (ContentFrame?.Content is Views.Emoji.Emoji emojiPage)
                    emojiPage.RefreshSpriteImages();
            };


            // ESCキーでウィンドウを閉じる。
            this.Content.KeyDown += (sender, e) =>
            {
                if (e.Key == Windows.System.VirtualKey.Escape)
                {
                    HidePalette();
                }
            };

            // ウィンドウのDragHandleをドラッグすることでウィンドウの移動をさせる。
            // またウィンドウサイズ変更時にはドラッグの領域を変更する
            DragHandle.Loaded += (s, e) => { this.UpdateDragRegions(DragHandle); };
            DragHandle.SizeChanged += (s, e) => { this.UpdateDragRegions(DragHandle); };
            AppWindow.Changed += (s, e) =>
            {
                if (e.DidSizeChange)
                {
                    this.UpdateDragRegions(DragHandle);
                }

                // ドラッグ操作の途中経過を毎回保存する必要はないため、動きが止まって
                // 一定時間タイマーが再始動しなかったタイミングでまとめて書き込む
                if (e.DidPositionChange)
                {
                    _windowSettingsSaveTimer.Stop();
                    _windowSettingsSaveTimer.Start();
                }
            };

            // ウィンドウが縮小されたらContentFrameを隠し、ListFrame+NavigationViewだけのレイアウトに切り替える
            RootContentGrid.Loaded += (s, e) => UpdateLayout();
            RootContentGrid.SizeChanged += (s, e) => UpdateLayout();

            // 未選択アイコンの淵の色はテーマごとに別ファイルなので、テーマが変わったら選び直す。
            // 起動直後もXAMLの初期値(ダーク版)のままなので、ここで実際のテーマに合わせる
            RootGrid.Loaded += (s, e) =>
            {
                UpdateNavIcons();

                // DPI(モニター間の移動・システムのスケール変更)が変わった時もアイコンを
                // 選び直す。テーマ/選択状態が変わらないままDPIだけ変わるケースは
                // ActualThemeChanged/SelectionChangedのどちらも発火しないため、
                // 別途XamlRoot.Changedで検知する
                var xamlRoot = RootGrid.XamlRoot;
                if (xamlRoot is not null)
                {
                    var lastScale = xamlRoot.RasterizationScale;
                    xamlRoot.Changed += (sender, args) =>
                    {
                        if (sender.RasterizationScale != lastScale)
                        {
                            lastScale = sender.RasterizationScale;
                            UpdateNavIcons();
                        }
                    };
                }
            };
            RootGrid.ActualThemeChanged += (s, e) => UpdateNavIcons();

            // Blur/Catのティントは明/暗テーマで色を変えているため、実行中にシステムの
            // テーマが変わったら選び直す
            RootGrid.ActualThemeChanged += (s, e) =>
                ApplyBackdropWithTintOverlay(Preferences.PreferencesGateway.GetWindowBackdrop());
        }

        /// <summary>
        /// バックドロップを適用し、Blur選択時だけルート要素に半透明のティントを重ねる。
        /// Blurは生のぼかしだけで色味が無く、背後が白い時などにNavigationViewの文字が
        /// 見えなくなることがあるため、Mica/Acrylicとは違いXAML側でコントラストを補う
        /// (Composition側でのブレンドはWin2Dが無いこの環境では使えなかった)。
        /// </summary>
        private void ApplyBackdropWithTintOverlay(Common.Models.WindowBackdropKind kind)
        {
            this.ApplyBackdrop(kind, alwaysActive: true);

            // Application.Current.RequestedThemeは起動時の既定値のまま更新されず、実行中に
            // システムのテーマ(明/暗)が変わっても追従しない。実際に効いている見た目の
            // テーマを見るには、要素のActualTheme(ActualThemeChangedで追従する)を使う
            var isDark = RootGrid.ActualTheme == ElementTheme.Dark;

            RootGrid.Background = kind switch
            {
                Common.Models.WindowBackdropKind.Blur => isDark
                    ? new SolidColorBrush(Windows.UI.Color.FromArgb(80, 32, 32, 32))
                    : new SolidColorBrush(Windows.UI.Color.FromArgb(80, 243, 243, 243)),

                // 猫テーマ: Micaの上に、暖色のパステル(クリーム/ピーチ)なティントを重ねる。
                // 明/暗テーマで色を変えず同じ固定色にしているが、Mica自体の地の色は
                // OSのテーマに応じて変わる(WinUIの仕様上ここでは打ち消せない)ため、
                // 完全な統一はできない。alphaを上げてMicaの地をできるだけ覆い隠し、
                // 差が目立たないようにしている
                Common.Models.WindowBackdropKind.Cat
                    => new SolidColorBrush(Windows.UI.Color.FromArgb(190, 255, 213, 179)),

                _ => null
            };

            var isCatTheme = kind == Common.Models.WindowBackdropKind.Cat;

            // 猫テーマの間は、設定(AppWindowCornerPreference)に関わらず
            // 一番丸いDWMWCP_ROUNDを強制する。それ以外は通常通り設定値に従う
            this.SetWindowCornerPreference(isCatTheme
                ? DwmAPI.DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND
                : ResolveWindowCornerPreference());
        }

        /// <summary>
        /// ウィンドウの角丸(DWM_WINDOW_CORNER_PREFERENCE)をApp.xamlのAppWindowCornerPreference
        /// (文字列リソース。メンバー名をそのまま指定する)から読み取る。
        /// 未定義や不正な値の場合は、角を丸くしない(DWMWCP_DONOTROUND)を既定にする。
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

        /// <summary>
        /// フォーカスを奪わずにウィンドウを表示する(ホットキーからの表示はこちらを使う)。
        /// 表示中はキー入力・マウス外クリック・フォアグラウンド変化の監視を始める。
        /// </summary>
        public void ShowActivate()
        {
            RestoreWindowSizeOnce();

            this.Activate();
            this.ForceForeground();
            this.SetTopMost(true);

            Utils.GarbageCollectionHelper.OnWindowShown();
        }

        /// <summary>パレットを隠し、各種監視も止める。次回表示に備えて状態をリセットする。</summary>
        public void HidePalette()
        {
            this.SetTopMost(false);
            this.Hide();
            Utils.GarbageCollectionHelper.OnWindowHidden();
        }

        // 他アプリがフォアグラウンドになったらパレットを閉じる。
        // パレットを出した時点の相手(ペースト先)は前面のままなので、そのままなら閉じない。
        // 自分自身(検索モードでフォーカスを取った場合)も対象外
        /// <summary>
        /// ペースト実行中(ForegroundPasteHelper.PasteToPreviousWindowAsync)はtrue。
        /// サクラエディタ等のMDIアプリでは、フォアグラウンド復帰時に想定先とは別の
        /// ウィンドウハンドルでEVENT_SYSTEM_FOREGROUNDが発火することがあり、
        /// OnForegroundChangedが誤ってHidePalette()を呼んでCtrl+V送信を
        /// 中断させてしまう。ペースト中はこの監視を無視する
        /// </summary>
        public bool IsPasting { get; set; }

        /// <summary>
        /// アクティブ化する直前のフォアグラウンドウィンドウを、ペースト先として自動的に
        /// 記憶してからアクティブ化する。以前はこれをApp.xaml.cs側のActivateMainWindow
        /// ヘルパーが個別に行っており、それを経由せず素朴にActivate()を呼んだ場所
        /// (タスクトレイの整理中など)では記憶が漏れてペーストが効かなくなっていた。
        /// 呼び出し元に依存せず常に正しく動くよう、Activate()自体に組み込む
        ///
        /// 注意: Microsoft.UI.Xaml.Window.Activate()はvirtualではないため、これは
        /// オーバーライドではなくnewによる隠蔽になる。MainWindow型の変数/thisを
        /// 経由した呼び出しでのみこちらが呼ばれ、Window型の変数越しに呼ぶと
        /// 基底のActivate()がそのまま呼ばれる点に注意
        /// (呼び出し元は基本的にMainWindow型を保持するようにしてあるため、影響はない)
        /// </summary>
        public new void Activate()
        {
            RememberPasteTarget(WinAPI.WinUser.GetForegroundWindow());
            base.Activate();
        }

        /// <summary>
        /// アクティブ化する時点のフォアグラウンドウィンドウを、ペースト先として記憶する。
        ///
        /// 自分自身のウィンドウは記憶しない。ホットキー連打やタブ切り替えでの再呼び出し時は
        /// フォアグラウンドが既にMainWindow自身になっていることがあり、それを記憶すると
        /// 貼り付け先が自分自身になってEnterを押しても何も起きなくなるため。
        ///
        /// 以前はこれを「非表示→表示の遷移の時だけ記憶する」という表示状態での判定で
        /// 回避していたが、表示済みの状態から呼ばれ続けると一度も記憶されないまま
        /// PreviousForegroundWindowがIntPtr.Zeroで固定され、クリップボードへの書き戻しは
        /// 成功するのにキー送信が一切行われない(=コピーはされるが貼り付かない)状態に
        /// 陥っていた。表示状態ではなく「自分のプロセスのウィンドウか」で判定することで、
        /// 自己記憶を防ぎつつ、外部アプリが前面にいる限り常に最新の相手を記憶できる
        /// (Dittoも同様に、自分のウィンドウがフォアグラウンドの間は追跡を更新しない)
        /// </summary>
        private void RememberPasteTarget(IntPtr foregroundWindow)
        {
            if (foregroundWindow == IntPtr.Zero || IsOwnWindow(foregroundWindow))
                return;

            PreviousForegroundWindow = foregroundWindow;
            PreviousFocusWindow = ForegroundPaster.GetFocusedChildWindow(foregroundWindow);
        }

        /// <summary>指定したウィンドウが自分のプロセスのものかどうか。</summary>
        private static bool IsOwnWindow(IntPtr hWnd)
        {
            WinAPI.WinUser.GetWindowThreadProcessId(hWnd, out var processId);
            return processId == (uint)Environment.ProcessId;
        }

        /// <summary>タブ切り替えをキャンセルして戻す際に、Tag文字列から元のNavigationViewItemを探す。</summary>
        private NavigationViewItem? FindNavItemByTag(string? tag)
        {
            if (tag is null)
                return null;

            foreach (var menuItem in NavView.MenuItems)
            {
                if (menuItem is NavigationViewItem navItem && navItem.Tag?.ToString() == tag)
                    return navItem;
            }

            return null;
        }

        // 初回の表示でだけ、前回のウィンドウサイズを復元する
        private bool _hasRestoredWindowSize;

        // ウィンドウが一度も表示されていない状態(コンストラクタ内など)でAppWindowを
        // リサイズしても、実際に表示されるタイミングでOS側の初期配置に上書きされ、
        // 毎回既定サイズに戻ってしまうことがあった。実際に表示するこのタイミングで復元する。
        //
        // 保存された位置も一緒に復元するため、呼び出し側(App.xaml.cs)はキャレット位置への
        // MoveToCaretより前にこれを呼ぶこと。順序を逆にすると、キャレットへ合わせた直後に
        // 前回位置で上書きされてしまう
        public void RestoreWindowSizeOnce()
        {
            if (_hasRestoredWindowSize)
                return;

            _hasRestoredWindowSize = true;
            var restored = this.RestoreWindowSize<Common.Models.MainWindowSize>();

            // 保存後にMinWidth/MinHeightが変わった場合や、想定しない値が
            // 保存されていた場合に備え、最低サイズを下回っていたら引き上げる
            if (restored is not null)
                ClampWindowSizeToMinimum();

            if (restored?.ListFrameWidth is double listFrameWidth)
                ListFrame.Width = Math.Max(listFrameWidth, ListFrame.MinWidth);
        }

        private void MainWindow_Activated(object sender, WindowActivatedEventArgs e)
        {
            // 他アプリへ切り替わった時にパレットを閉じる処理は、ここ(=WM_ACTIVATE相当)では
            // 行わない。自分でActivate()/SetForegroundWindow()を呼んでいる最中の過渡的な
            // Deactivatedまで拾ってしまい、表示した瞬間に閉じてしまうため。
            // アプリ境界をまたぐ切り替わりだけを拾うWM_ACTIVATEAPPで判定している(App.xaml.cs)
            if (e.WindowActivationState == WindowActivationState.CodeActivated ||
                e.WindowActivationState == WindowActivationState.PointerActivated)
            {
                RestoreWindowSizeOnce();
                Utils.GarbageCollectionHelper.OnWindowShown();
            }
        }

        // RestoreWindowSizeでAppWindowを直接リサイズしているため、ドラッグ時のように
        // WindowExのMinWidth/MinHeight制約が自動で効かない。復元後に自前でクランプする。
        // AppWindow.Sizeは物理ピクセル、MinWidth/MinHeightはDIPなのでDPIスケールを掛けて揃える
        private void ClampWindowSizeToMinimum()
        {
            var hWnd = WindowNative.GetWindowHandle(this);
            var dpi = WinAPI.WinUser.Dpi.GetDpiForWindow(hWnd);
            var scale = dpi / 96.0;

            var minWidth = (int)(this.MinWidth * scale);
            var minHeight = (int)(this.MinHeight * scale);

            var size = AppWindow.Size;
            var width = Math.Max(size.Width, minWidth);
            var height = Math.Max(size.Height, minHeight);

            if (width != size.Width || height != size.Height)
                AppWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
        }

        // キャレットとウィンドウの間に少し隙間を空ける
        private const int CaretGap = 4;

        /// <summary>
        /// ウィンドウを、指定したキャレット位置(GetGUIThreadInfo+ClientToScreenで得た
        /// 物理ピクセルの画面座標。AppWindowの座標系と一致する)のすぐ下・左揃えに移動する。
        /// 画面(作業領域)からはみ出す場合は、収まるように位置を調整する。
        /// </summary>
        public void MoveToCaret(Rect caretRect)
        {
            var width = AppWindow.Size.Width;
            var height = AppWindow.Size.Height;

            var x = (int)caretRect.X;
            var belowY = (int)(caretRect.Y + caretRect.Height) + CaretGap;

            var displayArea = DisplayArea.GetFromPoint(new PointInt32(x, belowY), DisplayAreaFallback.Nearest);
            var workArea = displayArea.WorkArea;

            // 下に置くのが基本だが、画面下端に近くてはみ出す場合はそのままだと
            // 単純にクランプされてキャレットの上に覆い被さってしまう。
            // 上に置いても収まるなら、キャレットの上へ出してそちらを優先する
            int y;
            if (belowY + height <= workArea.Y + workArea.Height)
            {
                y = belowY;
            }
            else
            {
                var aboveY = (int)caretRect.Y - CaretGap - height;
                y = aboveY >= workArea.Y ? aboveY : belowY;
            }

            // 右にはみ出す場合は収まる位置まで戻す
            x = Math.Min(x, workArea.X + workArea.Width - width);
            x = Math.Max(x, workArea.X);

            // 上下どちらに置いても画面に収まりきらない場合の最終手段としてクランプする。
            // この場合だけはキャレットに重なる可能性がある
            y = Math.Min(y, workArea.Y + workArea.Height - height);
            y = Math.Max(y, workArea.Y);

            AppWindow.Move(new PointInt32(x, y));
        }

        /// <summary>
        /// 現在のウィンドウサイズ・位置・セパレータ位置をwindow_settings.jsonへ書き込む。
        /// コンパクト表示中はListFrame.Widthがウィンドウ幅に固定されてしまっているため、
        /// その場合は退避してある通常表示時のドラッグ幅(_savedListFrameWidth)を使う。
        /// </summary>
        private void SaveWindowSettings()
        {
            var listFrameWidth = _isCompact
                ? _savedListFrameWidth
                : (double.IsNaN(ListFrame.Width) ? (double?)null : ListFrame.Width);
            this.SaveWindowSize<Common.Models.MainWindowSize>(savePosition: true,
                configure: data => data.ListFrameWidth = listFrameWidth);
        }

        private void MainWindow_Hide(object sender, WindowEventArgs e)
        {
            if (AllowClose)
            {
                // 遅延書き込みのタイマーが残っていても、閉じる時点の最新状態をここで
                // 直接保存するので待つ必要はない
                _windowSettingsSaveTimer.Stop();
                SaveWindowSettings();
                return;
            }

            HidePalette();
            e.Handled = true;
        }

        /// <summary>
        /// ホットキーなどから、指定したタブ(history/favorite/emoji)を選択状態にする。
        /// selectSecondItemがtrueで、かつListFrame(履歴一覧)が表示されている場合、
        /// 一覧の2番目の項目を選択状態にする。
        /// </summary>
        public void NavigateToTab(string tag, bool selectSecondItem = false)
        {
            foreach (var menuItem in NavView.MenuItems)
            {
                if (menuItem is NavigationViewItem item && (string)item.Tag == tag)
                {
                    NavView.SelectedItem = item;
                    break;
                }
            }

            // ListFrameの2番目の項目を選択状態にする
            if (selectSecondItem && ListFrame.Content is Views.ListFrame.ListFrame listFramePage)
            {
                listFramePage.SelectItemAtIndex(1);
            }

            // Emojiタブの場合、先頭の絵文字セルにキーボードフォーカスを移す
            // (History一覧と違いヘッダー行が無いため、インデックスは0が先頭になる)
            if (tag == "emoji" && ContentFrame.Content is Emoji.Emoji emojiPage)
            {
                emojiPage.SelectItemAtIndex(0);
            }
        }

        /// <summary>
        /// NavigationViewの各アイコンを選択状態に合わせて切り替える。
        /// 塗りと淵の重ね合わせはPNG側で済ませてあるので、ここではSourceを選ぶだけ。
        /// 未選択時の淵の色はテーマごとに別ファイルなので、実際のテーマに合わせて選ぶ。
        /// ImageIconごと差し替えると要素の作り直しでちらつくため、Sourceだけを入れ替える。
        /// </summary>
        private void UpdateNavIcons()
        {
            // _navIconCacheはassetName(アイコン名+選択状態+テーマ)だけをキーにしており、
            // DPI(必要なピクセル数)が変わっても同じキーのままキャッシュを再利用してしまう。
            // DPI変更後にテーマ切り替えやタブクリックでUpdateNavIconsが呼ばれた時、
            // 古いDPIでデコード済みの画像がそのまま返り続け、新しいDPIでの
            // 表示サイズと噛み合わずアイコンが正しく表示されない(消えて見える)ことがあった
            // ため、毎回作り直す(数個・軽量なので都度作り直しても問題ない)
            _navIconCache.Clear();

            // GetNavIconAsyncは画像のデコードを伴うため非同期だが、呼び出し元(テーマ変更/
            // DPI変更/タブ選択のイベントハンドラ)を全てasyncにするほどではないため、
            // ここでfire-and-forgetする
            _ = ApplyNavIconAsync(HistoryNavIcon, HistoryNavItem, "History");
            _ = ApplyNavIconAsync(FavoriteNavIcon, FavoriteNavItem, "Favorite");
            _ = ApplyNavIconAsync(EmojiNavIcon, EmojiNavItem, "Emoji");
        }

        private async Task ApplyNavIconAsync(ImageIcon icon, NavigationViewItem item, string name)
        {
            var isDark = RootGrid.ActualTheme == ElementTheme.Dark;
            var state = item.IsSelected ? "Selected" : "Unselected";
            var assetName = $"{name}{state}{(isDark ? "Dark" : "Light")}";

            var source = await GetNavIconAsync(assetName);

            if (!ReferenceEquals(icon.Source, source))
                icon.Source = source;
        }

        // NavigationViewItemのImageIconを表示しているサイズ(DIP)。MainWindow.xaml側の
        // ImageIcon.Width/Heightと合わせること
        private const double NavIconDisplaySizeDip = 16;

        // AssetsSrc/Nav/Source のSVGから同フォルダのrender-nav-icons.ps1で描き起こしてある
        // サイズ。拡大率 100% / 150% / 200% / 300% で必要になる物理ピクセル数に対応する。
        // ファイル数を減らすため、1アイコンにつきこの並び順で縦に結合した1枚のPNG
        // (Assets/Nav/{assetName}.png、幅48px)になっている。並び順を変える場合は
        // render-nav-icons.ps1側も合わせて直すこと
        private static readonly int[] NavIconAssetSizes = { 16, 24, 32, 48 };

        private readonly Dictionary<string, ImageSource> _navIconCache = new();

        /// <summary>
        /// 表示先のDPIに合ったサイズを、結合済みPNG(Assets/Nav/{assetName}.png)から
        /// 該当するY位置だけ切り出してデコードする。
        ///
        /// 以前は48pxのPNG1枚を DecodePixelWidth=48 で読み込んで16x16 DIPへ流し込んでいたが、
        /// 100%スケールでは48px→16pxの3:1縮小を描画時にコンポジタが潰すことになり、
        /// 輪郭がボケていた。必要な物理ピクセル数以上で最も小さいタイルを選び、
        /// デコード時点でちょうどのサイズにしておくことで、等倍で描画されるようにする。
        /// </summary>
        private async Task<ImageSource> GetNavIconAsync(string assetName)
        {
            if (_navIconCache.TryGetValue(assetName, out var cached))
                return cached;

            var hWnd = WindowNative.GetWindowHandle(this);
            var scale = WinAPI.WinUser.Dpi.GetDpiForWindow(hWnd) / 96.0;
            var requiredPx = (int)Math.Ceiling(NavIconDisplaySizeDip * scale);

            // 125%のように用意したサイズと一致しない拡大率では、それを上回る最小のタイルを
            // 選んでデコード時に縮める(拡大は輪郭が甘くなるので避ける)
            var assetSize = NavIconAssetSizes.FirstOrDefault(size => size >= requiredPx);
            if (assetSize == 0)
                assetSize = NavIconAssetSizes[^1];

            // 結合PNG内でのY位置(自分より小さいサイズのタイルの高さの合計)
            uint yOffset = 0;
            foreach (var size in NavIconAssetSizes)
            {
                if (size == assetSize)
                    break;
                yOffset += (uint)size;
            }

            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Nav", $"{assetName}.png");

            using var stream = System.IO.File.OpenRead(path).AsRandomAccessStream();
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
            var transform = new Windows.Graphics.Imaging.BitmapTransform
            {
                Bounds = new Windows.Graphics.Imaging.BitmapBounds
                {
                    X = 0,
                    Y = yOffset,
                    Width = (uint)assetSize,
                    Height = (uint)assetSize,
                },
            };
            var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                transform,
                Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation,
                Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage);

            var source = new SoftwareBitmapSource();
            await source.SetBitmapAsync(softwareBitmap);

            _navIconCache[assetName] = source;
            return source;
        }

        // タブ切り替えを(確認ダイアログのキャンセル等で)元に戻している間、
        // それ自体がSelectionChangedを再度誘発して確認ループになるのを防ぐガード
        private bool _isRevertingTabSelection;

        private async void NavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (_isRevertingTabSelection)
                return;

            if (args.SelectedItem is not NavigationViewItem item)
                return;

            // タブを切り替えるとContentsページ自体が新しく作り直され、古いインスタンスは
            // 破棄される(下のNavigate呼び出し)。編集中の未保存の変更が消えてしまうため、
            // 切り替える前に確認する。キャンセルされたら選択を元のタブへ戻す
            if (ContentFrame.Content is Contents.Contents outgoingContentsForConfirm
                && !await outgoingContentsForConfirm.ConfirmDiscardChangesAsync())
            {
                _isRevertingTabSelection = true;
                var previousItem = FindNavItemByTag(_currentTag);
                if (previousItem is not null)
                    sender.SelectedItem = previousItem;
                _isRevertingTabSelection = false;
                return;
            }

            UpdateNavIcons();

            var tag = item.Tag.ToString();
            _currentTag = tag;

            // NavigationViewItem.Contentは"History"等の表示名そのものなので、
            // そのままタイトルバーにも出す
            CurrentTabTitle.Text = item.Content?.ToString() ?? string.Empty;

            if (pages.TryGetValue(tag, out var pageType))
            {
                // 表示中の画像を破棄せずにページごと捨てると、GCされるまで
                // メモリに残り続けてしまうため、離れる前に明示的に破棄する
                if (ContentFrame.Content is Contents.Contents outgoingContents)
                {
                    outgoingContents.ReleaseImage();
                }
                else if (ContentFrame.Content is Emoji.Emoji outgoingEmoji)
                {
                    // 絵文字グリッドが抱えるコレクション・ItemsSourceを切り離してから捨てる
                    outgoingEmoji.ReleaseData();
                }

                var parameter = BuildContentFrameParameter(pageType);

                // Emoji/Contentsはクリック時やペースト先として使うMainWindowが必須のため、
                // nullのまま(=MainWindowを渡し損ねた状態で)Navigateしてしまわないようにする
                var requiresParameter = pageType == typeof(Emoji.Emoji) || pageType == typeof(Contents.Contents);
                if (parameter is null && requiresParameter)
                {
                    System.Diagnostics.Debug.WriteLine($"[MainWindow] BuildContentFrameParameterがnullを返しました: {pageType}");
                }
                else
                {
                    ContentFrame.NavigateWithoutAnimation(pageType, parameter);

                    // Back/Forwardナビゲーションを使わないため、古いページ参照を貯め込むだけの
                    // Back/ForwardStackは都度クリアする
                    ContentFrame.BackStack.Clear();
                    ContentFrame.ForwardStack.Clear();
                }
            }

            if (listPages.TryGetValue(tag, out var listPageType))
            {
                // ListFrame側のページにContentFrameを渡し、一覧から選択した項目を
                // ContentFrameへ直接Navigateできるようにする
                ListFrame.NavigateWithoutAnimation(listPageType, BuildListFrameParameter(listPageType));
                ListFrame.BackStack.Clear();
                ListFrame.ForwardStack.Clear();
            }

            UpdateLayout();

            // ListFrame/ContentFrameを丸ごとNavigateし直すため、それまでフォーカスを
            // 持っていた要素(一覧の項目など)は破棄され、フォーカスの枠が消えてしまう。
            // 切り替えたNavigationViewItem自身は破棄されず残るので、そこへ戻す
            item.Focus(FocusState.Keyboard);
        }



        // MainPage.xaml.cs
        private void NavView_Loaded(object sender, RoutedEventArgs e)
        {
            var navView = sender as NavigationView;

            var splitView = FindChildOfType<SplitView>(navView);
            if (splitView != null)
            {
                splitView.CornerRadius = new CornerRadius(0);
            }

            var paneContentGrid = FindChildByName(navView, "PaneContentGrid") as Grid;
            if (paneContentGrid != null)
            {
                paneContentGrid.CornerRadius = new CornerRadius(0);
            }

            EnableTabThroughMenuItems(navView);

            ContentFrame.NavigateWithoutAnimation(typeof(Contents.Contents), new Contents.ContentsNavigationParameter { MainWindow = this });
            ListFrame.NavigateWithoutAnimation(typeof(ListFrame.ListFrame), BuildListFrameParameter(typeof(ListFrame.ListFrame)));
            ContentFrame.BackStack.Clear();
            ListFrame.BackStack.Clear();
        }

        // Emoji/Contentsは、クリック時やエンコード結果のペースト先として使うMainWindowを必要とするため渡す。
        // それ以外のページはパラメータ不要
        private object? BuildContentFrameParameter(Type pageType)
        {
            if (pageType == typeof(Emoji.Emoji))
                return new Emoji.EmojiNavigationParameter { MainWindow = this };

            if (pageType == typeof(Contents.Contents))
                return new Contents.ContentsNavigationParameter { MainWindow = this };

            return null;
        }

        // ListFrame(履歴一覧)へは、DIで共有しているClipboardListViewModelをContentFrameと
        // セットにして渡す。それ以外のタブ(Favorite/Emoji)は今まで通りContentFrameだけ渡す。
        private object BuildListFrameParameter(Type listPageType)
        {
            if (listPageType == typeof(ListFrame.ListFrame) && Provider is not null)
            {
                return new ListFrame.ListFrameNavigationParameter
                {
                    ContentFrame = ContentFrame,
                    ViewModel = Provider.GetRequiredService<ClipboardListViewModel>(),
                    MainWindow = this
                };
            }

            if (listPageType == typeof(ListFrame.EmojiListFrame))
            {
                return new ListFrame.EmojiListFrameNavigationParameter
                {
                    ContentFrame = ContentFrame,
                    MainWindow = this
                };
            }

            if (listPageType == typeof(ListFrame.FavoriteListFrame) && Provider is not null)
            {
                return new ListFrame.FavoriteListFrameNavigationParameter
                {
                    ContentFrame = ContentFrame,
                    ViewModel = Provider.GetRequiredService<FavoriteListViewModel>(),
                    MainWindow = this
                };
            }

            return ContentFrame;
        }

        /// <summary>
        /// Tabキーだけで History/Favorite/Emoji を順に選べるようにする。
        ///
        /// NavigationViewの既定は「メニュー全体で1つのTabストップ、中の移動は矢印キー」
        /// というアクセシビリティの作法で、項目を載せているホスト要素が
        /// TabFocusNavigation=Onceを持っている。これはコントロールテンプレートの内部にあり、
        /// XAMLでNavigationViewにTabNavigationを指定しても上書きされないため、
        /// テンプレートが展開された後に実物を探して書き換える。
        /// </summary>
        private static void EnableTabThroughMenuItems(DependencyObject? navView)
        {
            if (navView is null)
                return;

            // WinUIのテンプレートでメニュー項目を載せている要素の名前
            if (FindChildByName(navView, "MenuItemsHost") is UIElement host)
            {
                host.TabFocusNavigation = KeyboardNavigationMode.Local;
                return;
            }

            System.Diagnostics.Debug.WriteLine(
                "[MainWindow] MenuItemsHostが見つかりませんでした。Tabでのタブ切り替えは既定の動作のままになります。");
        }

        private static T? FindChildOfType<T>(DependencyObject parent)
            where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var result = FindChildOfType<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        private static DependencyObject? FindChildByName(
            DependencyObject parent, string name)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is FrameworkElement fe && fe.Name == name)
                    return child;
                var result = FindChildByName(child, name);
                if (result != null) return result;
            }
            return null;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            HidePalette();
        }


        private void UpdateLayout()
        {
            var isCompact = RootContentGrid.ActualWidth > 0
                && RootContentGrid.ActualWidth < CompactWidthThreshold;

            var wasCompact = _isCompact;
            _isCompact = isCompact;

            // ContentSizerでListFrameを広げられる上限を「ウィンドウ幅 - 100」に追従させる
            // (縮小時はListFrameを全幅表示するため、このキャップをウィンドウ幅まで広げる。
            //  PositiveInfinityのような無制約値は、Auto列+*列にまたがるListFrameのサイズ解決を
            //  発散させてLayoutCycleExceptionを起こすことがあるため使わない)
            if (RootContentGrid.ActualWidth > 0)
            {
                ListFrame.MaxWidth = isCompact
                    ? RootContentGrid.ActualWidth
                    : Math.Max(ListFrame.MinWidth, RootContentGrid.ActualWidth - 100);
            }

            // emojiタブだけは縮小時、ListFrame(カテゴリ一覧)ではなく
            // ContentFrame(GroupStyleで種類別にまとめた絵文字全件)を表示する
            var isEmojiTab = _currentTag == "emoji";
            var showContentFrame = !isCompact || isEmojiTab;
            var showListFrame = !isCompact || !isEmojiTab;

            DividerBorder.Visibility = (!isCompact) ? Visibility.Visible : Visibility.Collapsed;
            ContentSizerControl.Visibility = (!isCompact) ? Visibility.Visible : Visibility.Collapsed;
            ListFrame.Visibility = showListFrame ? Visibility.Visible : Visibility.Collapsed;
            ContentFrame.Visibility = showContentFrame ? Visibility.Visible : Visibility.Collapsed;

            if (!isCompact)
            {
                // 通常レイアウト: ListFrame | 区切り | ContentFrame(右上にTitleBarRow)
                Grid.SetColumn(TitleBarRow, 2);
                Grid.SetColumnSpan(TitleBarRow, 1);

                Grid.SetRow(ListFrame, 0);
                Grid.SetRowSpan(ListFrame, 2);
                Grid.SetColumnSpan(ListFrame, 1);

                Grid.SetColumn(ContentFrame, 2);
                Grid.SetColumnSpan(ContentFrame, 1);

                // コンパクト表示中に退避しておいたドラッグ幅を復元する
                if (_savedListFrameWidth is double savedWidth)
                {
                    ListFrame.Width = savedWidth;
                    _savedListFrameWidth = null;
                }
            }
            else
            {
                // TitleBarRowを全幅にし、その下にListFrameまたはContentFrameを全幅で表示
                Grid.SetColumn(TitleBarRow, 0);
                Grid.SetColumnSpan(TitleBarRow, 3);

                Grid.SetRow(ListFrame, 1);
                Grid.SetRowSpan(ListFrame, 1);
                Grid.SetColumnSpan(ListFrame, 3);

                Grid.SetColumn(ContentFrame, 0);
                Grid.SetColumnSpan(ContentFrame, 3);

                // 通常表示からコンパクト表示に切り替わった瞬間のWidthだけを退避する。
                // ここで毎回退避すると、コンパクト表示中の後続リサイズのたびに
                // (このあと代入する)コンパクト時の幅で上書きされてしまい、
                // 通常表示に戻したときに元のSplitter位置が失われる。
                if (!wasCompact && !double.IsNaN(ListFrame.Width))
                {
                    _savedListFrameWidth = ListFrame.Width;
                }

                // コンパクト表示中は、ウィンドウ幅の具体的な値で固定する(NaNのような
                // 無制約値にすると、Auto列+*列にまたがるサイズ解決が発散して
                // LayoutCycleExceptionになる)
                ListFrame.Width = RootContentGrid.ActualWidth;
            }

            // Emojiページにコンパクト状態を伝え、GroupStyle(種類別の全件表示)を切り替える
            if (ContentFrame.Content is Emoji.Emoji emojiPage)
            {
                emojiPage.SetCompactMode(isCompact);
            }

            // レイアウトが変わったのでドラッグ領域も再計算する
            this.UpdateDragRegions(DragHandle);
        }

        // ContentSizerControl(区切り)のドラッグ処理。CommunityToolkit.WinUI.Controls.Sizersの
        // ContentSizerが非パッケージ環境で無反応になる問題を避けるための自前実装(MainWindow.xaml参照)
        private bool _isDraggingContentSizer;
        private double _contentSizerDragStartX;
        private double _contentSizerDragStartWidth;

        // カーソル切り替え用。WinUI3のUIElementにはCursorプロパティが無いため、
        // XamlRoot.ContentIslandから取れるInputPointerSourceに対して直接カーソルを設定する。
        // (素のWin32 SetCursorも試したが、直後にOSがWM_SETCURSORでデフォルトの矢印に
        // 戻してしまい、ウィンドウメッセージ側でTRUEを返す形でも解決しなかった。
        // こちらはWinUIの入力パイプラインに乗った正攻法で、設定した状態が保持される)
        private Microsoft.UI.Input.InputPointerSource? GetInputPointerSource() =>
            this.Content?.XamlRoot?.ContentIsland is { } island
                ? Microsoft.UI.Input.InputPointerSource.GetForIsland(island)
                : null;

        private void SetSizeWeCursor()
        {
            if (GetInputPointerSource() is { } source)
                source.Cursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast);
        }

        private void SetArrowCursor()
        {
            if (GetInputPointerSource() is { } source)
                source.Cursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);
        }

        private void ContentSizerControl_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            SetSizeWeCursor();
        }

        private void ContentSizerControl_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (!_isDraggingContentSizer)
                SetArrowCursor();
        }

        private void ContentSizerControl_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _isDraggingContentSizer = true;
            _contentSizerDragStartX = e.GetCurrentPoint(RootContentGrid).Position.X;
            _contentSizerDragStartWidth = ListFrame.ActualWidth;
            ContentSizerControl.CapturePointer(e.Pointer);
            SetSizeWeCursor();
            e.Handled = true;
        }

        private void ContentSizerControl_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            // ホバー中(未クリック)は、XAML側が毎回のポインター移動でカーソルを自動的に
            // 再評価して上書きしてしまうため、PointerEnteredで一度設定するだけでは足りず、
            // 移動のたびに再設定し続ける必要がある
            SetSizeWeCursor();

            if (!_isDraggingContentSizer)
                return;

            var currentX = e.GetCurrentPoint(RootContentGrid).Position.X;
            var newWidth = _contentSizerDragStartWidth + (currentX - _contentSizerDragStartX);
            newWidth = Math.Max(ListFrame.MinWidth, Math.Min(ListFrame.MaxWidth, newWidth));
            ListFrame.Width = newWidth;
            e.Handled = true;
        }

        private void ContentSizerControl_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!_isDraggingContentSizer)
                return;

            _isDraggingContentSizer = false;
            ContentSizerControl.ReleasePointerCapture(e.Pointer);
            SetArrowCursor();

            // ドラッグで変わった幅を保存する(以前はウィンドウ自体のリサイズ/移動時にしか
            // 保存されていなかったため、区切りだけドラッグして閉じると幅が失われる隙間があった)
            _windowSettingsSaveTimer.Stop();
            _windowSettingsSaveTimer.Start();

            e.Handled = true;
        }
    }
}
