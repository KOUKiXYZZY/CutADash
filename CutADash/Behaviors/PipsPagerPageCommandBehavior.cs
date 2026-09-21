using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Xaml.Interactivity;
using System.Windows.Input;

namespace CutADash.Behaviors
{
    /// <summary>
    /// PipsPagerのSelectedIndexChangedを、ページ切り替えのCommand呼び出しへ変換する。
    ///
    /// SelectedPageIndex自体はXAML側で ViewModel.PageIndex へ(表示のためだけに)
    /// OneWayバインドしているが、PipsPagerはユーザー操作だけでなくプログラムからの
    /// 値変更でもSelectedIndexChangedを発火するため、バインディングによる反映(VM→View)を
    /// ユーザー操作と区別する必要がある。CurrentIndex(ViewModel.PageIndexと同じ値を
    /// OneWayバインドしておく)と実際のSelectedPageIndexを比較し、一致していれば
    /// バインディングの反映によるものとみなして何もしない。
    /// </summary>
    public sealed class PipsPagerPageCommandBehavior : Behavior<PipsPager>
    {
        public static readonly DependencyProperty CommandProperty =
            DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(PipsPagerPageCommandBehavior), new PropertyMetadata(null));

        public ICommand? Command
        {
            get => (ICommand?)GetValue(CommandProperty);
            set => SetValue(CommandProperty, value);
        }

        public static readonly DependencyProperty CurrentIndexProperty =
            DependencyProperty.Register(nameof(CurrentIndex), typeof(int), typeof(PipsPagerPageCommandBehavior), new PropertyMetadata(0));

        public int CurrentIndex
        {
            get => (int)GetValue(CurrentIndexProperty);
            set => SetValue(CurrentIndexProperty, value);
        }

        protected override void OnAttached()
        {
            base.OnAttached();
            AssociatedObject.SelectedIndexChanged += OnSelectedIndexChanged;
        }

        protected override void OnDetaching()
        {
            AssociatedObject.SelectedIndexChanged -= OnSelectedIndexChanged;
            base.OnDetaching();
        }

        private void OnSelectedIndexChanged(PipsPager sender, PipsPagerSelectedIndexChangedEventArgs args)
        {
            if (sender.SelectedPageIndex == CurrentIndex)
                return; // ViewModel側の変更がバインディングで反映されただけ

            if (Command?.CanExecute(sender.SelectedPageIndex) == true)
                Command.Execute(sender.SelectedPageIndex);
        }
    }
}
