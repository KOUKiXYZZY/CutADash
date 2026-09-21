using Common.Utils;
using CutADash.Infra.Win32;
using CutADash.Models;
using CutADash.Repositories;
using CutADash.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Input;
using Preferences;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System;
using Windows.System;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CutADash.Views.ListFrame
{
    /// <summary>
    /// 履歴一覧。J/K移動・Enter/Deleteキー操作・見た目の選択保持などの共通部分は
    /// ListFrameBaseに集約してあり、ここにはSearchBox・全削除・お気に入り追加など
    /// History固有のUIだけを残す。
    /// </summary>
    public sealed partial class ListFrame : ClipboardListFrameBase
    {
        public ListFrame()
        {
            InitializeComponent();

            EmptyText.Text = AppStrings.Get("Empty_Text");

            // ページ自体は(NavigationCacheMode既定のDisabledで)タブを開き直すたびに
            // 新規生成されるが、History タブを開いたまま設定画面でテーマだけ変えた場合は
            // 再ナビゲーションが起きずOnNavigatedToが呼ばれないため、表示中に切り替わる
            // ケースを拾うにはこのイベント購読が別途必要
            Preferences.PreferencesGateway.WindowBackdropChanged += OnWindowBackdropChanged;
            this.Unloaded += (_, _) => Preferences.PreferencesGateway.WindowBackdropChanged -= OnWindowBackdropChanged;
        }

        private void OnWindowBackdropChanged(Common.Models.WindowBackdropKind kind)
            => DispatcherQueue.TryEnqueue(ApplyCatListStyle);

        protected override ListView ItemsListView => ListView;

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            // 通常はMainWindowからDI共有のClipboardListViewModel付きで渡されるが、
            // 万一渡されなかった場合でも画面が空のままにならないようフォールバックする
            if (e.Parameter is ListFrameNavigationParameter param)
            {
                ContentFrame = param.ContentFrame;
                MainWindowRef = param.MainWindow;
                DataContext = param.ViewModel;
            }
            else
            {
                ContentFrame = e.Parameter as Frame;
                DataContext ??= new ClipboardListViewModel(
                    new ClipboardRepository(
                        AppPaths.GetDataFilePath("clipboard_history.db"),
                        AppPaths.GetDataFilePath("ClipboardImages"),
                        (uint)PreferencesGateway.GetThumbnailMaxDimension()));
            }

            ApplyCatListStyle();
        }

        /// <summary>
        /// テーマが「猫」の間だけ、行の区切り線ではなく丸みを帯びたカードを
        /// 余白で並べる見た目(ClipboardTemplateSelectorCat)に切り替える。
        /// タブを開いた時(OnNavigatedTo)と、表示中にテーマが変わった時
        /// (OnWindowBackdropChanged)の両方から呼ぶ。
        /// </summary>
        private void ApplyCatListStyle()
        {
            var isCatTheme = PreferencesGateway.GetWindowBackdrop() == Common.Models.WindowBackdropKind.Cat;
            ListView.ItemTemplateSelector = (Microsoft.UI.Xaml.Controls.DataTemplateSelector)
                Resources[isCatTheme ? "ClipboardTemplateSelectorCat" : "ClipboardTemplateSelector"];
        }

        /// <summary>
        /// 検索欄へフォーカスを移す(MainWindowがCtrl+Fで本物のフォーカスを取った直後に呼ぶ)。
        /// ここまで来ればIMEも効くので、日本語のまま検索できる。
        /// </summary>
        public void FocusSearchBox()
        {
            SearchBox.Focus(FocusState.Keyboard);
            SearchBox.Select(SearchBox.Text.Length, 0);
        }

        // Ctrl+FでSearch欄にフォーカスを移す
        private void SideContent_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            var isCtrlDown = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            if (isCtrlDown && e.Key == VirtualKey.F)
            {
                SearchBox.Focus(FocusState.Programmatic);
                SearchBox.SelectAll();
                e.Handled = true;
            }
        }

        // SearchBox内のテキストをCtrl+Cでコピーしても、それをHistoryに新規項目として
        // 登録してしまわないようにする
        private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            var isCtrlDown = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            if (isCtrlDown && e.Key == VirtualKey.C)
            {
                MainWindowRef?.Provider?.GetService<ClipboardMonitor>()?.SuppressNextChange();
            }
        }

        // {Binding}のTwoWayに任せるとタイミングによって反映が遅れることがあるため、
        // 入力のたびに明示的にSearchTextへ反映する
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox && DataContext is ClipboardListViewModel viewModel)
            {
                viewModel.SearchText = textBox.Text;
            }
        }

        /// <summary>
        /// 一覧の各項目を右クリックした際のメニューから、選択した項目をお気に入りへ追加する。
        /// MenuFlyoutItemのDataContextは、Flyoutを開いたコンテナ(DataTemplateのGrid)から
        /// そのまま引き継がれるため、ClipboardItemとして直接取り出せる。
        /// </summary>
        /// <summary>
        /// 「お気に入りに追加」サブメニューの中身を、開くたびに作り直す。
        /// MenuFlyoutSubItem.Itemsへ実行時に項目を追加しても描画されない
        /// (WinUIの既知の不具合、既存のSubItemを使い回すと再現する)ため、
        /// 表示前に全項目を積み終えた新しいMenuFlyoutSubItemを毎回作って差し替える。
        /// </summary>
        private async void ItemContextMenu_Opening(object sender, object e)
        {
            if (sender is not MenuFlyout flyout || flyout.Target?.DataContext is not ClipboardItem item)
                return;

            var favoriteViewModel = MainWindowRef?.Provider?.GetService<FavoriteListViewModel>();
            if (favoriteViewModel is null)
                return;

            var folders = await favoriteViewModel.GetFolderChoicesAsync();

            var subItem = new MenuFlyoutSubItem
            {
                Text = Common.Utils.AppStrings.Get("ListFrame_AddToFavorite"),
                Icon = new FontIcon { Glyph = "" }
            };

            var rootItem = new MenuFlyoutItem { Text = Common.Utils.AppStrings.Get("ListFrame_FavoriteRoot") };
            rootItem.Click += async (_, _) => await favoriteViewModel.AddAsync(item, null);
            subItem.Items.Add(rootItem);

            foreach (var folder in folders)
            {
                var folderItem = new MenuFlyoutItem { Text = new string('　', folder.Depth) + folder.Name };
                var folderId = folder.Id;
                folderItem.Click += async (_, _) => await favoriteViewModel.AddAsync(item, folderId);
                subItem.Items.Add(folderItem);
            }

            var sequentialPasteQueue = MainWindowRef?.Provider?.GetService<SequentialPaste.SequentialPasteQueueViewModel>();
            var sequentialPasteItem = new MenuFlyoutItem { Text = Common.Utils.AppStrings.Get("ListFrame_AddToSequentialPaste") };
            sequentialPasteItem.Click += (_, _) => sequentialPasteQueue?.Enqueue(item);

            flyout.Items.Clear();
            flyout.Items.Add(subItem);

            // Shape/Imageのみ、ユーザーが名前を付けられる(未設定なら「名称未設定」表示)
            if (item.Type == ClipboardContentType.Image)
            {
                var renameItem = new MenuFlyoutItem { Text = "名前の変更", Icon = new FontIcon { Glyph = "" } };
                renameItem.Click += async (_, _) => await RenameItemAsync(item);
                flyout.Items.Add(renameItem);
            }

            flyout.Items.Add(new MenuFlyoutSeparator());

            var pasteItem = new MenuFlyoutItem { Text = "ペースト", Icon = new FontIcon { Glyph = "" } };
            pasteItem.Click += async (_, _) =>
            {
                SelectedItem = item;
                await PasteSelectedAsync();
            };
            flyout.Items.Add(pasteItem);

            flyout.Items.Add(sequentialPasteItem);

            // Text系項目のみ、改行で分割して1行ずつシーケンシャルペーストのキューに追加する
            // (空行はスキップする)
            if (item.Type == ClipboardContentType.Text)
            {
                var splitItem = new MenuFlyoutItem { Text = "1行ずつシーケンシャルペーストに追加" };
                splitItem.Click += (_, _) => EnqueueLinesToSequentialPaste(sequentialPasteQueue, item);
                flyout.Items.Add(splitItem);
            }
        }

        // itemのTextを改行で分割し、空行を除いた各行を個別のClipboardItemとして
        // シーケンシャルペーストのキューへ積む
        private static void EnqueueLinesToSequentialPaste(SequentialPaste.SequentialPasteQueueViewModel? queue, ClipboardItem item)
        {
            if (queue is null || string.IsNullOrEmpty(item.Text))
                return;

            foreach (var rawLine in item.Text.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                queue.Enqueue(new ClipboardItem
                {
                    Type = ClipboardContentType.Text,
                    Text = line,
                    Timestamp = DateTime.Now,
                    SourceAppName = item.SourceAppName
                });
            }
        }

        // Shape/Image項目の名前を変更する。未入力で確定した場合はnullに戻し、
        // 一覧では「名称未設定」表示(NameOrPlaceholderConverter)になる
        private async System.Threading.Tasks.Task RenameItemAsync(ClipboardItem item)
        {
            if (DataContext is not ClipboardListViewModel viewModel)
                return;

            var nameBox = new TextBox { Text = item.Name ?? string.Empty };
            nameBox.SelectionStart = 0;
            nameBox.SelectionLength = nameBox.Text.Length;

            var dialog = new ContentDialog
            {
                Title = "名前の変更",
                Content = nameBox,
                PrimaryButtonText = "変更",
                CloseButtonText = "キャンセル",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
                await viewModel.RenameItemAsync(item, string.IsNullOrWhiteSpace(nameBox.Text) ? null : nameBox.Text.Trim());
        }

        private async void AllClear_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ClipboardListViewModel viewModel)
                return;

            var dialog = new ContentDialog
            {
                Title = "履歴を全て削除しますか?",
                Content = "この操作は取り消せません。",
                PrimaryButtonText = "削除",
                CloseButtonText = "キャンセル",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await viewModel.ClearAllAsync();

                // ListViewのSelectionChangedに頼らず、Contents側の表示を確実に「なし」へ更新する
                // (0件になったタイミングのGCはClipboardListViewModel側で行う)
                NavigateContent(null);
            }
        }
    }
}
