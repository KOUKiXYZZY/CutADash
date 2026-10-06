using CutADash.Models;
using CutADash.Utils;
using CutADash.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
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
                    if (!await ConfirmDeleteAsync(item))
                    {
                        e.Handled = true;
                        return;
                    }

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
            MarkKeyboardNavigation();

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
        //
        // 設定「選択時にペーストしない」がオンの間は、貼り付けずクリップボードへ移すだけにする
        // (forcePasteがtrueなら、設定に関わらず貼り付ける。右クリックメニューの「ペースト」用)
        private async Task PasteToPreviousWindowAsync(ClipboardItem item, bool forcePaste = false)
        {
            var paste = forcePaste || !Preferences.PreferencesGateway.IsCopyOnlyOnSelect();
            await ForegroundPasteHelper.PasteToPreviousWindowAsync(MainWindowRef, item, paste: paste);

            // 貼り付けない場合(クリップボードへ移すだけ)、ヘルパーはウィンドウを開いたままにするが、
            // この設定では移し終えたらウィンドウを閉じ、元のウィンドウへフォーカスを戻す
            // (貼り付けた場合は、ヘルパーが閉じて貼り付け先へ戻す)
            if (!paste)
            {
                MainWindowRef?.HidePalette();
                await ForegroundPasteHelper.ActivatePreviousWindowAsync(MainWindowRef);
            }

            if (DataContext is IClipboardItemListViewModel viewModel)
                await viewModel.BumpToTopAsync(item);
        }

        /// <summary>削除してよいか確認する。既定は確認なし(履歴)。お気に入りは確認ダイアログを出す。</summary>
        protected virtual Task<bool> ConfirmDeleteAsync(ClipboardItem item) => Task.FromResult(true);

        /// <summary>
        /// 選択中の項目を削除する(MainWindowが低レベルキーフックで受けたDeleteキーから呼ばれる)。
        /// </summary>
        public async Task DeleteSelectedAsync()
        {
            if (DataContext is not IClipboardItemListViewModel viewModel || SelectedItem is null)
                return;

            if (!await ConfirmDeleteAsync(SelectedItem))
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
        public Task PasteSelectedAsync(bool forcePaste = false)
        {
            if (SelectedItem is null)
                return Task.CompletedTask;

            return PasteToPreviousWindowAsync(SelectedItem, forcePaste);
        }

        /// <summary>
        /// 項目をクリックした時の処理。Enterと同じく、その項目を直前のウィンドウへ貼り付ける
        /// (設定「選択時にペーストしない」がオンの間は、貼り付けず、クリップボードへ移して
        /// 元のウィンドウへフォーカスを戻す。PasteToPreviousWindowAsync参照)。
        /// </summary>
        protected Task PasteItemOnClickAsync(ClipboardItem item)
            => PasteToPreviousWindowAsync(item);

        protected async void ItemsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is ClipboardItem item)
                await PasteItemOnClickAsync(item);
        }

        // ---- マウスオーバーで、即座に選択する ----
        // 重なっている項目を選択する(選択の変更に連動して、Contentsにも表示される)

        private ClipboardItem? _hoverItem;

        // キーボードで一覧を操作している間は、マウスオーバーでの選択を止める。
        // キー操作で一覧がスクロールすると、動かしていないマウスの下の項目が変わり、見かけ上の
        // ポインター移動(位置が同じままのPointerMoved)が発生して、選択を奪い返してしまうため、
        // 次の2つで見分ける。
        //  - 位置が前回と同じPointerMoved: 見かけ上の移動なので無視する
        //  - キー操作の後は、マウスが実際に少し動くまで無効にする
        private const double HoverResumeDistanceDip = 4;
        private Windows.Foundation.Point? _lastPointerPosition;
        private Windows.Foundation.Point? _pointerPositionAtKeyboard;
        private bool _keyboardNavigating;

        /// <summary>一覧をキーボードで操作し始めたことを記録する(マウスオーバーでの選択を止める)。</summary>
        protected void MarkKeyboardNavigation()
        {
            _keyboardNavigating = true;
            _pointerPositionAtKeyboard = _lastPointerPosition;
            _hoverItem = null;
            MainWindowRef?.HideContentsPopup();
        }

        protected void ItemsList_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            var position = e.GetCurrentPoint(null).Position;
            if (_lastPointerPosition is { } last && last.X == position.X && last.Y == position.Y)
                return;
            _lastPointerPosition = position;

            if (_keyboardNavigating)
            {
                if (_pointerPositionAtKeyboard is { } origin
                    && Math.Abs(position.X - origin.X) < HoverResumeDistanceDip
                    && Math.Abs(position.Y - origin.Y) < HoverResumeDistanceDip)
                {
                    return;
                }

                _keyboardNavigating = false;
            }

            var item = FindItemUnderPointer(e.OriginalSource as DependencyObject);
            if (ReferenceEquals(item, _hoverItem))
                return;

            _hoverItem = item;
            if (item is null || IsDetached)
            {
                MainWindowRef?.HideContentsPopup();
                return;
            }

            SelectItemOnHover(item);

            // コンパクト表示(Contentsを置く場所が無い)の時は、ポップアップで見せる
            MainWindowRef?.ShowContentsPopup(item);
        }

        /// <summary>マウスオーバーされた項目を選択する。ツリー等、一覧の形が違う場合は上書きする。</summary>
        protected virtual void SelectItemOnHover(ClipboardItem item)
        {
            if (!ReferenceEquals(ItemsListView.SelectedItem, item))
                ItemsListView.SelectedItem = item;
        }

        protected void ItemsList_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            _hoverItem = null;
            MainWindowRef?.HideContentsPopup();
        }

        // ポインターの下の要素から親をたどり、項目(ClipboardItem、またはお気に入りのアイテム)を探す。
        // 項目のテンプレート内の要素はDataContextとして項目を引き継ぐため、最初に見つかったものが対象。
        // フォルダや、項目の外(余白・スクロールバー)ではnullを返す
        private static ClipboardItem? FindItemUnderPointer(DependencyObject? element)
        {
            while (element is not null)
            {
                if (element is FrameworkElement { DataContext: var context })
                {
                    switch (context)
                    {
                        case ClipboardItem item:
                            return item;
                        case FavoriteNode { IsFolder: false, Item: { } favoriteItem }:
                            return favoriteItem;
                    }
                }

                element = VisualTreeHelper.GetParent(element);
            }

            return null;
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
            // このページが既にFrameから追い出された後なら、全タブ共通のContentFrameには
            // 触らない(IsDetached参照)
            if (IsDetached)
                return;

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
            // このページが既にFrameから追い出された後なら、全タブ共通のContentFrameには
            // 触らない(IsDetached参照)。trueを返す(=選択の変更自体は妨げない)ことで、
            // 呼び出し元が「キャンセルされた」と誤解して選択を戻そうとしないようにする
            if (IsDetached)
                return true;

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
