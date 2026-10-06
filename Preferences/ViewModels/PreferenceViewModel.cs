using Common.Infra.Win32;
using Common.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Preferences.Utils;
using UiLibrary;
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
    /// ショートカットキーの入力は、UiLibraryのShortcutKeyBoxが受け持つ。ViewModelはその値
    /// (ShortcutKey)を保持し、変更されたらStoreへ保存する。
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

        // 「Windows標準のクリップボード履歴を無効にする」をチェックした直後だけ、赤字で
        // 「再起動が必要」と知らせる。設定画面を開き直した時や、チェックを外した時は出さない
        [ObservableProperty] private bool showClipboardHistoryRestartNote;
        [ObservableProperty] private bool copyOnlyOnSelect;
        [ObservableProperty] private bool contentsPopupEnabled;

        // ComboBoxのSelectedValuePath="Tag"に合わせ、WindowBackdropKindの
        // メンバー名の文字列("Mica"/"Acrylic"/"Blur"/"Cat")として保持する
        [ObservableProperty] private string selectedBackdropTag = nameof(WindowBackdropKind.Acrylic);

        // ComboBoxのSelectedValuePath="Tag"に合わせ、AppColorThemeのメンバー名の文字列
        // ("Light"/"Dark")として保持する
        [ObservableProperty] private string selectedColorThemeTag = nameof(AppColorTheme.Default);

        // 言語のComboBoxで「システム既定」を表すTag。設定ファイルには空文字列で保存するが、
        // ComboBoxのItemのTagに空文字列を使うと、SelectedValueと一致する項目が無い扱いになり
        // 起動後の表示が空欄になる(実際に踏んだ不具合)ため、画面側だけは空でない値にする
        private const string SystemLanguageTag = "system";

        // ComboBoxのSelectedValuePath="Tag"に合わせ、BCP-47言語タグ("ja-JP"/"en-US"/"zh-CN")、
        // またはSystemLanguageTag(システム既定)として保持する
        [ObservableProperty] private string selectedLanguageTag = SystemLanguageTag;

        [ObservableProperty] private double maxHistoryCount;
        [ObservableProperty] private bool disableImageHistory;

        // ComboBoxのSelectedValuePath="Tag"に合わせ、px数値を文字列で保持する("100"〜"800")
        [ObservableProperty] private string thumbnailMaxDimensionTag = "400";

        // ComboBoxのSelectedValuePath="Tag"に合わせ、ToolbarPlacementのメンバー名の文字列
        // ("Above"/"Below"/"Left"/"Right")として保持する
        [ObservableProperty] private string selectedToolbarPlacementTag = nameof(ToolbarPlacement.Above);

        [ObservableProperty] private bool selectionToolbarEnabled;
        [ObservableProperty] private double selectionToolbarAutoHideSeconds;
        [ObservableProperty] private string selectionToolbarAutoHideLabel = string.Empty;

        [ObservableProperty] private string newExcludedAppName = string.Empty;
        public ObservableCollection<string> ExcludedAppNames { get; } = new();

        [ObservableProperty] private string versionText = string.Empty;

        [ObservableProperty] private ShortcutKey? historyShortcut;
        [ObservableProperty] private ShortcutKey? favoriteShortcut;
        [ObservableProperty] private ShortcutKey? emojiShortcut;

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
            CopyOnlyOnSelect = PreferencesGateway.IsCopyOnlyOnSelect();
            ContentsPopupEnabled = PreferencesGateway.IsContentsPopupEnabled();

            SelectionToolbarEnabled = PreferencesGateway.IsSelectionToolbarEnabled();
            // 無効化中は実際の秒数(GetSelectionToolbarAutoHideSeconds)が0になっているため、
            // 直前に有効だった値(バックアップ)を表示する
            SelectionToolbarAutoHideSeconds = PreferencesGateway.GetSelectionToolbarAutoHideSecondsBackup();
            UpdateSelectionToolbarAutoHideLabel();

            SelectedToolbarPlacementTag = PreferencesGateway.GetSelectionToolbarPlacement().ToString();
            SelectedBackdropTag = PreferencesGateway.GetWindowBackdrop().ToString();
            SelectedColorThemeTag = PreferencesGateway.GetColorTheme().ToString();
            var savedLanguage = PreferencesGateway.GetLanguage();
            SelectedLanguageTag = savedLanguage.Length == 0 ? SystemLanguageTag : savedLanguage;

            MaxHistoryCount = PreferencesGateway.GetMaxHistoryCount();
            DisableImageHistory = PreferencesGateway.IsImageHistoryDisabled();
            ThumbnailMaxDimensionTag = PreferencesGateway.GetThumbnailMaxDimension().ToString();

            // AssemblyVersionは既定でRevision(4桁目)が常に0になるため、
            // 表示上はMajor.Minor.Buildの3桁("v1.0.0"形式)に揃える
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            var versionText = version is null ? "?" : $"v{version.ToString(3)}";
            VersionText = string.Format(PreferencesStrings.Get("Pref_Version"), versionText);

            ExcludedAppNames.Clear();
            foreach (var name in PreferencesGateway.GetExcludedAppNames())
                ExcludedAppNames.Add(name);

            HistoryShortcut = ReadShortcut("history");
            FavoriteShortcut = ReadShortcut("favorite");
            EmojiShortcut = ReadShortcut("emoji");
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

        partial void OnContentsPopupEnabledChanged(bool value)
        {
            if (_isLoadingPreferences)
                return;

            HistorySettingsStore.SetContentsPopupEnabled(value);
        }

        partial void OnCopyOnlyOnSelectChanged(bool value)
        {
            if (_isLoadingPreferences)
                return;

            HistorySettingsStore.SetCopyOnlyOnSelect(value);
        }

        // HKLMへの書き込みで管理者権限の確認(UAC)が出る。キャンセルされた/失敗した場合は、
        // チェックを元に戻す(戻す操作で、この処理が再度走らないようガードする)
        async partial void OnDisableWindowsClipboardHistoryChanged(bool value)
        {
            if (_isLoadingPreferences)
                return;

            if (await HistorySettingsStore.SetWindowsClipboardHistoryDisabledAsync(value))
            {
                ShowClipboardHistoryRestartNote = value;
                return;
            }

            _isLoadingPreferences = true;
            DisableWindowsClipboardHistory = !value;
            _isLoadingPreferences = false;
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

        // 次にツールバーが表示される時から反映される(表示のたびに設定を読み直す)
        partial void OnSelectedToolbarPlacementTagChanged(string value)
        {
            if (_isLoadingPreferences)
                return;

            if (Enum.TryParse<ToolbarPlacement>(value, out var placement))
                HistorySettingsStore.SetSelectionToolbarPlacement(placement);
        }

        // アプリ全体の明/暗の表示テーマ。切り替えは即座に反映される
        // (各ウィンドウがHistorySettingsStore.ColorThemeChangedを購読して適用する)
        partial void OnSelectedColorThemeTagChanged(string value)
        {
            if (_isLoadingPreferences)
                return;

            if (Enum.TryParse<AppColorTheme>(value, out var theme))
                HistorySettingsStore.SetColorTheme(theme);
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
            // システム既定のつもりが実質的な既定値として機能しなくなってしまう。
            // null/空白はシステム既定として扱う
            if (string.IsNullOrWhiteSpace(value))
            {
                SelectedLanguageTag = SystemLanguageTag;
                return;
            }

            // 画面上の「システム既定」(SystemLanguageTag)は、保存・適用では空文字列にする
            if (value == SystemLanguageTag)
                value = "";

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

        // 現在登録されているホットキーを、ShortcutKeyBoxの値(ShortcutKey)にして返す
        private ShortcutKey? ReadShortcut(string key)
        {
            var hotKeyService = _provider.GetRequiredKeyedService<HotKeyService>(key);
            return hotKeyService.Current is { } definition
                ? new ShortcutKey((ShortcutModifiers)definition.Modifiers, definition.VirtualKey)
                : null;
        }

        partial void OnHistoryShortcutChanged(ShortcutKey? value) => SaveShortcut("history", value);

        partial void OnFavoriteShortcutChanged(ShortcutKey? value) => SaveShortcut("favorite", value);

        partial void OnEmojiShortcutChanged(ShortcutKey? value) => SaveShortcut("emoji", value);

        // Storeへの保存だけ行う。実際のWin32登録の更新・解除(HotKeyService.Update/Remove)は、
        // App.xaml.csがPreferencesGateway.HotKeyChangedを購読して反映する
        // (ThumbnailMaxDimensionChanged/SelectionToolbarEnabledChangedと同じパターン)
        private void SaveShortcut(string key, ShortcutKey? value)
        {
            if (_isLoadingPreferences)
                return;

            if (value is null)
                HotKeySettingsStore.Remove(key);
            else
                HotKeySettingsStore.Save(key, new HotKeyDefinition((uint)value.Modifiers, value.VirtualKey));
        }
    }
}
