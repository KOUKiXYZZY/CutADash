using CutADash.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Xaml.Interactivity;
using System.Windows.Input;

namespace CutADash.Behaviors
{
    /// <summary>
    /// 絵文字セル(Border)のクリック/Enter/Spaceを、ペーストのCommand呼び出しへ変換する。
    ///
    /// MouseWheelPagingBehaviorと同じく「UIイベントをCommand呼び出しへ変換する」役割は
    /// コードビハインドではなくBehaviorへ寄せる、というMVVMの定石に沿った実装
    /// (EmojiGridViewModel側にはペーストの実処理(PasteCommand)だけを持たせ、
    /// 入力の解釈はここに閉じ込める)。矢印キーでのセル間フォーカス移動や、
    /// 右クリックでのバリエーションFlyout表示は、ItemsRepeater全体の状態や
    /// スプライト画像の切り出しといったView側の描画・レイアウト固有の関心事のため、
    /// 従来通りPage(Emoji.xaml.cs)側に残す。
    /// </summary>
    public sealed class EmojiCellPasteBehavior : Behavior<Border>
    {
        public static readonly DependencyProperty PasteCommandProperty =
            DependencyProperty.Register(nameof(PasteCommand), typeof(ICommand), typeof(EmojiCellPasteBehavior), new PropertyMetadata(null));

        public ICommand? PasteCommand
        {
            get => (ICommand?)GetValue(PasteCommandProperty);
            set => SetValue(PasteCommandProperty, value);
        }

        protected override void OnAttached()
        {
            base.OnAttached();
            AssociatedObject.Tapped += OnTapped;
            AssociatedObject.KeyDown += OnKeyDown;
        }

        protected override void OnDetaching()
        {
            AssociatedObject.Tapped -= OnTapped;
            AssociatedObject.KeyDown -= OnKeyDown;
            base.OnDetaching();
        }

        private void OnTapped(object sender, TappedRoutedEventArgs e) => TryExecute();

        private void OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key is Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space)
            {
                e.Handled = true;
                TryExecute();
            }
        }

        private void TryExecute()
        {
            if (AssociatedObject.DataContext is not EmojiItem item)
                return;

            if (PasteCommand?.CanExecute(item) == true)
                PasteCommand.Execute(item);
        }
    }
}
