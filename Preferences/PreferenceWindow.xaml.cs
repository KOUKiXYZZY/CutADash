using Common.Extension;
using Common.Infra.Win32;
using System;
using Preferences.Utils;
using Preferences.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Windowing;
using System.Collections.Generic;
using Windows.System;
using WinRT.Interop;
using WinUIEx;
using static WinAPI.WinUser;
using Microsoft.Extensions.DependencyInjection;

namespace Preferences.Views
{
    /// <summary>
    /// ショートカットキーの設定とAboutを表示するウィンドウ。
    /// 状態・保存ロジックはPreferenceViewModelが持ち、このコードビハインドには
    /// ウィンドウ自体の操作(ドラッグ・サイズ復元・キー捕捉の生イベント)だけを残す。
    /// 閉じたらイベント購読を解除し、インスタンスがメモリに残らないようにする。
    /// </summary>
    public sealed partial class PreferenceWindow : WindowEx
    {
        private bool _isCapturing;
        private uint _capturedModifiers;
        private string? _capturingKey;

        internal PreferenceViewModel ViewModel { get; }

        public PreferenceWindow(ServiceProvider provider)
        {
            ViewModel = new PreferenceViewModel(provider);

            InitializeComponent();

            LocalizeUi();

            // MainWindowと同様、境界線・タイトルバーを消して×ボタンだけにする
            this.EnableAcrylicBackdrop(); // アクリル素材を有効化
            this.RemoveTitleBar(); // タイトルバーを消す

            // 前回のウィンドウサイズ・位置を復元する。MainWindowと同じwindow_settings.jsonに
            // 保存するが、キー(PreferenceWindowKey)を分けているので互いを上書きしない。
            // 初回起動などまだ保存が無ければ、画面中央に表示する
            var restored = this.RestoreWindowSize<Common.Models.WindowSize>(PreferenceWindowKey);
            if (restored is null)
            {
                this.CenterOnScreen();
            }

            this.Content.KeyDown += OnKeyDown;

            // DragHandleをドラッグすることでウィンドウの移動をさせる(MainWindowと同じ仕組み)
            DragHandle.Loaded += OnDragHandleLoadedOrSizeChanged;
            DragHandle.SizeChanged += OnDragHandleLoadedOrSizeChanged;
            AppWindow.Changed += OnAppWindowChanged;

            this.Closed += OnClosed;

            // 生成コード(PreferenceWindow.g.cs)を確認したところ、x:Bindはウィンドウの
            // Activatedイベントのたびに Initialize()→Update_ViewModel_SelectedLanguageTag
            // を呼び直し、SelectedValueへViewModelの値を直接セットし直している。この
            // SelectedValueへの再代入は、値が""(システム既定)のときだけ一致する項目が
            // 見つからず選択解除されてしまう(WinUIの既知の挙動。空文字列は"何も選ばれて
            // いない"扱いと区別が曖昧になるらしい)。日本語等の非空値では問題が起きない
            // のはこのため。Activatedのたびに、SelectedItemを直接指定する方式で
            // 選び直して上書きする
            this.Activated += OnWindowActivated;
        }

        private void OnWindowActivated(object sender, WindowActivatedEventArgs e)
        {
            SelectComboBoxItemByTag(LanguageComboBox, ViewModel.SelectedLanguageTag);
            SelectComboBoxItemByTag(ThumbnailSizeComboBox, ViewModel.ThumbnailMaxDimensionTag);
        }

