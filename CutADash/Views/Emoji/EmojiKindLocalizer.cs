using Common.Utils;

namespace CutADash.Views.Emoji
{
    /// <summary>
    /// emoji.dbのKind列(例:"顔")をリソースキーとして、Strings/&lt;lang&gt;/Resources.reswから
    /// 表示言語に合わせた表示名を引く。該当する翻訳が無ければKindの値をそのまま返す。
    /// </summary>
    public static class EmojiKindLocalizer
    {
        public static string GetDisplayName(string kind) => AppStrings.Get(kind);
    }
}
