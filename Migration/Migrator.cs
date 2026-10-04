using Common.Db;
using Common.Utils;
using SQLite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CutADash.Migration
{
    /// <summary>
    /// CutADashのDB(履歴・お気に入り・絵文字)を、必要に応じて作成/暗号化移行する。
    /// CutADashの起動時、DbMigrationChecker.NeedsMigration()がtrueを返した場合だけ
    /// Runが呼ばれる。DBを開くより前に必ず完了させる必要がある。
    ///
    /// 各DBファイルごとに以下のいずれかを行う:
    /// 1. ファイルが存在しない → 新規に暗号化された空DBを作り、SchemaVersionを書き込む
    /// 2. 鍵で開ける(=既に暗号化済み) → SchemaVersionを確認し、無ければ/古ければ作成・更新する
    /// 3. 鍵で開けない(=暗号化されていない旧DB) → SQLCipherのsqlcipher_exportで
    ///    新しい暗号化DBへ全データを複製し、元ファイルは.bakとして残す
    /// </summary>
    public static class Migrator
    {
        /// <summary>
        /// DBの作成/暗号化移行を実行する。成功なら0、失敗なら1を返す。
        /// 失敗しても例外は投げず、内容はmigration.logへ記録する
        /// (移行できなくてもアプリ自体は起動させたいため)。
        /// </summary>
        public static int Run()
        {
            using var log = OpenLog();

            try
            {
                DbProviderInitializer.EnsureSqlCipherProvider();
                var key = DbEncryptionKeyProvider.GetOrCreateKey();

                foreach (var fileName in DbSchema.DatabaseFileNames)
                {
                    var previousVersion = ReadSchemaVersion(fileName, key);
                    MigrateOne(fileName, key, log);

                    // v2: TreeViewの標準ドラッグ&ドロップがフォルダかどうかを区別せず
                    // ドロップを受け付けてしまっていた不具合により、項目(フォルダでない
                    // ノード)の下に別の項目がぶら下がってしまっているデータが存在しうる。
                    // 一度だけ(旧バージョンから上がる時だけ)ルート直下へ戻す
                    if (fileName == "favorites.db" && previousVersion < 2)
                        FixFavoriteItemsWithItemParent(key, log);
                }

                // DBのSQLCipher化とは別に、参照先の画像ファイル自体(ClipboardImages/
                // FavoriteImages配下)がまだ平文(旧バージョンで書き出されたもの)の場合は
                // AES-256-GCMで暗号化する
                EncryptPlainImageFiles("clipboard_history.db", "ClipboardItemEntity", key, log);
                EncryptPlainImageFiles("favorites.db", "FavoriteItemEntity", key, log);

                log.WriteLine("[Migration] All databases processed.");
                return 0;
            }
            catch (Exception ex)
            {
                log.WriteLine($"[Migration] Failed: {Common.Utils.ExceptionText.ToEnglishString(ex)}");
                return 1;
            }
        }

        public static void DumpRawFormats()
        {
            DbProviderInitializer.EnsureSqlCipherProvider();
            var key = DbEncryptionKeyProvider.GetOrCreateKey();
            var dbPath = AppPaths.GetDataFilePath("clipboard_history.db");

            if (!File.Exists(dbPath))
            {
                Console.WriteLine($"DBが見つかりません: {dbPath}");
                return;
            }

            var connectionString = new SQLiteConnectionString(dbPath, storeDateTimeAsTicks: true, key: key);
            using var connection = new SQLiteConnection(connectionString);

            var rows = connection.Query<RawFormatsRow>(
                "SELECT Id, Type, IsShape, RawFormatsFilePath, Timestamp FROM ClipboardItemEntity " +
                "WHERE Type = 2 ORDER BY Timestamp DESC LIMIT 10");

            if (rows.Count == 0)
            {
                Console.WriteLine("Image項目が1件もありません。");
                return;
            }

            foreach (var row in rows)
            {
                Console.WriteLine($"--- Id={row.Id} IsShape={row.IsShape} Timestamp={row.Timestamp} ---");

                if (string.IsNullOrEmpty(row.RawFormatsFilePath) || !File.Exists(row.RawFormatsFilePath))
                {
                    Console.WriteLine("  RawFormatsFilePath: (なし)");
                    continue;
                }

                try
                {
                    var decrypted = ImageFileCipher.ReadDecryptedFileAsync(row.RawFormatsFilePath).GetAwaiter().GetResult();
                    var formats = Common.Infra.Win32.RawClipboardFormats.Deserialize(decrypted);
                    Console.WriteLine($"  フォーマット数: {formats.Count}");
                    foreach (var name in formats.Keys)
                        Console.WriteLine($"    - {name} ({formats[name].Length} bytes)");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  読み込みに失敗: {ex.Message}");
                }
            }
        }

        private sealed class RawFormatsRow
        {
            public int Id { get; set; }
            public int Type { get; set; }
            public bool IsShape { get; set; }
            public string? RawFormatsFilePath { get; set; }
            public DateTime Timestamp { get; set; }
        }

        public static void DumpCount()
        {
            DbProviderInitializer.EnsureSqlCipherProvider();
            var key = DbEncryptionKeyProvider.GetOrCreateKey();
            var dbPath = AppPaths.GetDataFilePath("clipboard_history.db");

            if (!File.Exists(dbPath))
            {
                Console.WriteLine($"DBが見つかりません: {dbPath}");
                return;
            }

            var connectionString = new SQLiteConnectionString(dbPath, storeDateTimeAsTicks: true, key: key);
            using var connection = new SQLiteConnection(connectionString);

            var total = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM ClipboardItemEntity");
            Console.WriteLine($"合計: {total}件");

            var byType = connection.Query<TypeCountRow>(
                "SELECT Type, COUNT(*) as Count FROM ClipboardItemEntity GROUP BY Type");
            foreach (var row in byType)
                Console.WriteLine($"  Type={row.Type}: {row.Count}件");
        }

        private sealed class TypeCountRow
        {
            public int Type { get; set; }
            public int Count { get; set; }
        }

        public static void DumpText()
        {
            DbProviderInitializer.EnsureSqlCipherProvider();
            var key = DbEncryptionKeyProvider.GetOrCreateKey();
            var dbPath = AppPaths.GetDataFilePath("clipboard_history.db");

            if (!File.Exists(dbPath))
            {
                Console.WriteLine($"DBが見つかりません: {dbPath}");
                return;
            }

            var connectionString = new SQLiteConnectionString(dbPath, storeDateTimeAsTicks: true, key: key);
            using var connection = new SQLiteConnection(connectionString);

            // Type=1(Text)を新しい順に20件
            var rows = connection.Query<TextRow>(
                "SELECT Id, Text, Rtf, Html, SourceAppName, Timestamp FROM ClipboardItemEntity " +
                "WHERE Type = 1 ORDER BY Timestamp DESC LIMIT 20");

            if (rows.Count == 0)
            {
                Console.WriteLine("Text項目が1件もありません。");
                return;
            }

            foreach (var row in rows)
            {
                var textPreview = Preview(row.Text);
                var rtfLength = row.Rtf?.Length ?? 0;
                var htmlLength = row.Html?.Length ?? 0;
                var isBlank = string.IsNullOrEmpty(row.Text) && rtfLength == 0 && htmlLength == 0;

                Console.WriteLine(
                    $"Id={row.Id} SourceAppName={row.SourceAppName} Timestamp={row.Timestamp} " +
                    $"IsBlank={isBlank} Text=\"{textPreview}\" RtfLength={rtfLength} HtmlLength={htmlLength}");
            }
        }

        private static string Preview(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            var oneLine = text.Replace("\r", "").Replace("\n", "\\n");
            return oneLine.Length > 60 ? oneLine[..60] + "..." : oneLine;
        }

        private sealed class TextRow
        {
            public int Id { get; set; }
            public string? Text { get; set; }
            public string? Rtf { get; set; }
            public string? Html { get; set; }
            public string? SourceAppName { get; set; }
            public DateTime Timestamp { get; set; }
        }

        private static void MigrateOne(string fileName, string key, TextWriter log)
        {
            var path = AppPaths.GetDataFilePath(fileName);
            log.WriteLine($"[Migration] {fileName} is being checked...");

            if (!File.Exists(path))
            {
                log.WriteLine($"[Migration] {fileName} does not exist; creating a new encrypted DB.");
                CreateNewEncryptedDatabase(path, key);
                EnsureSchemaVersion(path, key, log);
                return;
            }

            if (TryOpenWithKey(path, key))
            {
                log.WriteLine($"[Migration] {fileName} is already encrypted. Checking the schema version.");
                EnsureSchemaVersion(path, key, log);
                return;
            }

            log.WriteLine($"[Migration] {fileName} appears to be an old unencrypted format. Migrating to an encrypted DB.");
            MigratePlainToEncrypted(path, key, log);
            EnsureSchemaVersion(path, key, log);
        }

        // 鍵で実際に開けるか(=既に暗号化済みか)を試す。開けなければfalse
        private static bool TryOpenWithKey(string path, string key)
        {
            try
            {
                var connectionString = new SQLiteConnectionString(path, storeDateTimeAsTicks: true, key: key);
                using var connection = new SQLiteConnection(connectionString);
                // 暗号化DBかどうかは、実際に何か読んでみないと確定しない
                // (SQLCipherは鍵が違っていても接続自体はエラーにせず、最初のクエリで失敗する)
                connection.ExecuteScalar<int>("SELECT count(*) FROM sqlite_master");
                return true;
            }
            catch
            {
                return false;
            }
        }

        // MigrateOneがSchemaVersionを新しい値へ上書きしてしまう前に、旧バージョンを読んでおく。
        // ファイルが無い/読めない/バージョン列が無い等はすべて0(=最も古い)として扱う
        // (新規作成されるDBなので、以降のバージョン別移行処理は当然データが無く実質no-opになる)
        private static int ReadSchemaVersion(string fileName, string key)
        {
            var path = AppPaths.GetDataFilePath(fileName);
            if (!File.Exists(path))
                return 0;

            try
            {
                var connectionString = new SQLiteConnectionString(path, storeDateTimeAsTicks: true, key: key);
                using var connection = new SQLiteConnection(connectionString);
                return connection.ExecuteScalar<int>("SELECT Version FROM SchemaVersion WHERE Id = 1");
            }
            catch
            {
                return 0;
            }
        }

        private sealed class FavoriteRow
        {
            public int Id { get; set; }
            public int? ParentId { get; set; }
            public bool IsFolder { get; set; }
            public int Lft { get; set; }
            public int Rgt { get; set; }
        }

        private sealed class FavoriteTreeNode
        {
            public required FavoriteRow Row { get; init; }
            public List<FavoriteTreeNode> Children { get; } = new();
        }

        /// <summary>
        /// favorites.dbで、項目(フォルダでないノード)の下に別の項目がぶら下がっている
        /// 壊れたデータを、ルート直下(ParentId = null)へ戻す。TreeViewの標準ドラッグ&ドロップが
        /// フォルダかどうかを区別せずドロップを受け付けてしまっていた不具合の名残
        /// (FavoriteRepository.MoveNodeAsync側は既に修正済みで、以降は新たに発生しない)。
        /// ParentIdを直接書き換えるだけだと入れ子集合モデル(Lft/Rgt)が壊れるため、
        /// 修正後の親子関係でDFS順に振り直してから全行まとめて書き戻す。
        /// </summary>
        private static void FixFavoriteItemsWithItemParent(string key, TextWriter log)
        {
            var dbPath = AppPaths.GetDataFilePath("favorites.db");
            if (!File.Exists(dbPath))
                return;

            var connectionString = new SQLiteConnectionString(dbPath, storeDateTimeAsTicks: true, key: key);
            using var connection = new SQLiteConnection(connectionString);

            List<FavoriteRow> rows;
            try
            {
                rows = connection.Query<FavoriteRow>(
                    "SELECT Id, ParentId, IsFolder, Lft, Rgt FROM FavoriteItemEntity");
            }
            catch (Exception ex)
            {
                log.WriteLine($"[Migration] Failed to read favorites.db (skipped): {ex.Message}");
                return;
            }

            var byId = rows.ToDictionary(r => r.Id);
            var fixedCount = 0;

            foreach (var row in rows)
            {
                if (row.ParentId is int parentId
                    && byId.TryGetValue(parentId, out var parent)
                    && !parent.IsFolder)
                {
                    row.ParentId = null;
                    fixedCount++;
                }
            }

            if (fixedCount == 0)
            {
                log.WriteLine("[Migration] favorites.db: no broken data (items nested under items) found.");
                return;
            }

            RenumberAndSave(connection, rows);

            log.WriteLine($"[Migration] favorites.db: moved {fixedCount} item(s) nested under items back to the root.");
        }

        // 現在のLft昇順(=既存の表示順)を保ったまま、修正後のParentIdで木を組み直し、
        // DFSでLft/Rgtを1から振り直して全行を1トランザクションで書き戻す
        // (FavoriteRepository.BuildForest/Renumberと同じ考え方)
        private static void RenumberAndSave(SQLiteConnection connection, List<FavoriteRow> rows)
        {
            var byId = rows.ToDictionary(r => r.Id, r => new FavoriteTreeNode { Row = r });
            var roots = new List<FavoriteTreeNode>();

            foreach (var row in rows.OrderBy(r => r.Lft))
            {
                var node = byId[row.Id];
                if (row.ParentId is int parentId && byId.TryGetValue(parentId, out var parentNode))
                    parentNode.Children.Add(node);
                else
                    roots.Add(node);
            }

            var counter = 1;
            void Walk(FavoriteTreeNode node)
            {
                node.Row.Lft = counter++;
                foreach (var child in node.Children)
                    Walk(child);
                node.Row.Rgt = counter++;
            }
            foreach (var root in roots)
                Walk(root);

            connection.RunInTransaction(() =>
            {
                foreach (var row in rows)
                {
                    connection.Execute(
                        "UPDATE FavoriteItemEntity SET ParentId = ?, Lft = ?, Rgt = ? WHERE Id = ?",
                        row.ParentId, row.Lft, row.Rgt, row.Id);
                }
            });
        }

        private static void CreateNewEncryptedDatabase(string path, string key)
        {
            var connectionString = new SQLiteConnectionString(path, storeDateTimeAsTicks: true, key: key);
            using var connection = new SQLiteConnection(connectionString);
            // 接続を確立するだけで、鍵付きの新規DBファイルが作成される
            connection.ExecuteScalar<int>("SELECT count(*) FROM sqlite_master");
        }

        private static void EnsureSchemaVersion(string path, string key, TextWriter log)
        {
            var connectionString = new SQLiteConnectionString(path, storeDateTimeAsTicks: true, key: key);
            using var connection = new SQLiteConnection(connectionString);

            connection.Execute(
                "CREATE TABLE IF NOT EXISTS SchemaVersion (Id INTEGER PRIMARY KEY, Version INTEGER NOT NULL)");

            connection.Execute(
                "INSERT OR REPLACE INTO SchemaVersion (Id, Version) VALUES (1, ?)",
                DbSchema.CurrentVersion);

            log.WriteLine($"[Migration] SchemaVersion set to {DbSchema.CurrentVersion}.");
        }

        // SQLCipherのsqlcipher_export()を使い、暗号化されていない旧DBの全内容
        // (テーブル・インデックス・データすべて)を新しい暗号化DBへ複製する。
        // 元のプレーンなファイルは万一のため.bakとして残す(上書きしない)
        private static void MigratePlainToEncrypted(string plainPath, string key, TextWriter log)
        {
            var newPath = plainPath + ".new";
            var backupPath = plainPath + ".bak";

            if (File.Exists(newPath))
                File.Delete(newPath);

            using (var plainConnection = new SQLiteConnection(plainPath))
            {
                // ATTACHのパス・鍵はプレースホルダが使えないため直接埋め込む。
                // pathはAppPaths管理下の固定ファイル名、keyは自前生成した16進文字列のみなので、
                // どちらも引用符やSQL構文上の特殊文字を含まない
                var escapedNewPath = newPath.Replace("'", "''");
                plainConnection.Execute(
                    $"ATTACH DATABASE '{escapedNewPath}' AS encrypted KEY '{key}'");
                plainConnection.ExecuteScalar<string>("SELECT sqlcipher_export('encrypted')");
                plainConnection.Execute("DETACH DATABASE encrypted");
            }

            if (File.Exists(backupPath))
                File.Delete(backupPath);

            File.Move(plainPath, backupPath);
            File.Move(newPath, plainPath);

            log.WriteLine($"[Migration] Migration completed. The original file was kept as {Path.GetFileName(backupPath)}.");
        }

        // 指定したDB(既に暗号化済み)から、画像ファイルのパス列(ImageFilePath/ThumbnailFilePath)
        // を読み出し、まだ平文(PNG/GIFのマジックバイトで始まる)のファイルがあれば
        // AES-256-GCMで暗号化して上書きする。既に暗号化済みのファイルはそのまま何もしない
        private static void EncryptPlainImageFiles(string dbFileName, string tableName, string key, TextWriter log)
        {
            var dbPath = AppPaths.GetDataFilePath(dbFileName);
            if (!File.Exists(dbPath))
                return;

            var connectionString = new SQLiteConnectionString(dbPath, storeDateTimeAsTicks: true, key: key);
            using var connection = new SQLiteConnection(connectionString);

            List<(string? ImageFilePath, string? ThumbnailFilePath)> rows;
            try
            {
                rows = connection.Query<ImagePathRow>($"SELECT ImageFilePath, ThumbnailFilePath FROM {tableName}")
                    .Select(r => (r.ImageFilePath, r.ThumbnailFilePath))
                    .ToList();
            }
            catch (Exception ex)
            {
                // テーブルが無い(初回作成直後で空、等)場合もあるため、失敗しても処理継続する
                log.WriteLine($"[Migration] {dbFileName}: failed to read image path columns (skipped): {ex.Message}");
                return;
            }

            var encryptedCount = 0;
            foreach (var row in rows)
            {
                encryptedCount += EncryptIfPlain(row.ImageFilePath);
                encryptedCount += EncryptIfPlain(row.ThumbnailFilePath);
            }

            log.WriteLine($"[Migration] {dbFileName}: encrypted {encryptedCount} image file(s).");
        }

        private static int EncryptIfPlain(string? path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return 0;

            try
            {
                var bytes = File.ReadAllBytes(path);
                if (!ImageFileCipher.LooksLikePlainImage(bytes))
                    return 0; // 既に暗号化済み、または未知の形式

                var encrypted = ImageFileCipher.Encrypt(bytes);
                File.WriteAllBytes(path, encrypted);
                return 1;
            }
            catch
            {
                // 1件の失敗で全体を止めない(ファイルが使用中等)
                return 0;
            }
        }

        private sealed class ImagePathRow
        {
            public string? ImageFilePath { get; set; }
            public string? ThumbnailFilePath { get; set; }
        }

        private static TextWriter OpenLog()
        {
            var path = AppPaths.GetDataFilePath("migration.log");
            var writer = new StreamWriter(path, append: true) { AutoFlush = true };
            writer.WriteLine($"===== {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====");
            return writer;
        }
    }
}
