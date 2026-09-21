using Microsoft.Windows.ApplicationModel.Resources;

namespace Common.Utils
{
    /// <summary>
    /// Strings/&lt;lang&gt;/Resources.reswから、表示言語に合わせた文字列を引く汎用ヘルパー。
    /// リソース自体はCutADash(実行ファイル)のresources.priにビルドされるが、
    /// ResourceManagerはプロセス単位でそれを見つけるため、Preferences等の別プロジェクトの
    /// アセンブリからでも(CutADash.exeの中で動いている限り)同じように使える。
    /// </summary>
    public static class AppStrings
    {
        private static readonly ResourceManager _resourceManager = new();

        // 非パッケージ化アプリではApplicationLanguages.PrimaryLanguageOverrideが
        // 例外を投げて効かないため、ResourceContextの"Language"修飾子を直接書き換えて
        // 明示的に言語を切り替える。空文字ならシステム既定のまま(修飾子を上書きしない)。
        private static readonly ResourceContext _resourceContext = _resourceManager.CreateResourceContext();

        /// <summary>表示言語を明示的に切り替える。languageは空文字でシステム既定に戻す。</summary>
        public static void SetLanguage(string language)
        {
            try
            {
                _resourceContext.QualifierValues["Language"] = string.IsNullOrEmpty(language)
                    ? Windows.Globalization.ApplicationLanguages.Languages[0]
                    : language;
            }
            catch
            {
            }
        }

        /// <summary>該当するリソースが無ければキーの値をそのまま返す。Resources.resw(既定のサブツリー)から引く。</summary>
        public static string Get(string key) => Get("Resources", key);

        /// <summary>
        /// 該当するリソースが無ければキーの値をそのまま返す。
        /// subtreeには、ファイル名(拡張子なし)を指定する(例: 別名のPrefResources.reswなら"PrefResources")。
        /// </summary>
        public static string Get(string subtree, string key)
        {
            try
            {
                var value = _resourceManager.MainResourceMap
                    .GetValue($"{subtree}/{key}", _resourceContext)
                    ?.ValueAsString;

                return string.IsNullOrEmpty(value) ? key : value;
            }
            catch
            {
                return key;
            }
        }
    }
}
