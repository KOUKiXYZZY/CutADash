using Common.Models;
using Common.Utils;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using WinAPI;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI.Composition;
using Windows.UI.UIAutomation;
using WinRT;
using WinRT.Interop;
using WinUIEx;
using static WinAPI.DwmAPI;


namespace Common.Extension
{

    /// <summary>
    /// Window に対する拡張メソッドを提供するユーティリティクラス。
    /// </summary>
    /// <remarks>タイトルバー操作（非表示・削除）、Mica/Acrylic/Transparent の SystemBackdrop
    /// 適用、ウィンドウサイズの保存・復元などの拡張メソッドをまとめる。AppWindow API や WinUI 3（Windows App SDK）を利用しており、環境によっては一部機能が利用できない。UI
    /// スレッドでの呼び出しを前提とするメソッドが含まれる。</remarks>
    public static class WindowExtensionsHelpers
    {
        /// <summary>
        /// タイトルバーを完全に非表示にする拡張メソッド
        /// </summary>
        /// <remarks>AppWindow.TitleBar.ExtendsContentIntoTitleBar を true に設定し、TitleBarHeightOption.Collapsed
        /// を適用してタイトルバーを完全に隠す。WindowNative.GetWindowHandle で HWND を取得し、Microsoft.UI.Win32Interop.GetWindowIdFromWindow と
        /// AppWindow.GetFromWindowId を経由して AppWindow を取得する。</remarks>
        /// <param name="window">タイトルバーを非表示にする対象の Window インスタンス。</param>
        public static void HideTitleBar(this Window window)
        {
            // HWND と AppWindow を取得
            var hwnd = WindowNative.GetWindowHandle(window);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            // ウィンドウ領域にコンテンツを拡張
            window.ExtendsContentIntoTitleBar = true;

            // タイトルバーの背景を透明化（非表示扱い）
            var transparent = Windows.UI.Color.FromArgb(0, 0, 0, 0);
            appWindow.TitleBar.ButtonBackgroundColor = transparent;
            appWindow.TitleBar.ButtonInactiveBackgroundColor = transparent;

            // アイコンやシステムメニューも非表示
            appWindow.TitleBar.IconShowOptions = IconShowOptions.HideIconAndSystemMenu;
        }

        /// <summary>
        /// AppWindow API を使用してウィンドウのタイトルバーを非表示にする。
        /// </summary>
        /// <remarks>AppWindowTitleBar.IsCustomizationSupported() が true
        /// の場合、AppWindow.TitleBar.ExtendsContentIntoTitleBar を true に設定し、TitleBarHeightOption.Collapsed
        /// を適用してタイトルバーを完全に隠す。WindowNative.GetWindowHandle で HWND を取得し、Microsoft.UI.Win32Interop.GetWindowIdFromWindow と
        /// AppWindow.GetFromWindowId を経由して AppWindow を取得する。カスタマイズ非対応環境では何もしない。</remarks>
        /// <param name="window">タイトルバーを削除する対象の Window インスタンス。</param>
        public static void RemoveTitleBar(this Window window)
        {
            var hwnd = WindowNative.GetWindowHandle(window);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            // これで完全に消える
            if (AppWindowTitleBar.IsCustomizationSupported())
            {
                appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
                appWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;
            }
        }

        /// <summary>
        /// ウィンドウに DesktopAcrylicBackdrop を作成して SystemBackdrop として適用する。
        /// </summary>
        /// <remarks>DesktopAcrylicBackdrop を新規作成して割り当てるため、既存の SystemBackdrop は上書きされる。WinUI 3（Windows App
        /// SDK）が必要。</remarks>
        /// <param name="window">バックドロップを適用するウィンドウ。</param>
        public static void EnableAcrylicBackdrop(this Window window)
        {
            var _backdrop = new DesktopAcrylicBackdrop();
            window.SystemBackdrop = _backdrop;
        }

        // ウィンドウごとにコントローラを保持する。Windowにフィールドを足せないため、
        // ConditionalWeakTableでウィンドウの生存期間に紐づけて管理する
        private static readonly ConditionalWeakTable<Window, DesktopAcrylicController> _alwaysActiveAcrylicControllers = new();

