namespace Preferences.Utils
{
    /// <summary>
    /// Strings/&lt;lang&gt;/PrefResources.reswから、表示言語に合わせた文字列を引くヘルパー。
    /// リソースファイルの実体はPreferencesプロジェクト配下に置くが、実際にビルドされる
    /// resources.priはCutADash(実行ファイル)側(020_CutADash.csproj内でPRIResourceとして
    /// リンク登録している)。別アセンブリ独自のpriを明示パスで読み込む方式は、実行時に
    /// リソースが正しく引けなかったため採用していない。
    /// </summary>
    internal static class PreferencesStrings
    {
        private const string Subtree = "PrefResources";

        /// <summary>該当するリソースが無ければキーの値をそのまま返す。</summary>
        public static string Get(string key) => Common.Utils.AppStrings.Get(Subtree, key);
    }
}
