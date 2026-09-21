using Common.Utils;
using SQLite;
using System.IO;

namespace Common.Db
{
    /// <summary>
    /// CutADash起動時に、実際にMigration.exeを起動する必要があるかどうかを軽く判定する。
    /// Migration.exeを毎回起動するとコストが無視できないため、ここで「明らかに不要」な場合は
    /// スキップできるようにする。判定自体は各DBを開いてSchemaVersionを覗くだけの軽い処理。
    /// </summary>
    public static class DbMigrationChecker
    {
        /// <summary>
        /// いずれかのDBでマイグレーションが必要ならtrueを返す。
        /// ・ファイルが存在しない
        /// ・鍵で開けない(暗号化されていない旧DBなど)
        /// ・SchemaVersionがCurrentVersionと異なる
        /// のいずれかに該当する場合に必要と判定する。
        /// </summary>
        public static bool NeedsMigration()
        {
            DbProviderInitializer.EnsureSqlCipherProvider();
            var key = DbEncryptionKeyProvider.GetOrCreateKey();

            foreach (var fileName in DbSchema.DatabaseFileNames)
            {
                var path = AppPaths.GetDataFilePath(fileName);

                if (!File.Exists(path))
                    return true;

                try
                {
                    var connectionString = new SQLiteConnectionString(path, storeDateTimeAsTicks: true, key: key);
                    using var connection = new SQLiteConnection(connectionString);

                    var version = connection.ExecuteScalar<int>(
                        "SELECT Version FROM SchemaVersion WHERE Id = 1");

                    if (version != DbSchema.CurrentVersion)
                        return true;
                }
                catch
                {
                    // 鍵で開けない(未暗号化の旧DB等)、SchemaVersionテーブルが無い等は
                    // すべて「マイグレーションが必要」として扱う
                    return true;
                }
            }

            return false;
        }
    }
}
