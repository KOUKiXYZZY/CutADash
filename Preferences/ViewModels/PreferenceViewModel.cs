using Common.Infra.Win32;
using Common.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Preferences.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using static WinAPI.WinUser;

namespace Preferences.ViewModels
{
    /// <summary>
    /// 設定画面(PreferenceWindow)の状態とロジックを持つViewModel。
    /// 読み込みはPreferencesGateway(読み取り専用ファサード)経由、書き込みは
    /// 対応するStore(HistorySettingsStore等)を直接呼ぶ(PreferencesGatewayは
    /// 書き込みを持たない設計にしたため)。
    ///
    /// ショートカットキーの実際のキー捕捉(KeyDownの生イベント)だけはView側の責務として
    /// PreferenceWindow.xaml.csに残し、確定した結果の反映(ApplyCapturedShortcut)は
    /// こちらで行う。
    /// </summary>
    internal partial class PreferenceViewModel : ObservableObject
    {
        private readonly ServiceProvider _provider;

        // 初期値をコードから設定する際、対応するOnXxxChangedが副作用(保存)を
        // 発火させてしまうのを防ぐためのフラグ(元のコードビハインドの_isLoadingPreferencesと同じ役割)
        private bool _isLoadingPreferences = true;

        [ObservableProperty] private bool launchAtLogin;
        [ObservableProperty] private bool disableCaretPositioning;
        [ObservableProperty] private bool disableWindowsClipboardHistory;

        // ComboBoxのSelectedValuePath="Tag"に合わせ、WindowBackdropKindの
        // メンバー名の文字列("Mica"/"Acrylic"/"Blur"/"Cat")として保持する
        [ObservableProperty] private string selectedBackdropTag = nameof(WindowBackdropKind.Acrylic);

        // ComboBoxのSelectedValuePath="Tag"に合わせ、BCP-47言語タグ("ja-JP"/"en-US"/"zh-CN")、
        // または空文字列(システム既定)として保持する
        [ObservableProperty] private string selectedLanguageTag = "";

        [ObservableProperty] private double maxHistoryCount;
        [ObservableProperty] private bool disableImageHistory;

        // ComboBoxのSelectedValuePath="Tag"に合わせ、px数値を文字列で保持する("100"〜"800")
        [ObservableProperty] private string thumbnailMaxDimensionTag = "200";

        [ObservableProperty] private bool selectionToolbarEnabled;
        [ObservableProperty] private double selectionToolbarAutoHideSeconds;
        [ObservableProperty] private string selectionToolbarAutoHideLabel = string.Empty;

        [ObservableProperty] private string newExcludedAppName = string.Empty;
        public ObservableCollection<string> ExcludedAppNames { get; } = new();

        [ObservableProperty] private string versionText = string.Empty;

        [ObservableProperty] private string historyShortcutText = string.Empty;
        [ObservableProperty] private string favoriteShortcutText = string.Empty;
        [ObservableProperty] private string emojiShortcutText = string.Empty;

        public PreferenceViewModel(ServiceProvider provider)
        {
            _provider = provider;
            Load();
            _isLoadingPreferences = false;
        }

        private void Load()
        {
            LaunchAtLogin = StartupHelper.IsRegistered();
            DisableCaretPositioning = PreferencesGateway.IsCaretPositioningDisabled();
            DisableWindowsClipboardHistory = PreferencesGateway.IsWindowsClipboardHistoryDisabled();

            SelectionToolbarEnabled = PreferencesGateway.IsSelectionToolbarEnabled();
            // 無効化中は実際の秒数(GetSelectionToolbarAutoHideSeconds)が0になっているため、
            // 直前に有効だった値(バックアップ)を表示する
            SelectionToolbarAutoHideSeconds = PreferencesGateway.GetSelectionToolbarAutoHideSecondsBackup();
            UpdateSelectionToolbarAutoHideLabel();

            SelectedBackdropTag = PreferencesGateway.GetWindowBackdrop().ToString();
            SelectedLanguageTag = PreferencesGateway.GetLanguage();

            MaxHistoryCount = PreferencesGateway.GetMaxHistoryCount();
            DisableImageHistory = PreferencesGateway.IsImageHistoryDisabled();
            ThumbnailMaxDimensionTag = PreferencesGateway.GetThumbnailMaxDimension().ToString();

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            VersionText = string.Format(PreferencesStrings.Get("Pref_Version"), version);

            ExcludedAppNames.Clear();
            foreach (var name in PreferencesGateway.GetExcludedAppNames())
                ExcludedAppNames.Add(name);

            RefreshShortcutDisplay("history");
            RefreshShortcutDisplay("favorite");
            RefreshShortcutDisplay("emoji");
        }

