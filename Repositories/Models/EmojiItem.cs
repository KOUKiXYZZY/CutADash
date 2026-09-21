using System.Collections.Generic;

namespace CutADash.Models
{
    public class EmojiItem
    {
        /// <summary>顔・動物・乗り物・人・物 など、大まかな分類。</summary>
        public string Kind { get; set; } = string.Empty;

        /// <summary>実際の絵文字。</summary>
        public string Emoji { get; set; } = string.Empty;

        /// <summary>ツールチップに表示する説明文(未登録ならnull)。</summary>
        public string? Text { get; set; }

        /// <summary>
        /// 同じKind内での0始まりの位置。kindごとのメインスプライトシート(10列)上での
        /// セル位置(列=Index%10, 行=Index/10)の算出に使う。
        /// </summary>
        public int Index { get; set; }

        /// <summary>肌の色バリエーション(グリフ文字列、Light→Darkの順)。無ければ空。</summary>
        public List<string> Variants { get; set; } = new();

        /// <summary>
        /// バリエーション用スプライトシート(kindごとの{kind}.variants.scale-*.png、5列)上での
        /// 行番号。Variantsが空の場合は無意味(-1)。
        /// </summary>
        public int VariantRow { get; set; } = -1;

        public bool HasVariants => Variants.Count > 0;
    }
}
