using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace CutADash.Repositories
{
    using CutADash.Models;

    /// <summary>
    /// 絵文字マスタの読み込み。以前はSQLite(emoji.db)で管理していたが、アプリに
    /// 絵文字を追加/削除する機能は無く実質読み取り専用だったため、
    /// Assets/Emoji配下のkind別JSON(絵文字一覧、Unicode文字そのもの)を直接読み込む方式にした
    /// (SQLite自体・暗号化・マイグレーションが不要になる)。一覧・表示用スプライトシートPNGは
    /// どちらもMicrosoft Fluent Emoji(microsoft/fluentui-emoji、MIT、AssetsSrc配下に
    /// サブモジュールとして追加済み)を情報源に、AssetsSrc/EmojiGenerateScript/build_emoji_data.pyで
    /// ビルド前に生成したものをそのまま同梱している。
    /// </summary>
    public class EmojiRepository
    {
        // Fluent Emoji自身が持つgroup分類とその並び順に合わせる(EmojiGenerateScript/
        // build_emoji_data.pyの出力ファイル名と一致させること)。EmojiListFrameの
        // カテゴリタブの並び順に反映されるため、ファイル列挙順(OS依存)には頼らず固定する
        private static readonly string[] KindOrder =
        {
            "Smileys & Emotion", "People & Body", "Animals & Nature",
            "Food & Drink", "Travel & Places", "Activities",
            "Objects", "Symbols", "Flags",
        };

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        private sealed class EmojiFilePayload
        {
            public string Kind { get; set; } = "";
            public List<EmojiFileItem> Items { get; set; } = new();
        }

        private sealed class EmojiFileItem
        {
            public string Emoji { get; set; } = "";
            public string? Text { get; set; }
            public List<string>? Variants { get; set; }
        }

        private readonly string _assetsDir;

        private List<EmojiItem>? _all;
        private Dictionary<string, List<EmojiItem>>? _byKind;

        public EmojiRepository(string assetsDir)
        {
            _assetsDir = assetsDir;
        }

        /// <summary>絵文字のファイル名として使えるよう、kind名から空白・&amp;を置き換える。</summary>
        public static string ToFileNameStem(string kind) => kind.Replace(" ", "_").Replace("&", "and");

        private void EnsureLoaded()
        {
            if (_all is not null)
                return;

            var all = new List<EmojiItem>();
            var byKind = new Dictionary<string, List<EmojiItem>>();

            foreach (var kind in KindOrder)
            {
                var path = Path.Combine(_assetsDir, ToFileNameStem(kind) + ".json");
                if (!File.Exists(path))
                    continue;

                var payload = JsonSerializer.Deserialize<EmojiFilePayload>(File.ReadAllText(path), JsonOptions);
                if (payload is null)
                    continue;

                var list = new List<EmojiItem>(payload.Items.Count);
                var variantRow = 0;
                for (var i = 0; i < payload.Items.Count; i++)
                {
                    var src = payload.Items[i];
                    var variants = src.Variants ?? new List<string>();
                    list.Add(new EmojiItem
                    {
                        Kind = payload.Kind,
                        Emoji = src.Emoji,
                        Text = src.Text,
                        Index = i,
                        Variants = variants,
                        // バリエーションを持つアイテムが登場した順に、
                        // バリエーション用スプライトシート(build_emoji_data.py参照)の行を割り当てる
                        VariantRow = variants.Count > 0 ? variantRow++ : -1,
                    });
                }

                byKind[payload.Kind] = list;
                all.AddRange(list);
            }

            _all = all;
            _byKind = byKind;
        }

        public Task<List<EmojiItem>> GetAllAsync()
        {
            EnsureLoaded();
            return Task.FromResult(_all!);
        }

        /// <summary>実際に登録されているKindの一覧を、登録順(古い順)の重複なしで返す。</summary>
        public Task<List<string>> GetDistinctKindsAsync()
        {
            EnsureLoaded();
            return Task.FromResult(_byKind!.Keys.ToList());
        }

        /// <summary>指定した分類(Kind)の絵文字だけを読み込む。</summary>
        public Task<List<EmojiItem>> GetByKindAsync(string kind)
        {
            EnsureLoaded();
            return Task.FromResult(_byKind!.TryGetValue(kind, out var list) ? list : new List<EmojiItem>());
        }

        /// <summary>
        /// 指定したKindの絵文字件数。kindごとのメインスプライトシート(10列)の行数を
        /// 算出するために使う(EmojiSpriteAssets参照)。
        /// </summary>
        public int GetKindCount(string kind)
        {
            EnsureLoaded();
            return _byKind!.TryGetValue(kind, out var list) ? list.Count : 0;
        }

        /// <summary>
        /// 指定したKindで肌の色バリエーションを持つ絵文字の件数。
        /// バリエーション用スプライトシート(5列)の行数の算出に使う(EmojiSpriteAssets参照)。
        /// </summary>
        public int GetVariantRowCount(string kind)
        {
            EnsureLoaded();
            if (!_byKind!.TryGetValue(kind, out var list))
                return 0;

            var max = -1;
            foreach (var item in list)
            {
                if (item.VariantRow > max)
                    max = item.VariantRow;
            }
            return max + 1;
        }
    }
}