        partial void OnLaunchAtLoginChanged(bool value)
        {
            if (_isLoadingPreferences)
                return;

            if (value)
                StartupHelper.RegisterStartup();
            else
                StartupHelper.UnregisterStartup();
        }

        partial void OnDisableCaretPositioningChanged(bool value)
        {
            if (_isLoadingPreferences)
                return;

            HistorySettingsStore.SetCaretPositioningDisabled(value);
        }

        partial void OnDisableWindowsClipboardHistoryChanged(bool value)
        {
            if (_isLoadingPreferences)
                return;

            HistorySettingsStore.SetWindowsClipboardHistoryDisabled(value);
        }

        // メインウィンドウ(パレット)の背景素材(バックドロップ)。切り替えは即座に反映される
        // (MainWindow側がHistorySettingsStore.WindowBackdropChangedを購読して適用する)
        partial void OnSelectedBackdropTagChanged(string value)
        {
            if (_isLoadingPreferences)
                return;

            if (Enum.TryParse<WindowBackdropKind>(value, out var backdrop))
                HistorySettingsStore.SetWindowBackdrop(backdrop);
        }

        // 表示言語。保存とWindows.Globalization.ApplicationLanguages.PrimaryLanguageOverrideへの
        // 反映はすぐ行うが、既に読み込み済みのXAML/コードビハインドの文字列までは差し替わらない
        // ため、完全に反映するには再起動が必要(PreferenceWindow.xaml側で案内する)
        partial void OnSelectedLanguageTagChanged(string value)
        {
            if (_isLoadingPreferences)
                return;

            // ComboBoxのTwoWayバインディングが初期化のタイミングでnullを送り返してくることが
            // ある(WinUIの既知の挙動。選択項目の解決前にバインディングが一度走るため)。
            // ここで丸めないと設定ファイルに"Language":nullや空白だけの文字列が保存され続け、
            // システム既定(空文字列)のつもりが実質的な既定値として機能しなくなってしまう。
            // 実際に"System Default"項目(Tag="")が選ばれた場合の空文字列は、丸めずに
            // そのまま下の通常経路(保存・PrimaryLanguageOverride反映)へ流す
            if (value is null || (value.Length > 0 && string.IsNullOrWhiteSpace(value)))
            {
                SelectedLanguageTag = "";
                return;
            }

            HistorySettingsStore.SetLanguage(value);

            // 非パッケージアプリの環境によってはこのAPIが例外を投げることが確認されている
            // (App.xaml.csコンストラクタ側と同じ理由)。保存自体は上で済んでいるので、
            // ここで失敗しても次回起動時の適用(App.xaml.cs側)には影響しない
            try
            {
                Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = value;
            }
            catch
            {
            }

            // PrimaryLanguageOverrideは非パッケージアプリでは反映されないため、
            // 実際に文字列取得で参照されるResourceContextの"Language"修飾子を直接切り替える
            Common.Utils.AppStrings.SetLanguage(value);
        }

        // 保持する履歴の件数が変更されたら即座に保存する。
        // HistorySettingsStore.MaxHistoryCountChangedを購読しているClipboardListViewModel
        // (CutADash側)にも、この呼び出しの中で即座に通知される。
        partial void OnMaxHistoryCountChanged(double value)
        {
            if (_isLoadingPreferences || double.IsNaN(value))
                return;

            HistorySettingsStore.SetMaxHistoryCount((int)value);
        }

        partial void OnDisableImageHistoryChanged(bool value)
        {
            if (_isLoadingPreferences)
                return;

            HistorySettingsStore.SetImageHistoryDisabled(value);
        }

        // サムネイルサイズを保存する。HistorySettingsStore.ThumbnailMaxDimensionChangedを
        // 購読しているClipboardRepository(CutADash側)にも即座に通知される
        // (反映は次に画像をコピーした時から。既存の履歴のサムネイルは再生成しない)。
        partial void OnThumbnailMaxDimensionTagChanged(string value)
        {
            if (_isLoadingPreferences)
                return;

            if (int.TryParse(value, out var dimension))
                HistorySettingsStore.SetThumbnailMaxDimension(dimension);
        }

