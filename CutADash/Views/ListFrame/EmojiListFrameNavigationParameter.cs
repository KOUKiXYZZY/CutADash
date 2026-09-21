using Microsoft.UI.Xaml.Controls;

namespace CutADash.Views.ListFrame
{
    /// <summary>
    /// MainWindowからEmojiListFrameへNavigateする際に渡すパラメータ。
    /// カテゴリ選択後にEmojiページへペースト先を伝えられるよう、MainWindowも合わせて渡す。
    /// </summary>
    public sealed class EmojiListFrameNavigationParameter
    {
        public required Frame ContentFrame { get; init; }
        public required MainWindow MainWindow { get; init; }
    }
}
