using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.Xaml.Interactivity;
using System.Windows.Input;

namespace CutADash.Behaviors
{
    /// <summary>
    /// アタッチした要素のマウスホイールを、ページ送り/ページ戻しのCommand呼び出しへ変換する。
    ///
    /// 「UIイベントをCommand呼び出しへ変換する」役割はコードビハインドではなくBehaviorへ
    /// 寄せる、というMVVMの定石に沿った実装(EmojiGridViewModel側にはページ送りの実処理
    /// (GoToNextPage/GoToPreviousPageCommand)だけを持たせ、入力の解釈はここに閉じ込める)。
    /// </summary>
    public sealed class MouseWheelPagingBehavior : Behavior<UIElement>
    {
        public static readonly DependencyProperty NextPageCommandProperty =
            DependencyProperty.Register(nameof(NextPageCommand), typeof(ICommand), typeof(MouseWheelPagingBehavior), new PropertyMetadata(null));

        public ICommand? NextPageCommand
        {
            get => (ICommand?)GetValue(NextPageCommandProperty);
            set => SetValue(NextPageCommandProperty, value);
        }

        public static readonly DependencyProperty PreviousPageCommandProperty =
            DependencyProperty.Register(nameof(PreviousPageCommand), typeof(ICommand), typeof(MouseWheelPagingBehavior), new PropertyMetadata(null));

        public ICommand? PreviousPageCommand
        {
            get => (ICommand?)GetValue(PreviousPageCommandProperty);
            set => SetValue(PreviousPageCommandProperty, value);
        }

        protected override void OnAttached()
        {
            base.OnAttached();
            AssociatedObject.PointerWheelChanged += OnPointerWheelChanged;
        }

        protected override void OnDetaching()
        {
            AssociatedObject.PointerWheelChanged -= OnPointerWheelChanged;
            base.OnDetaching();
        }

        // 上(奥)へ回すと前のページ、下(手前)へ回すと次のページへ進む(一般的なスクロール方向と合わせる)
        private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var delta = e.GetCurrentPoint(AssociatedObject).Properties.MouseWheelDelta;
            if (delta == 0)
                return;

            var command = delta > 0 ? PreviousPageCommand : NextPageCommand;
            if (command?.CanExecute(null) == true)
            {
                command.Execute(null);

                // ページを切り替えるとItemsRepeaterのコンテナがリサイクルされ、直前まで
                // キーボードフォーカスを持っていたセルの「位置」に、中身だけ別の絵文字が
                // 差し替わって表示される。マウスは動いていないのにフォーカス枠だけが
                // 新しい絵文字の上に乗って見え、「選択が勝手に変わった」ように見えてしまう
                // (実際に踏んだ不具合)。ホイール操作はポインター操作なので、フォーカスは
                // どのセルにも残さず、アタッチ先(枠を持たない領域)へ逃がしておく
                if (AssociatedObject.IsTabStop)
                    AssociatedObject.Focus(FocusState.Programmatic);
            }

            e.Handled = true;
        }
    }
}