        partial void OnSelectionToolbarEnabledChanged(bool value)
        {
            // 読み込み中にプログラムから設定するとここが発火してしまう。その時点では
            // まだSelectionToolbarAutoHideSecondsが実際の値に設定されておらず、下の
            // SetSelectionToolbarAutoHideSeconds経由でバックアップ済みの秒数を
            // 上書き・破壊してしまうため、読み込み中は何もしない
            if (_isLoadingPreferences)
                return;

            HistorySettingsStore.SetSelectionToolbarEnabled(value);

            // チェックを外している間は実際の秒数を0(無効)にする。バックアップされた値は
            // SetSelectionToolbarAutoHideSeconds側で保持されるため、チェックを入れ直すと
            // 表示値(SelectionToolbarAutoHideSeconds)がそのまま復元される
            HistorySettingsStore.SetSelectionToolbarAutoHideSeconds(value ? SelectionToolbarAutoHideSeconds : 0);
        }

        partial void OnSelectionToolbarAutoHideSecondsChanged(double value)
        {
            // チェックが外れている間に表示専用の値(バックアップ)を設定しても、
            // 実際の秒数(0)を上書きしてしまわないようにする
            if (!_isLoadingPreferences && SelectionToolbarEnabled)
                HistorySettingsStore.SetSelectionToolbarAutoHideSeconds(value);

            UpdateSelectionToolbarAutoHideLabel();
        }

        private void UpdateSelectionToolbarAutoHideLabel()
        {
            SelectionToolbarAutoHideLabel = string.Format(
                PreferencesStrings.Get("Pref_SelectionToolbarAutoHideSeconds"),
                (int)SelectionToolbarAutoHideSeconds);
        }

        [RelayCommand]
        private void AddExcludedApp()
        {
            var name = NewExcludedAppName.Trim();
            if (name.Length == 0)
                return;

            // 既に登録済み(大文字/小文字を区別しない)なら追加しない
            foreach (var existing in ExcludedAppNames)
            {
                if (string.Equals(existing, name, StringComparison.OrdinalIgnoreCase))
                {
                    NewExcludedAppName = string.Empty;
                    return;
                }
            }

            ExcludedAppNames.Add(name);
            NewExcludedAppName = string.Empty;

            SaveExcludedAppNames();
        }

        [RelayCommand]
        private void RemoveExcludedApp(string name)
        {
            ExcludedAppNames.Remove(name);
            SaveExcludedAppNames();
        }

        private void SaveExcludedAppNames()
            => HistorySettingsStore.SetExcludedAppNames(new List<string>(ExcludedAppNames));

        [RelayCommand]
        private void DeleteShortcut(string key)
        {
            // Storeへの保存だけ行う。実際のWin32登録解除(HotKeyService.Remove)は、
            // App.xaml.csがPreferencesGateway.HotKeyChangedを購読して反映する
            // (ThumbnailMaxDimensionChanged/SelectionToolbarEnabledChangedと同じパターン)
            HotKeySettingsStore.Remove(key);
            RefreshShortcutDisplay(key);
        }

        /// <summary>
        /// キー入力の捕捉自体はView側(Window.KeyDown)が行うため、確定した定義の反映だけを
        /// こちらで受け持つ。Storeへの保存だけ行い、実際のWin32登録更新
        /// (HotKeyService.Update)はApp.xaml.csがPreferencesGateway.HotKeyChangedを
        /// 購読して反映する。
        /// </summary>
        public void ApplyCapturedShortcut(string key, HotKeyDefinition definition)
        {
            HotKeySettingsStore.Save(key, definition);
            RefreshShortcutDisplay(key);
        }

        public void RefreshShortcutDisplay(string key)
        {
            var hotKeyService = _provider.GetRequiredKeyedService<HotKeyService>(key);
            var text = hotKeyService.Current is { } definition
                ? FormatHotKey(definition)
                : PreferencesStrings.Get("Pref_NotSet");

            switch (key)
            {
                case "history": HistoryShortcutText = text; break;
                case "favorite": FavoriteShortcutText = text; break;
                case "emoji": EmojiShortcutText = text; break;
            }
        }

        private static string FormatHotKey(HotKeyDefinition definition)
        {
            var parts = new List<string>();

            if ((definition.Modifiers & HotKeyNative.HotKeyModifiers.MOD_CONTROL) != 0)
                parts.Add("Ctrl");
            if ((definition.Modifiers & HotKeyNative.HotKeyModifiers.MOD_SHIFT) != 0)
                parts.Add("Shift");
            if ((definition.Modifiers & HotKeyNative.HotKeyModifiers.MOD_ALT) != 0)
                parts.Add("Alt");
            if ((definition.Modifiers & HotKeyNative.HotKeyModifiers.MOD_WIN) != 0)
                parts.Add("Win");

            parts.Add(((Windows.System.VirtualKey)definition.VirtualKey).ToString());

            return string.Join(" + ", parts);
        }
    }
}
