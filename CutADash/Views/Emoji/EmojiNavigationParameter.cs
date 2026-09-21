namespace CutADash.Views.Emoji
{
    /// <summary>
    /// EmojiListFrameからEmojiページへNavigateする際に渡すパラメータ。
    /// 選択したカテゴリと、絵文字クリック時にペースト先として使うMainWindowを合わせて渡す。
    /// </summary>
    public sealed class EmojiNavigationParameter
    {
        public string? Category { get; init; }

        // Category=nullには「タブを開いた直後(未指定)」と「"全部"を選んだ(全件表示)」の
        // 2つの意味があるため、明示的に選択された場合のみtrueにして区別する
        public bool CategorySpecified { get; init; }
        public required MainWindow MainWindow { get; init; }
    }
}