        // Strings/<lang>/Resources.reswから、このウィンドウの表示文字列をすべて設定する。
        // MainWindowのタブ名(NavigationViewItem)やトレイメニューと同じ仕組み(AppStrings)を使う
        private void LocalizeUi()
        {
            GeneralTabItem.Text = PreferencesStrings.Get("Pref_Tab_General");
            HistoryTabItem.Text = PreferencesStrings.Get("Pref_Tab_History");
            ShortcutsTabItem.Text = PreferencesStrings.Get("Pref_Tab_Shortcuts");
            ExcludedAppsTabItem.Text = PreferencesStrings.Get("Pref_Tab_ExcludedApps");
            SelectionToolbarTabItem.Text = PreferencesStrings.Get("Pref_Tab_SelectionToolbar");
            AboutTabItem.Text = PreferencesStrings.Get("Pref_Tab_About");

            GeneralSectionTitle.Text = PreferencesStrings.Get("Pref_General_Title");
            LaunchAtLoginCheckBox.Content = PreferencesStrings.Get("Pref_LaunchAtLogin");
            DisableCaretPositioningCheckBox.Content = PreferencesStrings.Get("Pref_DisableCaretPositioning");
            DisableWindowsClipboardHistoryCheckBox.Content = PreferencesStrings.Get("Pref_DisableWindowsClipboardHistory");

            LanguageSectionTitle.Text = PreferencesStrings.Get("Pref_Language_Title");
            LanguageDescriptionText.Text = PreferencesStrings.Get("Pref_Language_Description");
            LanguageSystemDefaultItem.Content = PreferencesStrings.Get("Pref_Language_SystemDefault");

            ThemeSectionTitle.Text = PreferencesStrings.Get("Pref_Theme_Title");
            ThemeDescriptionText.Text = PreferencesStrings.Get("Pref_Theme_Description");

            HistorySectionTitle.Text = PreferencesStrings.Get("Pref_History_Title");
            MaxHistoryCountLabel.Text = PreferencesStrings.Get("Pref_MaxHistoryCount");
            DisableImageHistoryCheckBox.Content = PreferencesStrings.Get("Pref_DisableImageHistory");
            ThumbnailSizeLabel.Text = PreferencesStrings.Get("Pref_ThumbnailSize");
            ThumbnailSizeSmallItem.Content = PreferencesStrings.Get("Pref_ThumbnailSize_Small");
            ThumbnailSizeMediumItem.Content = PreferencesStrings.Get("Pref_ThumbnailSize_Medium");
            ThumbnailSizeLargeItem.Content = PreferencesStrings.Get("Pref_ThumbnailSize_Large");
            ThumbnailSizeExtraLargeItem.Content = PreferencesStrings.Get("Pref_ThumbnailSize_ExtraLarge");
            ThumbnailSizeExtraLargePlusItem.Content = PreferencesStrings.Get("Pref_ThumbnailSize_ExtraLargePlus");
            ThumbnailSizeMaxItem.Content = PreferencesStrings.Get("Pref_ThumbnailSize_Max");

            ShortcutsSectionTitle.Text = PreferencesStrings.Get("Pref_Tab_Shortcuts");
            OpenHistoryLabel.Text = PreferencesStrings.Get("Pref_OpenHistory");
            OpenFavoriteLabel.Text = PreferencesStrings.Get("Pref_OpenFavorite");
            OpenEmojiLabel.Text = PreferencesStrings.Get("Pref_OpenEmoji");
            ChangeHistoryShortcutButton.Content = PreferencesStrings.Get("Pref_Change");
            ChangeFavoriteShortcutButton.Content = PreferencesStrings.Get("Pref_Change");
            ChangeEmojiShortcutButton.Content = PreferencesStrings.Get("Pref_Change");
            DeleteHistoryShortcutButton.Content = PreferencesStrings.Get("Pref_Delete");
            DeleteFavoriteShortcutButton.Content = PreferencesStrings.Get("Pref_Delete");
            DeleteEmojiShortcutButton.Content = PreferencesStrings.Get("Pref_Delete");
            ShortcutHintText.Text = PreferencesStrings.Get("Pref_ShortcutHint");

            ExcludedAppsSectionTitle.Text = PreferencesStrings.Get("Pref_ExcludedApps_Title");
            ExcludedAppsDescriptionText.Text = PreferencesStrings.Get("Pref_ExcludedApps_Description");
            NewExcludedAppTextBox.PlaceholderText = PreferencesStrings.Get("Pref_ExcludedApps_Placeholder");
            AddExcludedAppButton.Content = PreferencesStrings.Get("Pref_Add");

            // ExcludedAppsListViewの行ごとの「削除」ボタンは、DataTemplate内の
            // StaticResourceで参照しているため、ItemsSourceを設定する前(=各行が
            // 実際に生成される前)にリソースの中身を書き換えておく
            RootGrid.Resources["Pref_Delete_Str"] = PreferencesStrings.Get("Pref_Delete");

            SelectionToolbarSectionTitle.Text = PreferencesStrings.Get("Pref_Tab_SelectionToolbar");
            SelectionToolbarDescriptionText.Text = PreferencesStrings.Get("Pref_SelectionToolbar_Description");
            SelectionToolbarEnabledCheckBox.Content = PreferencesStrings.Get("Pref_SelectionToolbarEnabled");

            AboutDescriptionText.Text = PreferencesStrings.Get("Pref_About_Description");
            LicensesText.Text = OpenSourceLicenses.BuildLicensesText();

            // x:BindのSelectedValueは、InitializeComponent()の時点(=ComboBoxItemがまだ
            // Items未確定/Contentも未設定)で初回評価されるため、一致する項目が見つからず
            // 選択なし(空欄)のままになることがあるWinUIの既知の挙動がある。しかも
            // ViewModel側の値がその時点で既に一致している場合、SelectedValueへ同じ値を
            // 再代入しても変化なしとみなされ再評価されない(SelectedValueのsetterは
            // 値が変わらなければComboBoxItemの選び直しを行わない)。そのため、ここでは
            // SelectedValueではなくSelectedItem自体をTagで検索して選び直すことで、
            // Content設定・Itemsの確定を終えた状態から確実に表示を更新する
            // (例: 言語が未設定でSystem Defaultが選ばれているはずなのに空欄に見えていた不具合)
            SelectComboBoxItemByTag(LanguageComboBox, ViewModel.SelectedLanguageTag);
            SelectComboBoxItemByTag(ThumbnailSizeComboBox, ViewModel.ThumbnailMaxDimensionTag);
        }

        private static void SelectComboBoxItemByTag(ComboBox comboBox, string? tag)
        {
            foreach (var obj in comboBox.Items)
            {
                if (obj is ComboBoxItem item && (item.Tag as string) == tag)
                {
                    comboBox.SelectedItem = item;
                    return;
                }
            }
        }

