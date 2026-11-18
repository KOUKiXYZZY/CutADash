using Common.Utils;
using CutADash.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using WinUIEx;

namespace CutADash.Views.TaskTray
{
    /// <summary>
    /// タスクトレイアイコンと右クリックメニュー。状態(監視の一時停止/再開、常にプレーン
    /// テキストでペースト)と、画面を開く操作(メインウィンドウ/設定/シーケンシャルペースト/
    /// 終了)のコマンドはすべてTaskTrayViewModelが持ち、ここはTrayIconの生成と、Windows標準の
    /// メニュー(NativeTrayMenu)で選ばれた項目→ViewModelのコマンド実行の橋渡しだけを行う。Windowは一切保持しない
    /// (画面を開く実際の処理はWindowServiceがMessenger経由で引き受ける)。
    /// </summary>
    internal sealed class TaskTray
    {
        // システムの明暗テーマ(タスクバー/通知領域の背景色)を判定する。Windowを介さずに
        // 取得できるため、トレイアイコン(ウィンドウを持たない)のテーマ追従に使う
        private readonly Windows.UI.ViewManagement.UISettings _uiSettings = new();
        private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        private readonly TaskTrayViewModel _viewModel;

        private TrayIcon? _icon;

        public TaskTray(ServiceProvider provider)
        {
            _viewModel = new TaskTrayViewModel(provider);

            // アイコンを作り直しても購読が重複しないよう、アイコン自体ではなく
            // システムの変更通知はここで一度だけ登録する

            // ColorValuesChangedはバックグラウンドスレッドから飛んでくるため、
            // アイコン差し替え(内部でHICONを作り直す)はUIスレッドへ戻してから行う
            _uiSettings.ColorValuesChanged += (s, e) =>
                _dispatcherQueue.TryEnqueue(UpdateTrayIconForTheme);

            // 表示スケール(DPI)の変更(モニタ間の移動・設定画面での変更)に追従する。
            // WinUIExのTrayIcon.SetIcon(string)は呼び出し時点のDPIでHICONのサイズを
            // 決めるため、初回作成時のDPIのまま放置すると、後からスケールが変わっても
            // 古いサイズのアイコンのままになり、特に100%のような小さいサイズ側で
            // 潰れて見えることがあった(実際に踏んだ不具合)。SystemEvents.
            // DisplaySettingsChangedは解像度変更等でも飛んでくるが、再生成のコストは
            // 軽いため区別せずまとめて呼び直す。バックグラウンドスレッドから飛んでくるため
            // ColorValuesChangedと同じくUIスレッドへ戻す
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (s, e) =>
                _dispatcherQueue.TryEnqueue(UpdateTrayIconForTheme);

            CreateTrayIcon();
        }

        /// <summary>
        /// Explorerの再起動(クラッシュ・再起動、タスクバーの作り直し)に対応して、
        /// タスクトレイのアイコンを作り直す。Explorerが再起動すると、通知領域に登録済みの
        /// アイコンは全て消える。OSはその時にTaskbarCreatedメッセージを全トップレベル
        /// ウィンドウへ送るので、受け取った側で登録し直す必要がある
        /// (WinUIExのTrayIconはこのメッセージを処理しない)。
        /// 呼び出し元はウィンドウプロシージャ内のため、UIスレッドのキューへ積んで後で行う。
        /// </summary>
        public void OnTaskbarCreated()
        {
            _dispatcherQueue.TryEnqueue(RecreateTrayIcon);
        }

        private void RecreateTrayIcon()
        {
            // 古いアイコンはExplorerごと消えているため、削除の失敗は無視してよい
            try
            {
                Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TaskTray] 旧アイコンの破棄に失敗: {ex}");
            }

            CreateTrayIcon();
        }

        public void Dispose()
        {
            if (_icon is not null)
            {
                _icon.IsVisible = false;
                _icon.Dispose();
                _icon = null;
            }
        }

        private void CreateTrayIcon()
        {
            // WinUIEx 2.9.2以降は、相対パスを渡すとPackage.Currentでパッケージの場所を調べるが、
            // パッケージ化していないこのアプリではそれが例外になり、トレイのアイコンが作られなかった。
            // 絶対パスならその処理を通らないため、絶対パスで渡す
            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            _icon = new TrayIcon(1, iconPath, AppStrings.Get("TrayIcon_Tooltip")) { IsVisible = true };
            UpdateTrayIconForTheme();

            _icon.Selected += (s, e) => _viewModel.OpenCommand.Execute(null);

            // メニューはWinUIExのMenuFlyoutではなく、Windows標準のメニューで出す
            // (Flyoutを設定しなければ、WinUIExはメニューを出さない)
            _icon.ContextMenu += (w, e) => ShowNativeContextMenu();
        }

        // 右クリックメニュー。選ばれた項目は、メニューが完全に閉じてから実行する
        private void ShowNativeContextMenu()
        {
            var items = new List<NativeTrayMenu.Item>
            {
                new(1, AppStrings.Get(_viewModel.IsMonitoring ? "TrayIcon_PauseMonitoring" : "TrayIcon_ResumeMonitoring")),
                new(2, AppStrings.Get("TrayIcon_AlwaysPastePlainText"), _viewModel.AlwaysPastePlainText),
                NativeTrayMenu.Item.Separator,
                new(3, AppStrings.Get("TrayIcon_Open")),
                new(4, AppStrings.Get("TrayIcon_SequentialPaste")),
                new(5, AppStrings.Get("TrayIcon_Settings")),
                NativeTrayMenu.Item.Separator,
                new(6, AppStrings.Get("TrayIcon_CheckForUpdates")),
                NativeTrayMenu.Item.Separator,
#if DEBUG || DEVDEBUG
                // デバッグビルドだけ、UIライブラリ(UiLibrary)の部品を一覧で試せる画面を開く項目を出す
                new(8, "Debug: UI Gallery"),
                NativeTrayMenu.Item.Separator,
#endif
                new(7, AppStrings.Get("TrayIcon_Quit")),
            };

            var selected = NativeTrayMenu.Show(items);
            if (selected == 0)
                return;

            _dispatcherQueue.TryEnqueue(() => ExecuteMenuCommand(selected));
        }

        private void ExecuteMenuCommand(int id)
        {
            switch (id)
            {
                case 1: _viewModel.ToggleMonitoringCommand.Execute(null); break;
                case 2: _viewModel.ToggleAlwaysPastePlainTextCommand.Execute(null); break;
                case 3: _viewModel.OpenCommand.Execute(null); break;
                case 4: _viewModel.OpenSequentialPasteCommand.Execute(null); break;
                case 5: _viewModel.OpenSettingsCommand.Execute(null); break;
                case 6: _viewModel.CheckForUpdatesCommand.Execute(null); break;
                case 7: _viewModel.QuitCommand.Execute(null); break;
#if DEBUG || DEVDEBUG
                case 8: new UiLibrary.GalleryWindow().Activate(); break;
#endif
            }
        }

        private bool IsSystemThemeDark()
        {
            var bg = _uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background);
            return bg.R < 128;
        }

        private void UpdateTrayIconForTheme()
        {
            var fileName = IsSystemThemeDark() ? "AppIconTrayDark.ico" : "AppIconTrayLight.ico";
            _icon?.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", fileName));
        }
    }
}
