using CutADash.Models;
using CutADash.Utils;
using CutADash.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Threading.Tasks;

namespace CutADash.Views.ListFrame
{
    /// <summary>
    /// ListFrame(履歴)とFavoriteListFrame(お気に入り)向けの共通処理。
    /// ClipboardItemを扱い、Contentsへのプレビュー表示・Enter/Deleteキーでの
    /// ペースト/削除を行う点で、EmojiListFrameが直接継承するListFrameBaseより
    /// 一段具体的な機能を持つ。
    /// </summary>
    public abstract class ClipboardListFrameBase : ListFrameBase
    {
        // 基底のSelectedItem(object?)を、ClipboardItem専用の型として扱うための隠蔽プロパティ
        protected new ClipboardItem? SelectedItem
        {
            get => base.SelectedItem as ClipboardItem;
            set => base.SelectedItem = value;
        }

        protected async void ItemsListView_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (sender is not ListView listView)
                return;

            if (HandleVimUpDownKey(e.Key, listView))
            {
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Delete)
            {
                if (DataContext is IClipboardItemListViewModel viewModel && listView.SelectedItem is ClipboardItem item)
                {
                    var index = listView.SelectedIndex;
                    await viewModel.DeleteAsync(item);

                    if (listView.Items.Count > 0)
                        listView.SelectedIndex = Math.Min(index, listView.Items.Count - 1);
                    else
                        NavigateContent(null);
                }
                e.Handled = true;
            }
        }

        // ListViewItemが内部でEnterキーを処理して既にHandled済みにしてしまい、
        // 通常のKeyDown(バブリング)まで届かないことがあるため、
        // トンネリングするPreviewKeyDownで先に横取りする
        protected async void ItemsListView_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (sender is not ListView listView)
                return;

            if (e.Key != Windows.System.VirtualKey.Enter)
                return;

            e.Handled = true;

            if (listView.SelectedItem is ClipboardItem item)
                await PasteToPreviousWindowAsync(item);
        }

        // 選択項目をOSクリップボードへ書き戻したうえで、このウィンドウを開く直前に
        // フォアグラウンドだったアプリへ戻し、Ctrl+Vを送ってペーストさせる。
        // 種別(Text/Image/リッチテキスト)を問わず、ペーストした項目は一覧の先頭へ上げる
        private async Task PasteToPreviousWindowAsync(ClipboardItem item)
        {
            await ForegroundPasteHelper.PasteToPreviousWindowAsync(MainWindowRef, item);

            if (DataContext is IClipboardItemListViewModel viewModel)
                await viewModel.BumpToTopAsync(item);
        }

        /// <summary>
        /// 選択中の項目を削除する(MainWindowが低レベルキーフックで受けたDeleteキーから呼ばれる)。
        /// </summary>
        public async Task DeleteSelectedAsync()
        {
            if (DataContext is not IClipboardItemListViewModel viewModel || SelectedItem is null)
                return;

            var listView = ItemsListView;
            var index = listView.SelectedIndex;
            var itemToDelete = SelectedItem;

            await viewModel.DeleteAsync(itemToDelete);

            if (listView.Items.Count > 0)
            {
                var newIndex = Math.Min(index, listView.Items.Count - 1);
                listView.SelectedIndex = newIndex;
                FocusContainerAtIndex(newIndex);
            }
            else
            {
                NavigateContent(null);
            }
        }

        /// <summary>
        /// 選択中の項目をペーストする(同じくキーフックで受けたEnterから呼ばれる)。
        /// 見た目の選択(ItemsListView.SelectedItem)ではなく、記憶している実際の選択を使う。
        /// フォーカスが一覧の外にあり見た目上は選択解除されていても、直前まで選んでいた
        /// 項目をそのままペーストできるようにするため。
        /// </summary>
        public Task PasteSelectedAsync()
        {
            if (SelectedItem is null)
                return Task.CompletedTask;

            return PasteToPreviousWindowAsync(SelectedItem);
        }

        // 選択操作を(確認ダイアログのキャンセル等で)元に戻している間、
        // それ自体がSelectionChangedを再度誘発して確認ループになるのを防ぐガード
        private bool _isRevertingSelection;

        protected async void ItemsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isRevertingSelection)
                return;

            // ClearSelectionIfFocusOutsideによる見た目だけの選択解除(何も追加されていない)では、
            // Contentsのプレビューを巻き込みたくないため、実際に選択が変わった場合だけ反映する
            if (e.AddedItems.Count == 0)
                return;

            if (sender is not ListView listView)
                return;

            var newItem = listView.SelectedItem as ClipboardItem;
            var previousItem = SelectedItem;

            if (!await NavigateContentWithConfirmationAsync(newItem))
            {
                // 未保存の変更があり、移動をキャンセルされた。選択を元に戻す
                _isRevertingSelection = true;
                listView.SelectedItem = previousItem;
                _isRevertingSelection = false;
                return;
            }

            SelectedItem = newItem;
        }

        // 選択のたびにNavigateし直すと、その都度Contentsページ(RichEditBox/Image等)が
        // 新しく作り直され、古いインスタンス(表示していた画像を含む)がGCされるまで
        // メモリに残ってしまう。既にContentsが表示されていればItemを差し替えるだけにする。
        //
        // 確認なしで強制的に切り替える版。削除操作の直後など、そのアイテム自体が
        // 既に無くなった/操作が確定済みで「保存しますか」の確認が無意味な場面で使う。
        protected void NavigateContent(ClipboardItem? item)
        {
            var ownerViewModel = DataContext as IClipboardItemListViewModel;

            if (ContentFrame?.Content is Contents.Contents existingContents)
            {
                existingContents.ForceSetContext(item, ownerViewModel);
                return;
            }

            object? parameter = MainWindowRef is not null
                ? new Contents.ContentsNavigationParameter { Item = item, MainWindow = MainWindowRef, OwnerViewModel = ownerViewModel }
                : item;

            ContentFrame?.NavigateWithoutAnimation(typeof(Contents.Contents), parameter);
        }

        /// <summary>
        /// 一覧内で選択している項目を切り替える時に使う、確認あり版。未保存の変更が
        /// あれば確認ダイアログを出し、キャンセルされたらfalseを返す(呼び出し側は
        /// 選択操作を取り消すこと)。
        /// </summary>
        protected async Task<bool> NavigateContentWithConfirmationAsync(ClipboardItem? item)
        {
            var ownerViewModel = DataContext as IClipboardItemListViewModel;

            if (ContentFrame?.Content is Contents.Contents existingContents)
                return await existingContents.RequestSetContextAsync(item, ownerViewModel);

            object? parameter = MainWindowRef is not null
                ? new Contents.ContentsNavigationParameter { Item = item, MainWindow = MainWindowRef, OwnerViewModel = ownerViewModel }
                : item;

            ContentFrame?.NavigateWithoutAnimation(typeof(Contents.Contents), parameter);
            return true;
        }
    }
}
