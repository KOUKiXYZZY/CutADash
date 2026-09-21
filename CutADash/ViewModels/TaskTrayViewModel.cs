using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CutADash.Infra.Win32;
using CutADash.Messages;
using CutADash.Services;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace CutADash.ViewModels
{
    /// <summary>
    /// タスクトレイの右クリックメニューが持つ状態とコマンド。Windowを直接知らず、
    /// 画面を開いてほしい操作(メインウィンドウ/設定/シーケンシャルペースト)は
    /// WeakReferenceMessenger経由でメッセージを送るだけにする。実際にWindowを
    /// 生成/表示するのはWindowServiceの役目(疎結合にするため)。
    /// </summary>
    public partial class TaskTrayViewModel : ObservableObject
    {
        private readonly ServiceProvider _provider;

        [ObservableProperty]
        private bool isMonitoring;

        [ObservableProperty]
        private bool alwaysPastePlainText;

        // 起動時に静かに確認した結果。trueなら「アップデートを確認」の代わりに
        // 「アップデートを適用して再起動」のラベルを出す(TaskTray側で切り替える)
        [ObservableProperty]
        private bool isUpdateAvailable;

        public TaskTrayViewModel(ServiceProvider provider)
        {
            _provider = provider;

            var clipboardMonitor = _provider.GetService<ClipboardMonitor>();
            isMonitoring = clipboardMonitor?.IsMonitoring ?? true;
            alwaysPastePlainText = Preferences.PreferencesGateway.IsAlwaysPastePlainText();

            // 起動時に一度だけ、更新の有無を静かに確認する(ダウンロード・適用まではしない)。
            // 失敗(オフライン等)しても起動やメニュー表示は妨げない
            _ = CheckForUpdatesQuietlyAsync();
        }

        private async Task CheckForUpdatesQuietlyAsync()
        {
            var updateService = _provider.GetService<UpdateService>();
            if (updateService is null)
                return;

            IsUpdateAvailable = await updateService.CheckForUpdatesQuietlyAsync();
        }

        [RelayCommand]
        private void Open() => WeakReferenceMessenger.Default.Send(new OpenMainWindowMessage());

        [RelayCommand]
        private void OpenSettings() => WeakReferenceMessenger.Default.Send(new OpenSettingsMessage());

        [RelayCommand]
        private void OpenSequentialPaste() => WeakReferenceMessenger.Default.Send(new OpenSequentialPasteMessage());

        [RelayCommand]
        private void ToggleMonitoring()
        {
            var clipboardMonitor = _provider.GetService<ClipboardMonitor>();
            if (clipboardMonitor is null)
                return;

            if (clipboardMonitor.IsMonitoring)
                clipboardMonitor.Stop();
            else
                clipboardMonitor.Start();

            IsMonitoring = clipboardMonitor.IsMonitoring;
        }

        [RelayCommand]
        private void ToggleAlwaysPastePlainText()
        {
            AlwaysPastePlainText = !AlwaysPastePlainText;
            Preferences.PreferencesGateway.SetAlwaysPastePlainText(AlwaysPastePlainText);
        }

        // 終了はWindowの後始末を伴わない(WindowServiceが自分でProcessExitに登録済み)ため、
        // ここは単純にプロセスの終了を要求するだけでよい
        [RelayCommand]
        private void Quit() => Environment.Exit(0);

        /// <summary>
        /// 更新を確認し、あればダウンロード・適用して再起動する(成功時はここで
        /// プロセスが終了する)。無かった場合はIsUpdateAvailableをfalseに戻すだけ。
        /// </summary>
        [RelayCommand]
        private async Task CheckForUpdates()
        {
            var updateService = _provider.GetService<UpdateService>();
            if (updateService is null)
                return;

            var applied = await updateService.CheckDownloadAndApplyAsync();
            if (!applied)
                IsUpdateAvailable = false;
        }
    }
}