        /// <summary>
        /// ウィンドウに、非アクティブになってもフォールバック(不透明な単色)へ切り替わらない
        /// Acrylicを適用する。
        /// </summary>
        /// <remarks>
        /// XAMLの&lt;Window.SystemBackdrop&gt;やEnableAcrylicBackdropが使う
        /// Microsoft.UI.Xaml.Media.DesktopAcrylicBackdropは、ウィンドウのActivated/Deactivatedに
        /// 連動してSystemBackdropConfiguration.IsInputActiveを自動更新しており、これを
        /// 外部から止める公開APIが無い。そのため、低レベルのDesktopAcrylicController+
        /// SystemBackdropConfigurationを自前で管理し、IsInputActiveを常にtrueへ固定する。
        /// WS_EX_NOACTIVATEウィンドウなど、ほぼ非アクティブのまま使うウィンドウ向け。
        ///
        /// window.SystemBackdropとは独立した仕組みのため、XAML側で&lt;Window.SystemBackdrop&gt;を
        /// 併用しない(二重に適用されてしまう)。
        /// </remarks>
        /// <param name="window">バックドロップを適用するウィンドウ。</param>
        public static void EnableAcrylicBackdropAlwaysActive(this Window window)
        {
            if (!DesktopAcrylicController.IsSupported())
            {
                // 環境が非対応の場合は、通常のDesktopAcrylicBackdropにフォールバックする
                window.SystemBackdrop = new DesktopAcrylicBackdrop();
                return;
            }

            var configuration = new SystemBackdropConfiguration
            {
                // 非アクティブでもフォールバックへ切り替わらないよう、常にtrueに固定する
                IsInputActive = true
            };

            void UpdateTheme()
            {
                if (window.Content is FrameworkElement root)
                {
                    configuration.Theme = root.ActualTheme switch
                    {
                        ElementTheme.Dark => SystemBackdropTheme.Dark,
                        ElementTheme.Light => SystemBackdropTheme.Light,
                        _ => SystemBackdropTheme.Default,
                    };
                }
            }

            UpdateTheme();
            if (window.Content is FrameworkElement rootElement)
            {
                rootElement.ActualThemeChanged += (_, _) => UpdateTheme();
            }

            var controller = new DesktopAcrylicController();
            controller.AddSystemBackdropTarget(window.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>());
            controller.SetSystemBackdropConfiguration(configuration);

            _alwaysActiveAcrylicControllers.AddOrUpdate(window, controller);

            // ウィンドウが閉じてもコントローラは自動破棄されないため、明示的に破棄する
            window.Closed += (_, _) =>
            {
                controller.Dispose();
                _alwaysActiveAcrylicControllers.Remove(window);
            };
        }

        /// <summary>
        /// 指定した Window に Mica の SystemBackdrop を設定します。
        /// </summary>
        /// <remarks>既存の SystemBackdrop を新しい MicaBackdrop インスタンスで置き換えます。Mica の効果は Windows 11
        /// と対応するフレームワーク（WinUI/Windows App SDK）が必要で、環境により表示が異なる場合があります。</remarks>
        /// <param name="window">Mica を適用する対象の Window。</param>
        public static void EnableMicaBackdrop(this Window window)
        {
            var _backdrop = new MicaBackdrop();
            window.SystemBackdrop = _backdrop;
        }

        // ウィンドウごとにコントローラを保持する(EnableAcrylicBackdropAlwaysActiveと同じ理由)
        private static readonly ConditionalWeakTable<Window, MicaController> _alwaysActiveMicaControllers = new();

        /// <summary>
        /// ウィンドウに、非アクティブになってもフォールバック(不透明な単色)へ切り替わらない
        /// Micaを適用する。EnableAcrylicBackdropAlwaysActiveのMica版。
        /// </summary>
        /// <param name="window">バックドロップを適用するウィンドウ。</param>
        public static void EnableMicaBackdropAlwaysActive(this Window window)
        {
            if (!MicaController.IsSupported())
            {
                window.SystemBackdrop = new MicaBackdrop();
                return;
            }

            var configuration = new SystemBackdropConfiguration
            {
                IsInputActive = true
            };

            void UpdateTheme()
            {
                if (window.Content is FrameworkElement root)
                {
                    configuration.Theme = root.ActualTheme switch
                    {
                        ElementTheme.Dark => SystemBackdropTheme.Dark,
                        ElementTheme.Light => SystemBackdropTheme.Light,
                        _ => SystemBackdropTheme.Default,
                    };
                }
            }

            UpdateTheme();
            if (window.Content is FrameworkElement rootElement)
            {
                rootElement.ActualThemeChanged += (_, _) => UpdateTheme();
            }

            var controller = new MicaController();
            controller.AddSystemBackdropTarget(window.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>());
            controller.SetSystemBackdropConfiguration(configuration);

            _alwaysActiveMicaControllers.AddOrUpdate(window, controller);

            window.Closed += (_, _) =>
            {
                controller.Dispose();
                _alwaysActiveMicaControllers.Remove(window);
            };
        }