        // MainWindowと同じwindow_settings.jsonを使うため、キーを分けて上書きし合わないようにする
        private const string PreferenceWindowKey = "PreferenceWindow";

        private void OnClosed(object sender, WindowEventArgs args)
        {
            // 次回も同じ位置・サイズで開けるよう保存する
            this.SaveWindowSize<Common.Models.WindowSize>(key: PreferenceWindowKey, savePosition: true);

            // 参照が残ってGCされなくなるのを防ぐため、購読したイベントを全て外す
            this.Content.KeyDown -= OnKeyDown;
            DragHandle.Loaded -= OnDragHandleLoadedOrSizeChanged;
            DragHandle.SizeChanged -= OnDragHandleLoadedOrSizeChanged;
            AppWindow.Changed -= OnAppWindowChanged;
            this.Activated -= OnWindowActivated;
            this.Closed -= OnClosed;
        }

        private void OnDragHandleLoadedOrSizeChanged(object sender, object e)
        {
            this.UpdateDragRegions(DragHandle);
        }

        private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
        {
            if (args.DidSizeChange)
            {
                this.UpdateDragRegions(DragHandle);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void SectionSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
        {
            var index = sender.Items.IndexOf(sender.SelectedItem);

            SettingsPanel.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
            HistoryPanel.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
            ShortcutsPanel.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
            ExcludedAppsPanel.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
            SelectionToolbarPanel.Visibility = index == 4 ? Visibility.Visible : Visibility.Collapsed;
            AboutPanel.Visibility = index == 5 ? Visibility.Visible : Visibility.Collapsed;
        }

        // Enterキーでも追加ボタンと同じ動作にする
        private void NewExcludedAppTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                ViewModel.AddExcludedAppCommand.Execute(null);
                e.Handled = true;
            }
        }

        private void RemoveExcludedAppButton_Click(object sender, RoutedEventArgs e)
        {
            if (((Button)sender).Tag is not string name)
                return;

            ViewModel.RemoveExcludedAppCommand.Execute(name);
        }

        // ショートカットのキー捕捉は、Windowのキー入力を直接見る必要がある(RichEditBox等の
        // フォーカスを介さない)View固有の処理のため、ここに残す。捕捉した結果の反映
        // (HotKeyServiceへの保存・表示更新)はViewModelへ委譲する
        private void ChangeShortcutButton_Click(object sender, RoutedEventArgs e)
        {
            var key = (string)((Button)sender).Tag;

            _capturingKey = key;
            _isCapturing = true;
            _capturedModifiers = 0;

            SetCapturingDisplay(key, PreferencesStrings.Get("Pref_WaitingForKeyInput"));
            ShortcutHintText.Visibility = Visibility.Visible;
        }

        private void SetCapturingDisplay(string key, string text)
        {
            switch (key)
            {
                case "history": ViewModel.HistoryShortcutText = text; break;
                case "favorite": ViewModel.FavoriteShortcutText = text; break;
                case "emoji": ViewModel.EmojiShortcutText = text; break;
            }
        }

        private void OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (!_isCapturing)
                return;

            switch (e.Key)
            {
                case VirtualKey.Control:
                case VirtualKey.LeftControl:
                case VirtualKey.RightControl:
                    _capturedModifiers |= HotKeyNative.HotKeyModifiers.MOD_CONTROL;
                    e.Handled = true;
                    return;
                case VirtualKey.Shift:
                case VirtualKey.LeftShift:
                case VirtualKey.RightShift:
                    _capturedModifiers |= HotKeyNative.HotKeyModifiers.MOD_SHIFT;
                    e.Handled = true;
                    return;
                case VirtualKey.Menu:
                case VirtualKey.LeftMenu:
                case VirtualKey.RightMenu:
                    _capturedModifiers |= HotKeyNative.HotKeyModifiers.MOD_ALT;
                    e.Handled = true;
                    return;
                case VirtualKey.LeftWindows:
                case VirtualKey.RightWindows:
                    _capturedModifiers |= HotKeyNative.HotKeyModifiers.MOD_WIN;
                    e.Handled = true;
                    return;
                case VirtualKey.Escape:
                    CancelCapture();
                    e.Handled = true;
                    return;
            }

            // 修飾キーなしの組み合わせは登録できない(他の操作と衝突しやすいため)
            if (_capturedModifiers == 0)
            {
                e.Handled = true;
                return;
            }

            var key = _capturingKey!;
            var newDefinition = new HotKeyDefinition(_capturedModifiers, (uint)e.Key);

            _isCapturing = false;
            _capturingKey = null;
            ShortcutHintText.Visibility = Visibility.Collapsed;
            ViewModel.ApplyCapturedShortcut(key, newDefinition);

            e.Handled = true;
        }

        private void CancelCapture()
        {
            var key = _capturingKey;

            _isCapturing = false;
            _capturingKey = null;
            ShortcutHintText.Visibility = Visibility.Collapsed;

            if (key is not null)
            {
                ViewModel.RefreshShortcutDisplay(key);
            }
        }
    }
}
