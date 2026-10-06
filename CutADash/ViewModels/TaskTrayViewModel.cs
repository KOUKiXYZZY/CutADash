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

        public TaskTrayViewModel(ServiceProvider provider)
        {
            _provider = provider;

            var clipboardMonitor = _provider.GetService<ClipboardMonitor>();
            isMonitoring = clipboardMonitor?.IsMonitoring ?? true;
            alwaysPastePlainText = Preferences.PreferencesGateway.IsAlwaysPastePlainText();
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
        /// プロセスが終了する)。更新は、OK/キャンセルで尋ねてから行う。
        /// </summary>
        [RelayCommand]
        private async Task CheckForUpdates()
        {
            var updateService = _provider.GetService<UpdateService>();
            if (updateService is null)
                return;

            var version = await updateService.GetAvailableVersionAsync();
            // 更新が無ければ、その旨を知らせる(押しても何も起きないように見えないよう)。
            // あれば、OK/キャンセルで尋ねてから適用する
            if (version is null)
            {
                await Utils.ConfirmPrompt.NotifyAsync(Common.Utils.AppStrings.Get("Update_UpToDate"));
                return;
            }

            if (!await Utils.UpdatePrompt.ConfirmUpdateAsync(version))
                return;

            await updateService.CheckDownloadAndApplyAsync();
        }
    }
}
