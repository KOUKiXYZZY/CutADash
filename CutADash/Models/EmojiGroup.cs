using System.Collections.Generic;

namespace CutADash.Models
{
    /// <summary>コンパクト表示用、種類(Kind)別にまとめた絵文字のグループ。</summary>
    public sealed class EmojiGroup : List<EmojiItem>
    {
        public string Key { get; }

        public EmojiGroup(string key, IEnumerable<EmojiItem> items) : base(items)
        {
            Key = key;
        }
    }
}