        /// <summary>
        /// WinUIExの公式サンプル(CustomBackdrops)にある「BlurredBackdrop」と同じ実装。
        /// CompositionBrushBackdrop(WinUIEx)を継承し、Compositor.CreateHostBackdropBrush()で
        /// デスクトップの背後をぼかすブラシを使う。Mica/Acrylicと同じくwindow.SystemBackdrop
        /// 経由で適用されるため、DisableBackdropでの後始末もSystemBackdrop = nullだけで済む。
        /// </summary>
        public static void EnableBlurBackdrop(this Window window)
        {
            window.SystemBackdrop = new BlurBackdrop();
        }

        /// <summary>
        /// EnableAcrylicBackdrop/EnableMicaBackdrop/EnableBlurBackdropのいずれで
        /// 適用した効果も含め、ウィンドウの背景素材を無効化する
        /// (テーマ切り替え時に、前の効果を消してから次を適用するために使う)。
        /// </summary>
        public static void DisableBackdrop(this Window window)
        {
            window.SystemBackdrop = null;

            if (_alwaysActiveAcrylicControllers.TryGetValue(window, out var acrylicController))
            {
                acrylicController.Dispose();
                _alwaysActiveAcrylicControllers.Remove(window);
            }

            if (_alwaysActiveMicaControllers.TryGetValue(window, out var micaController))
            {
                micaController.Dispose();
                _alwaysActiveMicaControllers.Remove(window);
            }
        }

        /// <summary>
        /// 設定で選ばれたバックドロップの種類をウィンドウへ適用する
        /// (PreferenceWindowの「テーマ」タブから呼ばれる想定)。
        /// </summary>
        /// <param name="window">適用対象のウィンドウ。</param>
        /// <param name="kind">適用するバックドロップの種類。</param>
        /// <param name="alwaysActive">
        /// trueの場合、非アクティブでも効果が薄れない「常時有効」版を使う
        /// (WS_EX_NOACTIVATEなウィンドウ向け)。Blurは元々非アクティブでも切り替わらないため影響しない。
        /// </param>
        public static void ApplyBackdrop(this Window window, Common.Models.WindowBackdropKind kind, bool alwaysActive = false)
        {
            window.DisableBackdrop();

            switch (kind)
            {
                case Common.Models.WindowBackdropKind.Mica:
                    if (alwaysActive)
                        window.EnableMicaBackdropAlwaysActive();
                    else
                        window.EnableMicaBackdrop();
                    break;

                case Common.Models.WindowBackdropKind.Acrylic:
                    if (alwaysActive)
                        window.EnableAcrylicBackdropAlwaysActive();
                    else
                        window.EnableAcrylicBackdrop();
                    break;

                case Common.Models.WindowBackdropKind.Cat:
                    // Cat専用のバックドロップ素材は無く、Micaを流用する。見た目の違いは
                    // 呼び出し側(MainWindow.ApplyBackdropWithTintOverlay)が重ねる暖色ティント
                    // だけで作る
                    if (alwaysActive)
                        window.EnableMicaBackdropAlwaysActive();
                    else
                        window.EnableMicaBackdrop();
                    break;

                case Common.Models.WindowBackdropKind.Blur:
                    window.EnableBlurBackdrop();
                    break;
            }
        }

