using SQLitePCL;

namespace Common.Db
{
    /// <summary>
    /// sqlite-net-pcl(SQLitePCLRaw)が使うネイティブプロバイダを、SQLCipher対応版に
    /// 明示的に固定する。sqlite-net-pclはbundle_green(素のSQLite)を暗黙に依存に含むため、
    /// 出力フォルダにe_sqlite3.dllとe_sqlcipher.dllの両方が並存してしまうことがある。
    /// 自動選択(Batteries_V2.Init())に任せるとどちらが選ばれるか保証できないため、
    /// CutADash/Migration.exeの両方で、最初のDBアクセスより前に必ずこれを呼ぶ。
    /// </summary>
    public static class DbProviderInitializer
    {
        private static bool _initialized;

        public static void EnsureSqlCipherProvider()
        {
            if (_initialized)
                return;

            raw.SetProvider(new SQLite3Provider_e_sqlcipher());
            _initialized = true;
        }
    }
}
