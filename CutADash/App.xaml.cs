using Common.Extension;
using Common.Infra.Win32;
using Common.Utils;
using CutADash.Infra.Win32;
using CutADash.Repositories;
using CutADash.Services;
using CutADash.Utils;
using CutADash.ViewModels;
using CutADash.Views;
using Preferences;
using Preferences.Views;
using SequentialPaste;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;


// using Windows.ApplicationModel;
// using Windows.ApplicationModel.Activation;
using WinRT.Interop;
using WinUIEx;
using static WinAPI.WinUser;
using System;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CutADash
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private SingleApp? singleApp;

        private HotKeyMonitor? hotKeyMonitor;

        // 試験的機能。既定は無効(オプトイン)で、PreferenceWindowのチェックボックスから
        // ON/OFFできる。プロセス生存中、有効な間だけマウスフックとUI Automationで
        // テキスト選択を監視する
        private readonly SelectionToolbar.SelectionToolbarService _selectionToolbarService = new();

        public ServiceProvider? provider {  get; private set; }

        /// <summary>
        /// DBの作成/暗号化移行が必要な場合だけ実行する。Common.Db.DbMigrationCheckerによる
        /// 軽いチェックで「明らかに不要」なら何もせず即座に戻る。
        ///
        /// DBを開く前に必ず終わらせる必要があるため、呼び出し側はこれを待ってから
        /// リポジトリ類を組み立てること。件数の多いDBでは時間がかかるので、
        /// UIスレッドを塞がないようワーカースレッドで動かす。
        /// </summary>
        private static async Task RunMigrationIfNeededAsync()
        {
            bool needsMigration;
            try
            {
                needsMigration = Common.Db.DbMigrationChecker.NeedsMigration();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Migration] 事前チェックに失敗しました。念のため実行します: {ex}");
                needsMigration = true;
            }

            if (!needsMigration)
                return;

            // Migrator.Runは中で例外を捕まえてmigration.logへ記録し、戻り値で成否を返す
            // (移行できなくてもアプリ自体は起動させる方針)
            var exitCode = await Task.Run(CutADash.Migration.Migrator.Run);

            if (exitCode != 0)
                Debug.WriteLine($"[Migration] 失敗しました(詳細はmigration.log)。");
        }

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            // Velopack(GitHub Releases経由の自動更新)は、インストール/アンインストール/
            // 更新後の再起動時に特殊な起動引数を渡してプロセスを起動する。それをこの最初の
            // タイミングで処理させる必要があるため、他のどのコードよりも前に呼ぶ
            // (これより後だと、更新の適用やショートカット作成が正しく動かない)
            Velopack.VelopackApp.Build().Run();

            // 保存済みの表示言語を、UIリソースが読み込まれる前(InitializeComponentより前)に適用する。
            // 空文字列(システム既定)ならPrimaryLanguageOverrideには触れない(既定の動作のまま)。
            // このAPIは非パッケージアプリの環境によっては例外を投げることが実機で確認された
            // (WinRT.Runtime.dllからInvalidOperationExceptionが飛び、例外ハンドラを
            // 登録する前(この直後のInitializeComponentより前)のため起動ごと落ちてしまっていた)。
            // 起動不能を防ぐため、失敗してもシステム既定の言語のまま続行する
            var language = PreferencesGateway.GetLanguage();
            if (!string.IsNullOrEmpty(language))
            {
                try
                {
                    Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = language;
                }
                catch
                {
                }
            }

            // PrimaryLanguageOverrideは非パッケージアプリでは反映されない(上のtryが例外を握りつぶす)ため、
            // AppStrings/EmojiKindLocalizerが実際に参照するResourceContextの"Language"修飾子を
            // 直接書き換えて表示言語を切り替える(これが実際に効く方)
            AppStrings.SetLanguage(language);

            InitializeComponent();
            // AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;

            // クラッシュの原因調査用。未処理例外をcrash_log.txtへ書き出す
            this.UnhandledException += (s, e) =>
                LogCrash("Application.UnhandledException", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                LogCrash("AppDomain.UnhandledException", e.ExceptionObject as Exception);
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
                LogCrash("TaskScheduler.UnobservedTaskException", e.Exception);
        }

        private static void LogCrash(string source, Exception? ex)
        {
            var text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\n{ex}\n\n";
            Debug.WriteLine(text);

            try
            {
                System.IO.File.AppendAllText(AppPaths.GetDataFilePath("crash_log.txt"), text);
            }
            catch
            {
                // ログ書き込み自体の失敗は無視する
            }
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            base.OnLaunched(args);

            singleApp = new SingleApp("CutADash");
            if (!singleApp.IsCurrent)
            {
                // 既に起動している別プロセスへ「前面に出して」を通知し、自分は何も
                // 作らずすぐ終了する(DBオープンやトレイアイコン作成などを二重にしない)
                // NotifyExistingInstance();
                Environment.Exit(0);
                return;
            }

            // DBが未作成/未暗号化/スキーマが古い場合だけ、Migration.exeを起動して完了を待つ。
            // 通常起動時はDbMigrationChecker側の軽いチェックだけで済み、プロセス起動のコストは払わない
            await RunMigrationIfNeededAsync();

            // 絵文字マスタはSQLiteをやめ、Assets/Emoji配下のkind別JSON+PNGを直接読み込む。
            // 画像はMicrosoft Fluent Emoji(MIT、AssetsSrc配下にサブモジュールとして追加済み)
            // から AssetsSrc/EmojiGenerateScript/build_emoji_data.py でビルド前に生成し、
            // そのまま同梱する(以前試したフォントからの実行時レンダリング方式は不安定だったため取りやめた)
            var emojiRepository = new EmojiRepository(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Emoji"));

            // Windowの生成・表示・後始末を一手に引き受けるサービス。「Windowそのものを
            // 各所で引き回す」のを避けるため、App自身はこれをローカル変数として
            // クロージャに捕捉させるだけでフィールドとしては保持しない
            // (TaskTray等もWindowを直接持たず、Messenger経由でこれを呼ぶ)
            var windowService = new WindowService();
            var window = windowService.GetMainWindow();
            var hWnd = WindowNative.GetWindowHandle(window);

            var windowMessageDispatcher = new WindowMessageDispatcher(hWnd);

            // 別プロセスが多重起動を検知して送ってくる「前面に出して」通知を受け取る
            windowMessageDispatcher.AddHandler((int)ActivateMessageId, (wParam, lParam) =>
            {
                windowService.ActivateMainWindow();
                return true;
            });

            // 他アプリへ切り替わったらパレットを閉じる。
            //
            // WinUIのWindow.Activated(=WM_ACTIVATE)は、自分でActivate()/SetForegroundWindow()を
            // 呼んでいる最中にも「いったん非アクティブ→再アクティブ」という過渡的な
            // Deactivatedを発火させる。PowerPoint/Excelを前面にした状態でホットキーを押すと、
            // 表示処理の途中でそれを拾って即座に閉じてしまい、「表示した瞬間に消える」状態に
            // なっていた(ShowActivate→ForceForeground→SetForegroundWindowのコールスタックの
            // 中からDeactivatedが同期的に配送されていた)。
            //
            // WM_ACTIVATEAPPはアプリ(スレッド)境界をまたぐ切り替わりでしか発火しないため、
            // この過渡的な揺れを原理的に拾わない。Dittoのようなネイティブ実装と同じ粒度にする
            windowMessageDispatcher.AddHandler(WinAPI.WindowMessages.WM_ACTIVATEAPP, (wParam, lParam) =>
            {
#if !DEVDEBUG
                // wParamが0なら、他アプリのウィンドウがアクティブになった(こちらは非アクティブ)
                if (wParam == IntPtr.Zero && !window.IsPasting)
                {
                    // ペースト中は閉じない。ペーストは貼り付け先アプリへ意図的に
                    // フォアグラウンドを渡すため、ここが必ず発火する。その最中に
                    // Hide()するとフォアグラウンドの受け渡しをかき乱し、Ctrl+Vが
                    // 貼り付け先へ届かなくなる(MainWindow.IsPastingのコメント参照)。
                    // ペースト後の後始末はForegroundPasteHelperが自分でHidePaletteを呼ぶ
                    window.HidePalette();
                }
#endif
                // 他のハンドラや既定の処理も動かす必要があるため、握りつぶさない
                return false;
            });

            hotKeyMonitor = new HotKeyMonitor(windowMessageDispatcher);

            // キーはMainWindowのNavigationViewItem.Tagと同じ文字列("history"/"favorite"/"emoji")に揃える。
            // 保存済みの設定が無い場合はデフォルトを割り当てず、未設定のままにする。
            // HotKeyService(Common)は永続化の実装を知らないため、起動時の読み込みだけを
            // PreferencesGateway経由でここから注入する。変更後の保存/削除はHotKeyServiceの
            // 責務ではなく、PreferenceViewModel(Preferencesプロジェクト内)がHotKeySettingsStoreへ
            // 直接行う(HotKeyServiceのコメント参照)
            var services = new ServiceCollection();
            services.AddKeyedSingleton<HotKeyService>("history",
                new HotKeyService("history", hotKeyMonitor,
                                   null,
                                   () => windowService.OpenTab("history"),
                                   PreferencesGateway.LoadHotKeyOrDefault));

            services.AddKeyedSingleton<HotKeyService>("favorite",
                new HotKeyService("favorite", hotKeyMonitor,
                                   null,
                                   () => windowService.OpenTab("favorite"),
                                   PreferencesGateway.LoadHotKeyOrDefault));

            services.AddKeyedSingleton<HotKeyService>("emoji",
                new HotKeyService("emoji", hotKeyMonitor,
                                   null,
                                   () => windowService.OpenTab("emoji"),
                                   PreferencesGateway.LoadHotKeyOrDefault));

            // ListFrame(履歴一覧)とClipboardMonitorのコールバックが同じ履歴を共有できるよう、
            // シングルトンとして登録する。ClipboardListViewModelはコンストラクタで
            // ClipboardRepositoryを受け取るため、これも合わせて登録しておく。
            var clipboardRepository = new ClipboardRepository(
                AppPaths.GetDataFilePath("clipboard_history.db"),
                AppPaths.GetDataFilePath("ClipboardImages"),
                (uint)PreferencesGateway.GetThumbnailMaxDimension());

            // 設定画面で変更されたら、次にコピーした画像から即座に新しいサイズが使われるようにする。
            // Repositoriesプロジェクトはpreferences非依存に保ちたいため、購読はここで行う
            PreferencesGateway.ThumbnailMaxDimensionChanged += value =>
                clipboardRepository.ThumbnailMaxDimension = (uint)value;

            services.AddSingleton(clipboardRepository);
            services.AddSingleton<ClipboardListViewModel>();

            // お気に入りは履歴とは別のDBファイル・画像ディレクトリで管理する
            // (履歴側の上限削除やクリアの影響を受けないようにするため)
            services.AddSingleton(new FavoriteRepository(
                AppPaths.GetDataFilePath("favorites.db"),
                AppPaths.GetDataFilePath("FavoriteImages")));
            services.AddSingleton<FavoriteListViewModel>();

            services.AddSingleton(emojiRepository);

            // 履歴からOSクリップボードへ書き戻す際、それを新規コピーとして再取り込みしないよう
            // SuppressNextChangeを呼べるようにするため、ClipboardMonitorもDIで共有する。
            var clipBoardMonitor = new ClipboardMonitor(hWnd, windowMessageDispatcher);
            services.AddSingleton(clipBoardMonitor);

            // シーケンシャルペースト(積んだ項目を1件ずつ順番にペーストする、独立ウィンドウの
            // 別プロジェクト)。実際のOSクリップボード書き込み/Ctrl+V送信は本体プロジェクト
            // 内部(internal)のクラスに依存するため、そちらから直接は呼べない
            // SequentialPasteQueueViewModel.PasteToForegroundへここで注入する
            // (HotKeyServiceと同じ、下位層がコールバックを受け取るだけの設計パターン)
            var sequentialPasteQueue = new SequentialPasteQueueViewModel();
            sequentialPasteQueue.PasteToForeground = async item =>
            {
                clipBoardMonitor.BeginOwnWrite();
                try
                {
                    await ClipboardContentWriter.SetClipboardContentAsync(item, Preferences.PreferencesGateway.IsAlwaysPastePlainText());
                }
                finally
                {
                    clipBoardMonitor.EndOwnWrite();
                }

                var targetHwnd = GetForegroundWindow();
                if (targetHwnd != IntPtr.Zero)
                    await ForegroundPaster.ActivateAndPasteAsync(targetHwnd);
            };
            services.AddSingleton(sequentialPasteQueue);
            services.AddSingleton(new SequentialPasteService(sequentialPasteQueue));

            // GitHub Releases経由の自動更新(Velopack)。詳細はUpdateServiceのコメント参照
            services.AddSingleton<UpdateService>();

            provider = services.BuildServiceProvider();
            windowService.Provider = provider;
            window.Provider = provider;

            // ショートカットの変更/削除は、PreferenceViewModelがStoreへ保存するだけで
            // 完結させ(PreferencesGateway.HotKeyChanged参照)、実際のWin32登録の更新は
            // ここでHotKeyServiceを操作して行う(ThumbnailMaxDimensionChanged等と同じパターン)
            PreferencesGateway.HotKeyChanged += (key, definition) =>
            {
                var hotKeyService = provider.GetRequiredKeyedService<HotKeyService>(key);
                if (definition is null)
                    hotKeyService.Remove();
                else
                    hotKeyService.Update(definition);
            };

            clipBoardMonitor.Register((clipboardItem) => {
#if DEVDEBUG
                Debug.WriteLine(clipboardItem.Type.ToString());
#endif
                provider.GetRequiredService<ClipboardListViewModel>().Enqueue(clipboardItem);
            });

            _ = new Views.TaskTray.TaskTray(provider);
            InitializeSelectionToolbar();

            // App(このクラス)が直接生成したリソースの後始末。Environment.Exit(0)は
            // OnExit等の通常の終了処理を素通りするが、ProcessExitだけは呼ばれるため、
            // 終了ボタン側(TaskTray)で手動で呼ぶのではなくここへ登録しておく。
            // Window自体の後始末はWindowServiceが自分でProcessExitに登録済みのため、ここでは扱わない
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                provider?.Dispose();
                provider = null;

                hotKeyMonitor?.Dispose();
                hotKeyMonitor = null;

                singleApp?.Dispose();
                singleApp = null;

                // SelectionToolbarはグローバルなフックを張ったままになるため、
                // 明示的に止めないとアプリ終了後もプロセスが残ってしまう
                _selectionToolbarService.Stop();
            };

#if DEVDEBUG
            window.Activate();
#endif
        }

        // 試験的機能。設定に従って起動時の有効/無効を決め、以降は設定画面での
        // 切り替えを即座に反映する(アプリの再起動は不要)
        private void InitializeSelectionToolbar()
        {
            if (Preferences.PreferencesGateway.IsSelectionToolbarEnabled())
                _selectionToolbarService.Start();

            PreferencesGateway.SelectionToolbarEnabledChanged += enabled =>
            {
                if (enabled)
                    _selectionToolbarService.Start();
                else
                    _selectionToolbarService.Stop();
            };
        }

        // 全プロセスで同じ番号になる(RegisterWindowMessageがOSに問い合わせて発行する)
        // カスタムメッセージ。多重起動を検知した側のプロセスがこれをブロードキャストし、
        // 起動済みの側のプロセスがWindowMessageDispatcher経由で受け取って前面に出す
        private static readonly uint ActivateMessageId = RegisterWindowMessage("CutADash_ActivateRequest");

        private static void NotifyExistingInstance()
        {
            PostMessage(HWND_BROADCAST, ActivateMessageId, IntPtr.Zero, IntPtr.Zero);
        }
    }
}
