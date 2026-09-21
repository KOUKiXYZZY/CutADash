using Common.Extension;
using Common.Infra.Win32;
using Common.Models;
using CommunityToolkit.Mvvm.Messaging;
using CutADash.Infra.Win32;
using CutADash.Messages;
using CutADash.Views;
using Microsoft.Extensions.DependencyInjection;
using Preferences;
using Preferences.Views;
using SequentialPaste;
using System;
using WinUIEx;

namespace CutADash.Services
{
    /// <summary>
    /// MainWindow/PreferenceWindowの生成・表示・後始末を一手に引き受けるサービス。
    /// 「Windowそのものを各所で引き回す」のを避けるため、App/TaskTray/SelectionToolbar等は
    /// このサービスのメソッド(またはMessenger経由のメッセージ)を呼ぶだけにし、Window型を
    /// 直接持ち回らない。TaskTrayViewModelのようなViewModelは、Windowを直接知らずに
    /// OpenMainWindowMessage等をWeakReferenceMessenger.Defaultへ送るだけでよい。
    /// </summary>
    public sealed class WindowService
    {
        private MainWindow? _window;
        private PreferenceWindow? _preferenceWindow;

        // DIコンテナはHotKeyServiceの構築(このサービスのOpenTabをコールバックとして渡す)より
        // 後に組み立てられるため、コンストラクタでは受け取れない。ProviderがビルドされてIから
        // 代入してもらう(SequentialPasteQueueViewModel.PasteToForeground等と同じ、
        // 下位層がコールバックだけ先に受け取る設計パターン)
        public ServiceProvider? Provider { get; set; }

        public WindowService()
        {
            WeakReferenceMessenger.Default.Register<OpenMainWindowMessage>(this, (r, m) => ActivateMainWindow());
            WeakReferenceMessenger.Default.Register<OpenSettingsMessage>(this, (r, m) => ShowSettings());
            WeakReferenceMessenger.Default.Register<OpenSequentialPasteMessage>(this, (r, m) =>
                Provider?.GetService<SequentialPasteService>()?.Toggle());
        }

        public MainWindow GetMainWindow()
        {
            if (_window is not null)
                return _window;

            _window = new MainWindow();
            _window.AppWindow.Closing += (s, e) =>
            {
                e.Cancel = true;
                s.Hide();
            };
            var wm = WindowManager.Get(_window);
            wm.WindowStateChanged += (s, state) => wm.AppWindow.IsShownInSwitchers = state != WindowState.Minimized;
            return _window;
        }

        // タスクトレイやSelectionToolbarからウィンドウを開く際に使う。ペースト先の記憶は
        // MainWindow.Activate()が自動的に行うため、ここではタブ切り替えとActivate()だけでよい
        public void ActivateMainWindow()
        {
            var mainWindow = GetMainWindow();

            // タスクトレイから開いた場合は常にHistoryタブを開き、直前にコピーした項目
            // (先頭)ではなく2番目の項目を選択状態にする(OpenTabのホットキー経路と同じ挙動)
            mainWindow.NavigateToTab("history", selectSecondItem: true);

            mainWindow.Activate();
        }

        /// <summary>ショートカットキーからウィンドウを開く。キャレット位置の直下に表示する。</summary>
        public void OpenTab(string tag)
        {
            var foregroundWindow = WinAPI.WinUser.GetForegroundWindow();

            // 設定で無効化されている場合は、キャレット検出自体を行わない
            // (COM呼び出しのコストも省ける)。前回の表示位置のまま開く
            var caretRect = PreferencesGateway.IsCaretPositioningDisabled()
                ? null
                : CaretInfoProvider.GetCaretInfo(foregroundWindow);

            var mainWindow = GetMainWindow();

            // 保存された前回位置の復元は、これより後でMoveToCaretするより前に
            // 済ませておく必要がある(逆順だとキャレット位置を前回位置で上書きしてしまう)
            mainWindow.RestoreWindowSizeOnce();

            // キャレット位置が取得できた場合のみ、そのすぐ下へウィンドウを移動する。
            // 取得できない(caretRect is null、対象アプリがキャレット位置を報告しない等)
            // 場合は前回の表示位置のまま
            if (caretRect is { IsVisible: true })
            {
                mainWindow.MoveToCaret(caretRect.Rect);
            }

            // フォーカスを奪わずに表示する。Explorerのファイル名変更中でも
            // 名前変更が確定されず、そのままペーストできるようにするため。
            // ペースト先の記憶(RememberPasteTarget)はMainWindow.Activate()が自動的に行う
            mainWindow.ShowActivate();

            // 履歴をショートカットキーで開いた場合、直前にコピーした項目(先頭)ではなく
            // 2番目の項目を選択状態にする
            mainWindow.NavigateToTab(tag, selectSecondItem: tag == "history");
        }

        public void ShowSettings()
        {
            if (_preferenceWindow is not null)
            {
                _preferenceWindow.Activate();
                return;
            }

            _preferenceWindow = new PreferenceWindow(Provider!);
            _preferenceWindow.Closed += (_, _) => _preferenceWindow = null;
            _preferenceWindow.Activate();
            _preferenceWindow.Show();
        }
    }
}
