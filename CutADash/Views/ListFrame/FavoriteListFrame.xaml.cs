using Common.Utils;
using CutADash.Infra.Win32;
using CutADash.ViewModels;
using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace CutADash.Views.ListFrame
{
    /// <summary>
    /// Favoriteタブ用。フォルダ階層はTreeView(FolderTree)で表示・操作し、
    /// 検索中だけ従来通りのフラットなListView(BaseExample)に切り替える。
    ///
    /// J/K移動・グローバルキーフック経由のIsWithinList判定(ClipboardListFrameBase/
    /// ListFrameBase)は、検索中はItemsListView(=BaseExample)、通常時はFolderTreeを
    /// 対象にするようMoveSelection/IsWithinListをオーバーライドして切り替える。
    /// </summary>
    public sealed partial class FavoriteListFrame : ClipboardListFrameBase
    {
        private FavoriteListViewModel? _viewModel;

        public FavoriteListFrame()
        {
            InitializeComponent();

            EmptyText.Text = AppStrings.Get("Empty_Text");
        }

        protected override ListView ItemsListView => BaseExample;

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is FavoriteListFrameNavigationParameter param)
            {
                ContentFrame = param.ContentFrame;
                MainWindowRef = param.MainWindow;
                DataContext = param.ViewModel;
                AttachViewModel(param.ViewModel);
            }
            else
            {
                ContentFrame = e.Parameter as Frame;
            }
        }

        // FavoriteListViewModelはDIシングルトンでアプリと同じ寿命のため、
        // 購読解除はしない(他のシングルトンViewModelの既存パターンと同じ)
        private void AttachViewModel(FavoriteListViewModel viewModel)
        {
            if (_viewModel is not null)
                return;

            _viewModel = viewModel;
            _viewModel.TreeLoaded += RestoreSelectedFolder;

            // AttachViewModel時点で既に読み込み済み(2回目以降にこのタブへ来た時)なら、
            // TreeLoadedを待たずにここで復元する
            if (_viewModel.RootNodes.Count > 0)
                RestoreSelectedFolder();
        }

        // 最後に選択していたフォルダを選び直す。ツリー再読み込みのたびに呼ばれるが、
        // 既に何か選択済みなら(ユーザーの操作を上書きしないよう)何もしない
        private async void RestoreSelectedFolder()
        {
            if (_viewModel is null || FolderTree.SelectedNode is not null)
                return;

            var folderId = await _viewModel.GetSelectedFolderIdAsync();
            if (folderId is not int id)
                return;

            var node = FindTreeViewNode(FolderTree, id);
            if (node?.Content is FavoriteNode { IsFolder: true })
            {
                _suppressTreeSelectionChanged = true;
                FolderTree.SelectedNode = node;
                _suppressTreeSelectionChanged = false;

                FolderTree.UpdateLayout();
                if (FolderTree.ContainerFromNode(node) is Control container)
                    container.StartBringIntoView();
            }
        }

        /// <summary>
        /// 検索欄へフォーカスを移す(MainWindowがCtrl+Fで本物のフォーカスを取った直後に呼ぶ)。
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
        // 入力のたびに明示的にSearchTextへ反映する。あわせて、検索中はフォルダを無視した
        // フラット一覧(BaseExample)を、検索していない時はツリー(FolderTree)を表示する
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox textBox || _viewModel is null)
                return;

            _viewModel.SearchText = textBox.Text;

            var searching = !string.IsNullOrWhiteSpace(textBox.Text);
            FolderTree.Visibility = searching ? Visibility.Collapsed : Visibility.Visible;
            BaseExample.Visibility = searching ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// 「+」ボタン。ダイアログは出さず、選択中フォルダ(無ければルート)の子として
        /// 即座に「新しいフォルダ」を作る。名前は後で右クリック→名前の変更で変える。
        /// </summary>
        private async void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel is null)
                return;

            var parentId = GetSelectedFolderIdForNewChild();
            await _viewModel.CreateFolderCommand.ExecuteAsync(parentId);
        }

        // 新規フォルダ/追加先として使う親フォルダIdを、現在の選択状態から決める。
        // フォルダを選択中ならそのフォルダの子、アイテムを選択中ならその親、
        // 何も選んでいなければルート直下にする
        private int? GetSelectedFolderIdForNewChild()
        {
            if (FolderTree.SelectedNode?.Content is not FavoriteNode selected)
                return null;

            return selected.IsFolder ? selected.Id : selected.ParentId;
        }

        private async void RenameFolder_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel is null || sender is not MenuFlyoutItem { DataContext: FavoriteNode node } || !node.IsFolder)
                return;

            var nameBox = new TextBox { Text = node.Name, SelectionStart = 0, SelectionLength = node.Name.Length };
            var dialog = new ContentDialog
            {
                Title = "フォルダ名を変更",
                Content = nameBox,
                PrimaryButtonText = "変更",
                CloseButtonText = "キャンセル",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(nameBox.Text))
                await _viewModel.RenameFolderAsync(node.Id, nameBox.Text.Trim());
        }

        // Shape/Image項目の名前を変更する。未入力で確定した場合はnullに戻し、
        // 一覧では「名称未設定」表示(NameOrPlaceholderConverter)になる
        private async void RenameItem_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel is null || sender is not MenuFlyoutItem { DataContext: FavoriteNode { IsImageItem: true } node })
                return;

            var nameBox = new TextBox { Text = node.Item?.Name ?? string.Empty };
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
                await _viewModel.RenameItemAsync(node.Id, string.IsNullOrWhiteSpace(nameBox.Text) ? null : nameBox.Text.Trim());
        }

        // 右クリックメニューの「ペースト」。右クリックはTreeViewの選択と連動しないことがあるため、
        // DataContextのアイテムを直接直前のウィンドウへ貼り付ける(PasteSelectedAsyncが見る
        // SelectedItemには依存しない)
        private async void PasteItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { DataContext: FavoriteNode { IsItem: true, Item: not null } node })
                return;

            SelectedItem = node.Item;
            await PasteSelectedAsync();
        }

        private async void DeleteNode_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel is null || sender is not MenuFlyoutItem { DataContext: FavoriteNode node })
                return;

            await DeleteNodeAsync(node);
        }

        private void AddToSequentialPaste_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { DataContext: FavoriteNode node } || _viewModel is null)
                return;

            _viewModel.AddToSequentialPasteCommand.Execute(node);
        }

        private void AddLinesToSequentialPaste_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { DataContext: FavoriteNode node } || _viewModel is null)
                return;

            _viewModel.AddLinesToSequentialPasteCommand.Execute(node);
        }

        // 選択を強制的に変更している間、それ自体がSelectionChangedを再度誘発して
        // 確認ダイアログのループや二重処理になるのを防ぐガード
        // (未保存確認のキャンセルで元に戻す時と、削除後に次の選択を設定する時の両方で使う)
        private bool _suppressTreeSelectionChanged;

        /// <summary>
        /// フォルダ/アイテムを削除する。削除後もフォーカスをTreeView内に留めるため、
        /// 削除前の兄弟内での位置を覚えておき、削除後に「下(繰り上がって同じ位置に来た
        /// ノード)」を選ぶ。下が無ければ「上(1つ前)」を選ぶ。兄弟が1つも残らなければ
        /// (その階層が空になったら)選択・フォーカスをTreeViewの外(検索欄)へ移す。
        /// </summary>
        private async System.Threading.Tasks.Task DeleteNodeAsync(FavoriteNode node)
        {
            if (_viewModel is null)
                return;

            var siblingsBefore = _viewModel.GetSiblings(node.ParentId);
            var deletedIndex = IndexOfById(siblingsBefore, node.Id);

            await _viewModel.DeleteNodeAsync(node.Id);

            var siblingsAfter = _viewModel.GetSiblings(node.ParentId);
            var nextSelection = siblingsAfter.Count > 0
                ? siblingsAfter[Math.Min(deletedIndex, siblingsAfter.Count - 1)]
                : null;

            _suppressTreeSelectionChanged = true;
            try
            {
                if (nextSelection is not null)
                {
                    var treeNode = FindTreeViewNode(FolderTree, nextSelection.Id);
                    FolderTree.SelectedNode = treeNode;

                    if (treeNode is not null)
                    {
                        FolderTree.UpdateLayout();
                        if (FolderTree.ContainerFromNode(treeNode) is Control container)
                            container.Focus(FocusState.Keyboard);
                    }

                    SelectedItem = nextSelection.IsFolder ? null : nextSelection.Item;
                    NavigateContent(nextSelection.IsFolder ? null : nextSelection.Item);
                }
                else
                {
                    // この階層に何も残らなかった。TreeViewの外へフォーカスを移す
                    FolderTree.SelectedNode = null;
                    SelectedItem = null;
                    NavigateContent(null);
                    SearchBox.Focus(FocusState.Programmatic);
                }
            }
            finally
            {
                _suppressTreeSelectionChanged = false;
            }
        }

        private static int IndexOfById(IReadOnlyList<FavoriteNode> nodes, int id)
        {
            for (var i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Id == id)
                    return i;
            }
            return 0;
        }

        // ツリーの選択が変わるたびに、アイテムならContentsへプレビュー表示する
        // (フォルダを選んだ時はプレビューを消す)。未保存の変更があれば確認し、
        // キャンセルされたら選択を元に戻す。あわせて、グローバルキーフック経由の
        // Enter/Paste(ClipboardListFrameBase.PasteSelectedAsync)がツリー選択中の
        // アイテムも拾えるよう、隠し持っているSelectedItemも更新しておく
        private async void FolderTree_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
        {
            if (_suppressTreeSelectionChanged)
                return;

            var selectedNode = sender.SelectedNode?.Content as FavoriteNode;
            var newItem = selectedNode is { IsFolder: false, Item: not null } ? selectedNode.Item : null;

            if (!await NavigateContentWithConfirmationAsync(newItem))
            {
                // 未保存の変更があり、移動をキャンセルされた。選択を元に戻す
                var previousNode = args.RemovedItems.Count > 0 ? args.RemovedItems[0] as FavoriteNode : null;
                var previousTreeNode = previousNode is not null ? FindTreeViewNode(sender, previousNode.Id) : null;

                _suppressTreeSelectionChanged = true;
                sender.SelectedNode = previousTreeNode;
                _suppressTreeSelectionChanged = false;
                return;
            }

            SelectedItem = newItem;

            // フォルダを選んだ時だけ、次回の選択復元用に記録する。アイテムを選んだ場合は
            // 「今見ているフォルダ」を変えないので、直前に選んでいたフォルダのままにしておく
            if (selectedNode is { IsFolder: true } && _viewModel is not null)
                await _viewModel.SetSelectedFolderIdAsync(selectedNode.Id);
        }

        // TreeViewItemが内部でEnterキーを処理して既にHandled済みにしてしまい、通常の
        // KeyDown(バブリング)まで届かないことがあるため、ItemsListView_PreviewKeyDown
        // (ClipboardListFrameBase)と同じくトンネリングするPreviewKeyDownで先に横取りする
        private async void FolderTree_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Enter)
                return;

            // フォルダを選んでいる時は開閉(既定のTreeView動作)に任せ、ペーストはしない
            if (FolderTree.SelectedNode?.Content is not FavoriteNode { IsFolder: false })
                return;

            e.Handled = true;
            await PasteSelectedAsync();
        }

        // Delete削除に加え、J/K/H/Lをvim風の上下/親子移動(矢印キーの代わり)として扱う
        private async void FolderTree_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Delete)
            {
                if (FolderTree.SelectedNode?.Content is not FavoriteNode node)
                    return;

                e.Handled = true;
                await DeleteNodeAsync(node);
                return;
            }

            switch (e.Key)
            {
                case VirtualKey.J:
                    MoveTreeSelection(1);
                    e.Handled = true;
                    break;
                case VirtualKey.K:
                    MoveTreeSelection(-1);
                    e.Handled = true;
                    break;
                case VirtualKey.L:
                    MoveTreeSelectionToChild();
                    e.Handled = true;
                    break;
                case VirtualKey.H:
                    MoveTreeSelectionToParent();
                    e.Handled = true;
                    break;
            }
        }

        // J/K: DFS順(表示順)で1つ後ろ/前のノードへ選択を移す(すべて常時展開されているため、
        // 表示されている全ノードが対象になる)
        private void MoveTreeSelection(int delta)
        {
            var flatNodes = FlattenTreeNodes(FolderTree.RootNodes);
            if (flatNodes.Count == 0)
                return;

            var currentIndex = FolderTree.SelectedNode is TreeViewNode selected
                ? flatNodes.IndexOf(selected)
                : -1;
            var newIndex = Math.Clamp(currentIndex < 0 ? 0 : currentIndex + delta, 0, flatNodes.Count - 1);

            FocusTreeNode(flatNodes[newIndex]);
        }

        // L: 選択中のフォルダに子があれば、その最初の子へ選択を移す(矢印キーの右相当)
        private void MoveTreeSelectionToChild()
        {
            if (FolderTree.SelectedNode is not TreeViewNode selected || selected.Children.Count == 0)
                return;

            FocusTreeNode(selected.Children[0]);
        }

        // H: 選択中のノードに親があれば、その親へ選択を移す(矢印キーの左相当)
        private void MoveTreeSelectionToParent()
        {
            if (FolderTree.SelectedNode is not TreeViewNode selected)
                return;

            var parent = GetParentTreeViewNode(FolderTree, selected);
            if (parent is not null)
                FocusTreeNode(parent);
        }

        private void FocusTreeNode(TreeViewNode node)
        {
            FolderTree.SelectedNode = node;
            FolderTree.UpdateLayout();

            if (FolderTree.ContainerFromNode(node) is Control container)
            {
                container.StartBringIntoView();
                container.Focus(FocusState.Keyboard);
            }
        }

        /// <summary>
        /// ドラッグ&amp;ドロップで並び替え/フォルダ間移動が完了した後、実際の位置を
        /// DBへ反映する。WinUIのTreeViewが見た目上のTreeViewNodeの並びは既に
        /// 変更済みなので、ここではその結果を読み取って永続化するだけでよい。
        /// </summary>
        private async void FolderTree_DragItemsCompleted(TreeView sender, TreeViewDragItemsCompletedEventArgs args)
        {
            if (_viewModel is null)
                return;

            foreach (var movedContent in args.Items)
            {
                if (movedContent is not FavoriteNode movedNode)
                    continue;

                var movedTreeNode = FindTreeViewNode(sender, movedNode.Id);
                if (movedTreeNode is null)
                    continue;

                var parentNode = GetParentTreeViewNode(sender, movedTreeNode);
                var siblings = parentNode?.Children ?? sender.RootNodes;
                var newIndex = siblings.IndexOf(movedTreeNode);
                var newParentId = (parentNode?.Content as FavoriteNode)?.Id;

                if (newIndex < 0)
                    continue;

                try
                {
                    await _viewModel.MoveNodeAsync(movedNode.Id, newParentId, newIndex);
                }
                catch (InvalidOperationException)
                {
                    // フォルダを自分の子孫の中へ移動しようとした等の不正な移動。
                    // TreeViewは既に見た目上ドラッグ結果を反映してしまっているため、
                    // DBの状態から読み直して見た目上の移動を取り消す
                    await _viewModel.ReloadAsync();
                }
            }
        }

        private static TreeViewNode? FindTreeViewNode(TreeView tree, int id)
        {
            TreeViewNode? Search(System.Collections.Generic.IList<TreeViewNode> nodes)
            {
                foreach (var node in nodes)
                {
                    if (node.Content is FavoriteNode n && n.Id == id)
                        return node;

                    if (Search(node.Children) is TreeViewNode found)
                        return found;
                }
                return null;
            }

            return Search(tree.RootNodes);
        }

        private static TreeViewNode? GetParentTreeViewNode(TreeView tree, TreeViewNode target)
        {
            TreeViewNode? Search(TreeViewNode? parent, System.Collections.Generic.IList<TreeViewNode> nodes)
            {
                foreach (var node in nodes)
                {
                    if (ReferenceEquals(node, target))
                        return parent;

                    if (Search(node, node.Children) is TreeViewNode found)
                        return found;
                }
                return null;
            }

            foreach (var root in tree.RootNodes)
            {
                if (ReferenceEquals(root, target))
                    return null; // ルート直下

                if (Search(root, root.Children) is TreeViewNode found)
                    return found;
            }

            return null; // 見つからなければルート扱い
        }

        /// <summary>
        /// 検索中はBaseExample(基底実装)、通常時はFolderTreeの中かどうかも見るようにする。
        /// グローバルキーフックのJ/K判定・Enterの宛先振り分けが、ツリー表示中も
        /// 正しく「一覧の中」と判定できるようにするため。
        /// </summary>
        public override bool IsWithinList(DependencyObject? element)
        {
            if (base.IsWithinList(element))
                return true;

            var current = element;
            while (current is not null)
            {
                if (ReferenceEquals(current, FolderTree))
                    return true;

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private static System.Collections.Generic.List<TreeViewNode> FlattenTreeNodes(
            System.Collections.Generic.IList<TreeViewNode> nodes)
        {
            var result = new System.Collections.Generic.List<TreeViewNode>();

            void Walk(System.Collections.Generic.IList<TreeViewNode> list)
            {
                foreach (var node in list)
                {
                    result.Add(node);
                    Walk(node.Children); // 常にIsExpanded=Trueのため子も常に表示されている
                }
            }

            Walk(nodes);
            return result;
        }
    }
}
