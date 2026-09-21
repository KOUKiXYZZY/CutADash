namespace SelectionToolbar.Utils
{
    /// <summary>
    /// Strings/&lt;lang&gt;/ToolbarResources.reswから、表示言語に合わせた文字列を引くヘルパー。
    /// リソースファイルの実体はSelectionToolbarプロジェクト配下に置くが、実際にビルドされる
    /// resources.priはCutADash(実行ファイル)側(020_CutADash.csproj内でPRIResourceとして
    /// リンク登録している)。詳細はPreferences.Utils.PreferencesStringsと同じ経緯。
    /// </summary>
    public static class ToolbarStrings
    {
        private const string Subtree = "ToolbarResources";

        /// <summary>該当するリソースが無ければキーの値をそのまま返す。</summary>
        public static string Get(string key) => Common.Utils.AppStrings.Get(Subtree, key);
    }
}
