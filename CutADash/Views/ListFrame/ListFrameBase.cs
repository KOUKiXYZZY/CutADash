using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;

namespace CutADash.Views.ListFrame
{
    /// <summary>
    /// ListFrame(履歴)・FavoriteListFrame(お気に入り)・EmojiListFrame(絵文字カテゴリ)は、
    /// どれも「1つのListViewをJ/K移動・見た目の選択保持で扱う」という同じ作りのため、
    /// その共通部分をここへ集約する。
    ///
    /// 選択項目の型は一覧ごとに異なる(ClipboardItem/EmojiCategoryItem)ため、
    /// SelectedItemはobjectで持つ。ClipboardItemを扱うListFrame/FavoriteListFrameは、
    /// Contentsへのプレビュー表示・Enter/Deleteキーでのペースト/削除も必要なため、
    /// <see cref="ClipboardListFrameBase"/>を継承する。
    ///
    /// </summary>
    public abstract class ListFrameBase : Page
    {
        // ContentFrame.Navigate(typeof(ListFrame), contentFrame) で渡される、
        // 選択項目の表示先となるFrame
        protected Frame? ContentFrame;

        // Enterでのペースト先(直前のフォアグラウンドウィンドウ)を得るためのMainWindow参照
        protected MainWindow? MainWindowRef;

        // ClearSelectionIfFocusOutsideで見た目の選択(ItemsListView.SelectedItem)だけを
        // 一時的に外しても、Contentsのプレビューやペースト対象は変えたくないため、
        // 実際の選択はここに保持しておく。型は一覧ごとに異なるためobjectで持つ
        // (ClipboardListFrameBaseは型付きのSelectedItemプロパティで隠蔽して使う)
        protected object? SelectedItem;

        /// <summary>派生クラスのXAMLでx:Name指定されたListViewそのもの。</summary>
        protected abstract ListView ItemsListView { get; }

        /// <summary>
        /// ListView自身が本物のキーボードフォーカスを持っている場合
        /// J(下)/K(上)によるvim風の選択を移動する
        /// </summary>
        protected static bool HandleVimUpDownKey(Windows.System.VirtualKey key, ListView listView)
        {
            if (key == Windows.System.VirtualKey.J)
            {
                if (listView.SelectedIndex < listView.Items.Count - 1)
                {
                    listView.SelectedIndex++;
                    listView.ScrollIntoView(listView.SelectedItem);
                }
                return true;
            }

            if (key == Windows.System.VirtualKey.K)
            {
                if (listView.SelectedIndex > 0)
                {
                    listView.SelectedIndex--;
                    listView.ScrollIntoView(listView.SelectedItem);
                }
                return true;
            }

            return false;
        }

        /// <summary>
        /// 選択を前後へ動かす(MainWindowが低レベルキーフックで受けた上下キーから呼ばれる)。
        /// MainWindowはWS_EX_NOACTIVATEでフォーカスを持たないため、
        /// ListView自身のキー処理は使えない。
        /// </summary>
        public virtual void MoveSelection(int delta)
        {
            var listView = ItemsListView;
            if (listView.Items.Count == 0)
                return;

            // 未選択なら先頭から始める
            var current = listView.SelectedIndex < 0 ? 0 : listView.SelectedIndex + delta;
            var index = Math.Clamp(current, 0, listView.Items.Count - 1);

            listView.SelectedIndex = index;
            listView.ScrollIntoView(listView.SelectedItem);
            FocusContainerAtIndex(index);
        }

        // 選択を変えるだけではキーボードフォーカスは追従しないため、
        // 選択した項目のコンテナへ明示的にフォーカスを移す。
        // ListViewItemの既定テンプレートはFocusState.Keyboardのときだけ
        // フォーカス枠を表示するため、Keyboardを指定する(SelectItemAtIndexと同じ理由)
        protected void FocusContainerAtIndex(int index)
        {
            var listView = ItemsListView;
            listView.UpdateLayout();
            if (listView.ContainerFromIndex(index) is Control container)
                container.Focus(FocusState.Keyboard);
            else
                listView.Focus(FocusState.Keyboard);
        }

        /// <summary>
        /// 指定した要素が一覧(ItemsListView)の中にあるかどうかを判定する。
        /// MainWindowがEnterキーの宛先(一覧のペースト/フォーカス中コントロールのInvoke)を
        /// 振り分けるために使う。
        /// </summary>
        public virtual bool IsWithinList(DependencyObject? element)
        {
            var listView = ItemsListView;
            while (element is not null)
            {
                if (ReferenceEquals(element, listView))
                    return true;

                element = VisualTreeHelper.GetParent(element);
            }

            return false;
        }

        /// <summary>
        /// フォーカスが一覧の外へ移っても選択は解除しない
        /// (MainWindowがFocusManager.GotFocusを監視して呼ぶ)。
        /// フォーカスが他所へ移るたびに選択が失われると、表示中の内容と見た目上の
        /// 選択が食い違って紛らわしいため、あえて何もしない。
        /// </summary>
        public void ClearSelectionIfFocusOutside(DependencyObject? focused)
        {
            //if (!IsWithinList(focused))
            //    ItemsListView.SelectedItem = null;
        }

        // ListViewにフォーカスが移った時点で見た目上の選択が外れていれば、
        // 記憶している実際の選択を復元する(見つからない場合は先頭を選択状態にする)
        protected void ItemsListView_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not ListView listView || listView.SelectedItem is not null)
                return;

            if (SelectedItem is not null && listView.Items.Contains(SelectedItem))
                listView.SelectedItem = SelectedItem;
            else if (listView.Items.Count > 0)
                listView.SelectedIndex = 0;
        }

        /// <summary>
        /// ショートカットキーで一覧を開いた直後などに、指定インデックスの項目を選択状態にし、
        /// キーボードフォーカスもその項目へ移す(J/K等ですぐ操作を続けられるようにする)。
        /// 範囲外のインデックス(項目数が足りない等)なら何もしない。
        /// </summary>
        public void SelectItemAtIndex(int index)
        {
            var listView = ItemsListView;
            if (index < 0 || index >= listView.Items.Count)
                return;

            listView.SelectedIndex = index;
            FocusContainerAtIndex(index);
        }
    }
}