        /// <summary>
        /// 指定した Window に TransparentTintBackdrop を作成して SystemBackdrop として設定します。
        /// </summary>
        /// <remarks>内部で TransparentTintBackdrop のインスタンスを生成し、既存の SystemBackdrop を置き換えます。UI
        /// スレッドから呼び出してください。</remarks>
        /// <param name="window">透過バックドロップを適用する Window。</param>
        public static void EnableTransparentBackdrop(this Window window)
        {
            var _backdrop = new TransparentTintBackdrop();
            window.SystemBackdrop = _backdrop;
        }

        // 全ウィンドウのサイズ・位置を1つにまとめて保存するファイル。
        // ウィンドウごとにこの中のキーを分けて持つ
        private const string WindowSettingsFileName = "window_settings.json";

        /// <summary>
        /// ウィンドウの現在のサイズ・位置を JSON ファイルに保存する。
        /// </summary>
        /// <remarks>AppWindow.Size/Positionを<typeparamref name="TSize"/>としてシリアライズし、
        /// %LOCALAPPDATA%\CutADash\window_settings.json内の該当キーへ書き込む
        /// (他のウィンドウのキーは読み込んでそのまま残す)。</remarks>
        /// <typeparam name="TSize">保存する型。MainWindowはListFrameWidthを持つ
        /// <see cref="MainWindowSize"/>を、それ以外は基底の<see cref="WindowSize"/>を指定する。</typeparam>
        /// <param name="window">サイズを保存する対象の Window インスタンス。</param>
        /// <param name="key">保存先のキー。ウィンドウごとに別の値を指定する
        /// (省略時はMainWindow用の既定値"MainWindow")。</param>
        /// <param name="savePosition">trueの場合、ウィンドウ位置(AppWindow.Position)も保存する。
        /// MainWindowのようにキャレット位置へ毎回動かすウィンドウではfalseのままでよい。</param>
        /// <param name="configure">TSize固有の追加フィールド(MainWindowSize.ListFrameWidth等)を
        /// 設定するためのコールバック。不要な型ではnullでよい。</param>
        public static void SaveWindowSize<TSize>(this Window window, string key = "MainWindow", bool savePosition = false, Action<TSize>? configure = null)
            where TSize : WindowSize, new()
        {
            var size = window.AppWindow.Size;
            var data = new TSize
            {
                Width = size.Width,
                Height = size.Height,
            };

            if (savePosition)
            {
                var position = window.AppWindow.Position;
                data.X = position.X;
                data.Y = position.Y;
            }

            configure?.Invoke(data);

            var store = LoadWindowSizeStore();
            store[key] = JsonSerializer.SerializeToElement(data, GetWindowSizeTypeInfo<TSize>());

            string json = JsonSerializer.Serialize(store, WindowSizeContext.Default.DictionaryStringJsonElement);
            File.WriteAllText(AppPaths.GetDataFilePath(WindowSettingsFileName), json);
        }


        /// <summary>
        /// window_settings.jsonの該当キーに保存されたサイズ・位置を読み込み、指定したウィンドウへ復元します。
        /// </summary>
        /// <remarks>ファイルが存在しない、または該当キーが無い場合は処理を行いません。復元は AppWindow.Resize/Move
        /// を使用して行います。</remarks>
        /// <typeparam name="TSize">読み込む型。保存時に指定した<see cref="SaveWindowSize{TSize}"/>の
        /// 型引数と合わせる。</typeparam>
        /// <param name="window">サイズを復元する対象の Window インスタンス。</param>
        /// <param name="key">読み込むキー(省略時はMainWindow用の既定値"MainWindow")。</param>
        /// <returns>読み込んだ設定(ListFrameWidthなど呼び出し側で追加反映したい値を含む)。
        /// 保存が無い場合はnull。</returns>
        public static TSize? RestoreWindowSize<TSize>(this Window window, string key = "MainWindow")
            where TSize : WindowSize
        {
            var store = LoadWindowSizeStore();
            if (!store.TryGetValue(key, out var element))
                return null;

            var data = element.Deserialize(GetWindowSizeTypeInfo<TSize>());
            if (data is null)
                return null;

            window.AppWindow.Resize(new Windows.Graphics.SizeInt32(data.Width, data.Height));

            if (data.X is int x && data.Y is int y)
            {
                window.AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
            }

            return data;
        }

