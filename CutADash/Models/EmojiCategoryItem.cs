namespace CutADash.Models
{
    /// <summary>
    /// EmojiListFrameのカテゴリ一覧で使う、カテゴリの言語に依存しないキーと表示名のペア。
    /// Keyがnullの場合は「全部」(Kindで絞り込まずDBを無条件に全件表示)を表す。
    /// </summary>
    public sealed class EmojiCategoryItem
    {
        public string? Key { get; }
        public string DisplayName { get; }

        public EmojiCategoryItem(string? key, string displayName)
        {
            Key = key;
            DisplayName = displayName;
        }
    }
}
