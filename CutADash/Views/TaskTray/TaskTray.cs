using Common.Utils;
using CutADash.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using System;
using WinUIEx;

namespace CutADash.Views.TaskTray
{
    /// <summary>
    /// タスクトレイアイコンと右クリックメニュー。状態(監視の一時停止/再開、常にプレーン
    /// テキストでペースト)と、画面を開く操作(メインウィンドウ/設定/シーケンシャルペースト/
    /// 終了)のコマンドはすべてTaskTrayViewModelが持ち、ここはTrayIcon/MenuFlyoutの生成と、
    /// Click→ViewModelのコマンド実行の橋渡しだけを行う。Windowは一切保持しない
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
            _icon = new TrayIcon(1, "Assets/AppIcon.ico", AppStrings.Get("TrayIcon_Tooltip")) { IsVisible = true };
            UpdateTrayIconForTheme();

            // ColorValuesChangedはバックグラウンドスレッドから飛んでくるため、
            // アイコン差し替え(内部でHICONを作り直す)はUIスレッドへ戻してから行う
            _uiSettings.ColorValuesChanged += (s, e) =>
                _dispatcherQueue.TryEnqueue(UpdateTrayIconForTheme);

            _icon.Selected += (s, e) => _viewModel.OpenCommand.Execute(null);
            _icon.ContextMenu += (w, e) => e.Flyout = BuildContextMenu();
        }

        // メニューを開くたびに作り直すことで、チェック状態やラベルを常に最新にする
        private MenuFlyout BuildContextMenu()
        {
            var flyout = new MenuFlyout();

            var openItem = new MenuFlyoutItem { Text = AppStrings.Get("TrayIcon_Open") };
            var sequentialPasteItem = new MenuFlyoutItem { Text = AppStrings.Get("TrayIcon_SequentialPaste") };
            var settingsItem = new MenuFlyoutItem { Text = AppStrings.Get("TrayIcon_Settings") };
            var monitorToggleItem = new MenuFlyoutItem
            {
                Text = AppStrings.Get(_viewModel.IsMonitoring ? "TrayIcon_PauseMonitoring" : "TrayIcon_ResumeMonitoring")
            };
            var alwaysPlainTextItem = new ToggleMenuFlyoutItem
            {
                Text = AppStrings.Get("TrayIcon_AlwaysPastePlainText"),
                IsChecked = _viewModel.AlwaysPastePlainText
            };
            var checkForUpdatesItem = new MenuFlyoutItem
            {
                Text = AppStrings.Get(_viewModel.IsUpdateAvailable ? "TrayIcon_ApplyUpdate" : "TrayIcon_CheckForUpdates")
            };
            var quitItem = new MenuFlyoutItem { Text = AppStrings.Get("TrayIcon_Quit") };

            flyout.Items.Add(monitorToggleItem);
            flyout.Items.Add(alwaysPlainTextItem);
            flyout.Items.Add(new MenuFlyoutSeparator());
            flyout.Items.Add(openItem);
            flyout.Items.Add(sequentialPasteItem);
            flyout.Items.Add(settingsItem);
            flyout.Items.Add(new MenuFlyoutSeparator());
            flyout.Items.Add(checkForUpdatesItem);
            flyout.Items.Add(new MenuFlyoutSeparator());
            flyout.Items.Add(quitItem);

            openItem.Click += (s, e) => _viewModel.OpenCommand.Execute(null);
            sequentialPasteItem.Click += (s, e) => _viewModel.OpenSequentialPasteCommand.Execute(null);
            settingsItem.Click += (s, e) => _viewModel.OpenSettingsCommand.Execute(null);
            monitorToggleItem.Click += (s, e) => _viewModel.ToggleMonitoringCommand.Execute(null);
            alwaysPlainTextItem.Click += (s, e) => _viewModel.ToggleAlwaysPastePlainTextCommand.Execute(null);
            checkForUpdatesItem.Click += (s, e) => _viewModel.CheckForUpdatesCommand.Execute(null);
            quitItem.Click += (s, e) => _viewModel.QuitCommand.Execute(null);

            return flyout;
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