        /// <summary>
        /// window_settings.jsonを辞書(キー=ウィンドウ名、値=生のJSON)として読み込む。
        /// 値をDictionary&lt;string, WindowSize&gt;のような具体型の辞書にしないのは、
        /// キーによって実際の型(WindowSize/MainWindowSize)が異なるため。各キーの中身は
        /// SaveWindowSize/RestoreWindowSizeが呼び出し側の要求する型へその場で
        /// デシリアライズする。ファイルが無い/壊れている場合は空の辞書を返す。
        /// </summary>
        private static Dictionary<string, JsonElement> LoadWindowSizeStore()
        {
            var path = AppPaths.GetDataFilePath(WindowSettingsFileName);
            if (!File.Exists(path))
                return new Dictionary<string, JsonElement>();

            try
            {
                var json = File.ReadAllText(path);
                var store = JsonSerializer.Deserialize(json, WindowSizeContext.Default.DictionaryStringJsonElement);
                return store ?? new Dictionary<string, JsonElement>();
            }
            catch (JsonException)
            {
                return new Dictionary<string, JsonElement>();
            }
        }

        /// <summary>
        /// WindowSizeContext(source generator)から型引数に対応するJsonTypeInfoを選ぶ。
        /// WindowSize/MainWindowSizeの2種類しか無い閉じた集合なので、この単純な分岐で足りる。
        /// </summary>
        private static JsonTypeInfo<TSize> GetWindowSizeTypeInfo<TSize>() where TSize : WindowSize
        {
            if (typeof(TSize) == typeof(MainWindowSize))
                return (JsonTypeInfo<TSize>)(object)WindowSizeContext.Default.MainWindowSize;

            if (typeof(TSize) == typeof(WindowSize))
                return (JsonTypeInfo<TSize>)(object)WindowSizeContext.Default.WindowSize;

            throw new NotSupportedException($"WindowSizeContextに未登録の型です: {typeof(TSize)}");
        }

        /// <summary>
        /// Windowsの設定でアクセントカラーのウィンドウ枠を無効にする拡張メソッド
        /// </summary>
        /// <param name="window"></param>
        public static void DisableAccentBorder(this Window window)
        {
            const int DWMWA_BORDER_COLOR = 34;
            IntPtr hwnd = WindowNative.GetWindowHandle(window);

            // アクセントカラー無効
            int color = unchecked((int)0xFFFFFFFE);

            DwmAPI.DwmSetWindowAttribute(hwnd,
                DWMWA_BORDER_COLOR,
                ref color,
                sizeof(int));
        }


        /// <summary>
        /// DWMのウィンドウトランジションアニメーションを無効化する
        /// </summary>
        /// <param name="window"></param>
        public static void DwmTransitions(this Window window, bool disable)
        {
            int disableValue = disable ? 1 : 0;
            const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;

            var hwnd = WindowNative.GetWindowHandle(window);

            DwmAPI.DwmSetWindowAttribute(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref disableValue, sizeof(int));
        }


        /// <summary>
        /// ウィンドウの角の優先表示スタイルを DWM に設定する。
        /// </summary>
        /// <remarks>内部で DwmSetWindowAttribute を呼び出し、ウィンドウハンドルを用いて DWMWA_WINDOW_CORNER_PREFERENCE
        /// を設定する。</remarks>
        /// <param name="window">設定対象の System.Windows.Window インスタンス。</param>
        /// <param name="preference">適用する DwmAPI.DWM_WINDOW_CORNER_PREFERENCE の値。</param>
        public static void SetWindowCornerPreference(this Window window, DwmAPI.DWM_WINDOW_CORNER_PREFERENCE preference)
        {
            int prefValue = (int)preference;
            const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
            var hwnd = WindowNative.GetWindowHandle(window);
            DwmAPI.DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref prefValue, sizeof(int));
        }


        public static void UpdateDragRegions(this Window window, Grid dragHandle)
        {
            if (dragHandle.ActualWidth <= 0 || dragHandle.ActualHeight <= 0)
                return;

            var hWnd = WindowNative.GetWindowHandle(window);
            var dpi = WinUser.Dpi.GetDpiForWindow(hWnd);
            var scale = dpi / 96.0;

            // DragHandleのウィンドウ内座標(DIP)を取得し、物理ピクセルへ変換
            var transform = dragHandle.TransformToVisual((UIElement)window.Content);
            var origin = transform.TransformPoint(new Point(0, 0));

            var rect = new RectInt32(
                (int)(origin.X * scale),
                (int)(origin.Y * scale),
                (int)(dragHandle.ActualWidth * scale),
                (int)(dragHandle.ActualHeight * scale));

            var nonClientInputSrc = InputNonClientPointerSource.GetForWindowId(window.AppWindow.Id);
            nonClientInputSrc.SetRegionRects(NonClientRegionKind.Caption, new[] { rect });
        }


