using Common.Extension;
using Common.Infra.Win32;
using System;
using System.IO;
using Preferences.Utils;
using Preferences.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
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
        internal PreferenceViewModel ViewModel { get; }

        public PreferenceWindow(ServiceProvider provider)
        {
            ViewModel = new PreferenceViewModel(provider);

            InitializeComponent();

            LocalizeUi();

            // 明/暗の表示テーマ(Light/Dark)。バックドロップより先に適用する
            this.ApplyColorTheme(PreferencesGateway.GetColorTheme());
            // 設定画面は開くたびに新しく作られ、閉じても購読が残ると、閉じた後のウィンドウに対して
            // テーマ変更のたびに処理が走り、例外でアプリが落ちる。閉じる時に必ず解除する(OnClosed)
            _colorThemeChangedHandler = theme =>
                this.DispatcherQueue.TryEnqueue(() => this.ApplyColorTheme(theme));
            PreferencesGateway.ColorThemeChanged += _colorThemeChangedHandler;

            // MainWindowと同様、境界線・タイトルバーを消して×ボタンだけにする
            this.EnableAcrylicBackdrop(); // アクリル素材を有効化
            TitleBarRow.AttachTo(this); // システムのタイトルバーを消し、自前のタイトルバーを使う

            // サイズは固定(748x487 DIP。XAMLのWidth/Heightとサイズ変更不可の指定)。
            // 位置は毎回、画面中央に表示する
            this.CenterOnScreen();

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
            AppearanceTabItem.Text = PreferencesStrings.Get("Pref_Tab_Appearance");
            AppearanceSectionTitle.Text = PreferencesStrings.Get("Pref_Tab_Appearance");
            HistoryTabItem.Text = PreferencesStrings.Get("Pref_Tab_History");
            ShortcutsTabItem.Text = PreferencesStrings.Get("Pref_Tab_Shortcuts");
            ExcludedAppsTabItem.Text = PreferencesStrings.Get("Pref_Tab_ExcludedApps");
            SelectionToolbarTabItem.Text = PreferencesStrings.Get("Pref_Tab_SelectionToolbar");
            AboutTabItem.Text = PreferencesStrings.Get("Pref_Tab_About");

            GeneralSectionTitle.Text = PreferencesStrings.Get("Pref_General_Title");
            LaunchAtLoginCheckBox.Content = PreferencesStrings.Get("Pref_LaunchAtLogin");
            DisableCaretPositioningCheckBox.Content = PreferencesStrings.Get("Pref_DisableCaretPositioning");
            DisableWindowsClipboardHistoryCheckBox.Content = PreferencesStrings.Get("Pref_DisableWindowsClipboardHistory");
            ClipboardHistoryRestartNoteText.Text = PreferencesStrings.Get("Pref_DisableWindowsClipboardHistory_Restart");
            ContentsPopupCheckBox.Content = PreferencesStrings.Get("Pref_ContentsPopup");
            CopyOnlyOnSelectCheckBox.Content = PreferencesStrings.Get("Pref_CopyOnlyOnSelect");
            CopyOnlyOnSelectNoteText.Text = PreferencesStrings.Get("Pref_CopyOnlyOnSelect_Note");

            LanguageSectionTitle.Text = PreferencesStrings.Get("Pref_Language_Title");
            LanguageDescriptionText.Text = PreferencesStrings.Get("Pref_Language_Description");
            LanguageSystemDefaultItem.Content = PreferencesStrings.Get("Pref_Language_SystemDefault");

            ColorThemeSectionTitle.Text = PreferencesStrings.Get("Pref_ColorTheme_Title");
            ColorThemeDescriptionText.Text = PreferencesStrings.Get("Pref_ColorTheme_Description");
            ColorThemeDefaultItem.Content = PreferencesStrings.Get("Pref_ColorTheme_Default");
            ColorThemeLightItem.Content = PreferencesStrings.Get("Pref_ColorTheme_Light");
            ColorThemeDarkItem.Content = PreferencesStrings.Get("Pref_ColorTheme_Dark");

            BackdropSectionTitle.Text = PreferencesStrings.Get("Pref_Backdrop_Title");
            BackdropDescriptionText.Text = PreferencesStrings.Get("Pref_Backdrop_Description");

            HistorySectionTitle.Text = PreferencesStrings.Get("Pref_History_Title");
            MaxHistoryCountLabel.Text = PreferencesStrings.Get("Pref_MaxHistoryCount");
            DisableImageHistoryCheckBox.Content = PreferencesStrings.Get("Pref_DisableImageHistory");
            ThumbnailSizeLabel.Text = PreferencesStrings.Get("Pref_ThumbnailSize");
            ThumbnailSizeSmallItem.Content = PreferencesStrings.Get("Pref_ThumbnailSize_Small");
            ThumbnailSizeMediumItem.Content = PreferencesStrings.Get("Pref_ThumbnailSize_Medium");
            ThumbnailSizeLargeItem.Content = PreferencesStrings.Get("Pref_ThumbnailSize_Large");

            ShortcutsSectionTitle.Text = PreferencesStrings.Get("Pref_Tab_Shortcuts");
            OpenHistoryLabel.Text = PreferencesStrings.Get("Pref_OpenHistory");
            OpenFavoriteLabel.Text = PreferencesStrings.Get("Pref_OpenFavorite");
            OpenEmojiLabel.Text = PreferencesStrings.Get("Pref_OpenEmoji");
            foreach (var box in new[] { HistoryShortcutBox, FavoriteShortcutBox, EmojiShortcutBox })
            {
                box.PlaceholderText = PreferencesStrings.Get("Pref_NotSet");
                box.CapturingText = PreferencesStrings.Get("Pref_WaitingForKeyInput");
            }

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
            SelectionToolbarPlacementLabel.Text = PreferencesStrings.Get("Pref_SelectionToolbarPlacement");
            ToolbarPlacementAboveItem.Content = PreferencesStrings.Get("Pref_ToolbarPlacement_Above");
            ToolbarPlacementBelowItem.Content = PreferencesStrings.Get("Pref_ToolbarPlacement_Below");
            ToolbarPlacementLeftItem.Content = PreferencesStrings.Get("Pref_ToolbarPlacement_Left");
            ToolbarPlacementRightItem.Content = PreferencesStrings.Get("Pref_ToolbarPlacement_Right");

            AboutDescriptionText.Text = PreferencesStrings.Get("Pref_About_Description");
            LicensesText.Text = OpenSourceLicenses.BuildLicensesText();
            LoadAboutAppIcon();

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

        private Action<Common.Models.AppColorTheme>? _colorThemeChangedHandler;

        private void OnClosed(object sender, WindowEventArgs args)
        {
            if (_colorThemeChangedHandler is not null)
            {
                PreferencesGateway.ColorThemeChanged -= _colorThemeChangedHandler;
                _colorThemeChangedHandler = null;
            }

            // 参照が残ってGCされなくなるのを防ぐため、購読したイベントを全て外す
            this.Activated -= OnWindowActivated;
            this.Closed -= OnClosed;
        }

        /// <summary>
        /// About画面のアプリアイコンを読み込む。非パッケージ(unpackaged)アプリでは
        /// ms-appx:///によるパッケージリソース解決が実行時に効かないため
        /// (Emoji/Navアイコンで踏んだのと同じ制約)、実行ファイルと同じ場所に
        /// 配置されたAssetsを直接ファイルパスで参照する。
        /// </summary>
        private void LoadAboutAppIcon()
        {
            try
            {
                var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.png");
                var bitmap = new BitmapImage();
                using (var stream = System.IO.File.OpenRead(path))
                {
                    bitmap.SetSource(stream.AsRandomAccessStream());
                }
                AboutAppIcon.Source = bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PreferenceWindow] About画面のアイコン読み込みに失敗: {ex}");
            }
        }

        private void SectionSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
        {
            var index = sender.Items.IndexOf(sender.SelectedItem);

            SettingsPanel.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
            AppearancePanel.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
            HistoryPanel.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
            ShortcutsPanel.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
            ExcludedAppsPanel.Visibility = index == 4 ? Visibility.Visible : Visibility.Collapsed;
            SelectionToolbarPanel.Visibility = index == 5 ? Visibility.Visible : Visibility.Collapsed;
            AboutPanel.Visibility = index == 6 ? Visibility.Visible : Visibility.Collapsed;
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

    }
}