        public static void MakeWindowFixedSize(this Window window, Double width, Double height)
        {
            IntPtr hWnd = WindowNative.GetWindowHandle(window);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = false;   // リサイズ不可
                presenter.IsMaximizable = false; // 最大化も不可にしておくと安心
                presenter.IsMinimizable = true;  // 最小化は許可（お好みで）
            }

            appWindow.Resize(new Windows.Graphics.SizeInt32((int)width, (int)height));
        }


        /// <summary>
        /// ホットキー受信時のように、直前まで別アプリがフォアグラウンドを握っていた状態から
        /// このウィンドウを前面化する。Windowsのフォアグラウンドロックにより、通常の
        /// Activate()/SetForegroundWindowは(直前の入力がキー操作でない等の理由で)黙って
        /// 失敗することがある(ForegroundPaster.ActivateAndPasteAsyncと同じ制約)。
        /// 対象(元のフォアグラウンド)スレッドへ一時的に入力キューをアタッチしてから
        /// SetForegroundWindowを呼ぶことで、この制限を回避して確実に前面化する。
        /// </summary>
        public static void ForceForeground(this Window window)
        {
            var hWnd = WindowNative.GetWindowHandle(window);

            // 既に前面なら何もしない。余計なSetForegroundWindowはアクティブ状態を
            // 揺らすだけで得が無い(ForegroundPaster.ActivateAndPasteAsyncと同じ考え方)
            if (WinAPI.WinUser.GetForegroundWindow() == hWnd)
                return;

            // かつてはAttachThreadInputで相手スレッドと入力キューを結合していたが、
            // 結合すると「どのウィンドウがアクティブか」という状態まで共有されてしまい、
            // 直前にActivate()でアクティブにした自分のウィンドウが、相手側の状態に
            // 引き戻されて非アクティブ扱いになることがあった。
            // PowerPoint/Excelを前面にした状態でホットキーを押すと、SetForegroundWindowの
            // 呼び出しの中から同期的にDeactivatedが配送され、それを「他アプリへ切り替わった」と
            // 誤認して表示した瞬間にパレットを閉じてしまっていた。
            //
            // フォアグラウンドロックの制限時間を一時的に0にすれば、入力キューを結合しなくても
            // SetForegroundWindowが通る(Dittoの実装を参考。ForegroundPaster側も同じ手を使う)
            uint previousLockTimeout = 0;
            var lockTimeoutSaved = WinAPI.WinUser.SystemParametersInfo(
                WinAPI.WinUser.SPI_GETFOREGROUNDLOCKTIMEOUT, 0, ref previousLockTimeout, 0);
            WinAPI.WinUser.SystemParametersInfo(
                WinAPI.WinUser.SPI_SETFOREGROUNDLOCKTIMEOUT, 0, IntPtr.Zero, 0);

            try
            {
                WinAPI.WinUser.SetForegroundWindow(hWnd);
            }
            finally
            {
                // SPI_SETFOREGROUNDLOCKTIMEOUTは値そのものをpvParamとして渡す
                // (ポインタ渡しにすると、アドレス値が制限時間として設定されてしまう)
                if (lockTimeoutSaved)
                    WinAPI.WinUser.SystemParametersInfo(
                        WinAPI.WinUser.SPI_SETFOREGROUNDLOCKTIMEOUT, 0, (IntPtr)previousLockTimeout, 0);
            }
        }


        public static void SetTopMost(this Window window, bool topMost)
        {
            var hWnd = WindowNative.GetWindowHandle(window);

            WinAPI.WinUser.SetWindowPos(
                hWnd,
                topMost ? WinAPI.WinUser.HWND_TOPMOST : WinAPI.WinUser.HWND_NOTOPMOST,
                0, 0, 0, 0,
                WinAPI.WinUser.SWP_NOMOVE | WinAPI.WinUser.SWP_NOSIZE);
        }
    }

    /// <summary>
    /// WinUIExの公式サンプル(CustomBackdrops)にある「BlurredBackdrop」相当。
    /// CompositionBrushBackdropを継承し、CreateHostBackdropBrush()でデスクトップの
    /// 背後をぼかすブラシを使う("Blur"テーマの実体)。
    ///
    /// 生のホストバックドロップブラシだけだと、Acrylicと違って色味(ティント)が無いため、
    /// 背後の色によってはNavigationViewの文字が見えなくなることがある(例: 背後が白い時)。
    /// Composition側でブラシを合成する手(CompositeEffect等)はWin2D無しのこの環境では
    /// 使えなかったため、コントラスト確保はXAML側(MainWindowのルート要素の半透明な
    /// Background)で行う
    /// </summary>
    public class BlurBackdrop : CompositionBrushBackdrop
    {
        protected override Windows.UI.Composition.CompositionBrush CreateBrush(Windows.UI.Composition.Compositor compositor)
            => compositor.CreateHostBackdropBrush();
    }

    /// <summary>
    /// カスタムBackdropクラス
    /// CompositionBrushBackdropを継承して、背景ブラシを自前で生成する
    /// 色が時間で変化するアニメーションを持つカラーブラシを生成している
    /// </summary>
    /// <example>
    /// public MainWindow()
    /// {
    ///   this.InitializeComponent();
    ///   // カスタムBackdropを適用
    ///   this.SystemBackdrop = new ColorAnimatedBackdrop();
    /// }
    /// </example>
    public class ColorAnimatedBackdrop : CompositionBrushBackdrop
    {
        /// <summary>
        /// バックドロップに使用するブラシを生成するメソッド
        /// </summary>
        /// <param name="compositor"></param>
        /// <returns></returns>
        protected override Windows.UI.Composition.CompositionBrush CreateBrush(Windows.UI.Composition.Compositor compositor)
        {
            // 初期色が赤のカラーブラシを作成
            var brush = compositor.CreateColorBrush(Windows.UI.Color.FromArgb(255, 255, 0, 0));
            // 色を時間で変化させるアニメーションを作成
            var animation = CreateColorKeyFrameAnimation(compositor);
            // ブラシのColorプロパティに対してアニメーションを適用
            brush.StartAnimation("Color", animation);
            return brush;
        }


        /// <summary>
        /// 赤→緑→青→赤 をループする ColorKeyFrameAnimation を作成する。
        /// </summary>
        /// <remarks>線形イージングを使用。キーフレームは 0: Red、0.333: Green、0.667: Blue、1: Red に設定。色補間は HSL、期間は 15
        /// 秒、IterationBehavior は Forever。</remarks>
        /// <param name="compositor">アニメーションを作成するための Compositor。</param>
        /// <returns>構成済みの Windows.UI.Composition.ColorKeyFrameAnimation。</returns>
        private Windows.UI.Composition.ColorKeyFrameAnimation CreateColorKeyFrameAnimation(Windows.UI.Composition.Compositor compositor)
        {
            var animation = compositor.CreateColorKeyFrameAnimation();
            // 線形補間（一定速度で色が変化）
            var easing = compositor.CreateLinearEasingFunction();
            // キーフレームを設定
            // 0%地点：赤
            animation.InsertKeyFrame(0, Colors.Red, easing);
            // 約33%地点：緑
            animation.InsertKeyFrame(.333f, Colors.Green, easing);
            // 約66%地点：青
            animation.InsertKeyFrame(.667f, Colors.Blue, easing);
            // 100%地点：赤（ループ用に最初と同じ色）
            animation.InsertKeyFrame(1, Colors.Red, easing);
            // 色補間をHSL空間で行う
            // RGB補間よりも自然な色変化になる
            animation.InterpolationColorSpace = Windows.UI.Composition.CompositionColorSpace.Hsl;
            // アニメーションの周期（15秒で1周）
            animation.Duration = TimeSpan.FromSeconds(15);
            // 無限ループ
            animation.IterationBehavior = Windows.UI.Composition.AnimationIterationBehavior.Forever;

            return animation;
        }
    }

}