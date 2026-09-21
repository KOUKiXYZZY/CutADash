using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CutADash.Repositories
{
    using CutADash.Models;
    using CutADash.Repositories.Data.Entities;
    using CutADash.Repositories.Utils;
    using SQLite;
    using System.Text.Json;

    /// <summary>
    /// クリップボード履歴のSQLiteによる永続化。保存先のパスは呼び出し側(CutADashプロジェクトの
    /// AppPaths)が決めて渡す。このプロジェクト自体はどこに保存するかを知らない。
    /// 検索はFTS5(trigramトークナイザ)による全文検索を使う。日本語のように単語間に
    /// スペースが無い文章でも、3文字単位のn-gramで部分一致検索できる。
    /// 画像はDBに直接入れるとメモリ・DBサイズを圧迫するため、フルサイズはimageDirectory配下の
    /// ファイルとして書き出し、DBには一覧表示用の縮小サムネイルとファイルパスだけを持たせる。
    /// </summary>
    public class ClipboardRepository
    {
        private readonly SQLiteAsyncConnection _connection;
        private readonly string _imageDirectory;

        /// <summary>
        /// サムネイルの最大辺の長さ(px)。設定画面から即座に変更できるよう、
        /// コンストラクタ引数だけでなく書き込み可能なプロパティとしても公開する。
        /// </summary>
        public uint ThumbnailMaxDimension { get; set; }

        public ClipboardRepository(string dbPath, string imageDirectory, uint thumbnailMaxDimension = 200)
        {
            // SQLCipherによる暗号化。鍵はCommon.Db.DbEncryptionKeyProviderがDPAPIで管理しており、
            // Migration.exe側で暗号化済みDBとして作成/移行済みであることが前提
            var connectionString = new SQLiteConnectionString(
                dbPath, storeDateTimeAsTicks: true, key: Common.Db.DbEncryptionKeyProvider.GetOrCreateKey());
            _connection = new SQLiteAsyncConnection(connectionString);
            _imageDirectory = imageDirectory;
            ThumbnailMaxDimension = thumbnailMaxDimension;
            Directory.CreateDirectory(_imageDirectory);

            _connection.CreateTableAsync<ClipboardItemEntity>()
                .Wait();

            // ClipboardItemEntityとは別に持つFTS5テーブル。ItemIdでEntity.Idと対応付ける
            // (external contentテーブル+トリガーは使わず、Insert/DeleteAll時に手動で同期する)
            _connection.ExecuteAsync(
                "CREATE VIRTUAL TABLE IF NOT EXISTS ClipboardItemFts USING fts5(Text, ItemId UNINDEXED, tokenize='trigram')")
                .Wait();
        }

        public async Task InsertAsync(ClipboardItem item)
        {
            string? imageFilePath = null;
            string? thumbnailFilePath = null;
            var isGif = false;

            if (item.Type == ClipboardContentType.Image && item.Image is not null)
            {
                isGif = IsGifImage(item.Image);
                var extension = isGif ? "gif" : "png";
                imageFilePath = Path.Combine(_imageDirectory, $"{Guid.NewGuid():N}.{extension}");
                await Common.Db.ImageFileCipher.WriteEncryptedFileAsync(imageFilePath, item.Image);

                // GIFでもサムネイル生成はPNGへの変換(先頭フレームのみ)になるため、
                // 縮小画像は常にアニメーションを持たない固定のPNGになる
                var thumbnail = await ImageThumbnailGenerator.CreateThumbnailAsync(item.Image, ThumbnailMaxDimension);
                if (thumbnail is not null)
                {
                    thumbnailFilePath = Path.Combine(_imageDirectory, $"{Guid.NewGuid():N}_thumb.png");
                    await Common.Db.ImageFileCipher.WriteEncryptedFileAsync(thumbnailFilePath, thumbnail);
                }
            }

            string? rawFormatsFilePath = null;
            if (item.RawFormats is { Count: > 0 })
            {
                var serialized = Common.Infra.Win32.RawClipboardFormats.Serialize(item.RawFormats);
                rawFormatsFilePath = Path.Combine(_imageDirectory, $"{Guid.NewGuid():N}.rawfmt");
                await Common.Db.ImageFileCipher.WriteEncryptedFileAsync(rawFormatsFilePath, serialized);
            }

            var entity = new ClipboardItemEntity
            {
                Type = (int)item.Type,
                Text = item.Text,
                Rtf = item.Rtf,
                Html = item.Html,
                ImageFilePath = imageFilePath,
                ThumbnailFilePath = thumbnailFilePath,
                IsGif = isGif,
                IsShape = item.IsShape,
                RawFormatsFilePath = rawFormatsFilePath,
                FilesJson = item.Files == null
                    ? null
                    : JsonSerializer.Serialize(item.Files),
                Timestamp = item.Timestamp,
                SourceAppName = item.SourceAppName
            };

            await _connection.InsertAsync(entity);
            item.Id = entity.Id;
            item.ImageFilePath = imageFilePath;
            item.ThumbnailFilePath = thumbnailFilePath;
            item.IsGif = isGif;
            item.RawFormatsFilePath = rawFormatsFilePath;
            item.Image = null; // 永続化できたので、メモリ上のフルサイズ画像は残さない
            item.RawFormats = null; // 同上。ペースト時はRawFormatsFilePathから読み込む

            if (!string.IsNullOrEmpty(entity.Text))
            {
                await _connection.ExecuteAsync(
                    "INSERT INTO ClipboardItemFts(Text, ItemId) VALUES (?, ?)",
                    entity.Text, entity.Id);
            }
        }

        /// <summary>
        /// 同じTextの項目が既にあれば削除してから登録し直す。並び順はTimestamp降順なので、
        /// これだけで結果的に「一番上に上がる」動きになる。
        /// </summary>
        public async Task InsertOrBumpAsync(ClipboardItem item)
        {
            if (!string.IsNullOrEmpty(item.Text))
            {
                // Textだけで判定すると、同じ文面を書式付きでコピーしてあった行が、
                // 後からプレーンテキストでコピーしただけで消えてしまう。
                // 書式(Rtf)も一致した場合のみ同じ内容とみなす。
                //
                // Htmlは判定に含めない。ブラウザはほぼ全てのコピーにCF_HTMLを付けるうえ、
                // ラッパーのオフセット値が毎回変わるため、含めると重複が全く効かなくなる。
                //
                // SQLではNULL = NULLが成立しないため、Rtfの有無で条件を分ける
                var duplicates = item.Rtf is null
                    ? await _connection.Table<ClipboardItemEntity>()
                        .Where(x => x.Text == item.Text && x.Rtf == null)
                        .ToListAsync()
                    : await _connection.Table<ClipboardItemEntity>()
                        .Where(x => x.Text == item.Text && x.Rtf == item.Rtf)
                        .ToListAsync();

                foreach (var duplicate in duplicates)
                {
                    await DeleteEntityAsync(duplicate);
                }
            }

            await InsertAsync(item);
        }

        /// <summary>
        /// 既存の項目(Id指定)のTimestampだけを現在時刻に更新する。並び順はTimestamp降順
        /// なので、これだけで一覧の先頭に上がる。既存の履歴項目を貼り付けたときに使う
        /// (Text/Image/リッチテキストいずれの種別でも、内容を書き換えずに済む)。
        /// </summary>
        public async Task TouchAsync(int id, DateTime timestamp)
        {
            if (id == 0)
                return;

            await _connection.ExecuteAsync(
                "UPDATE ClipboardItemEntity SET Timestamp = ? WHERE Id = ?",
                timestamp, id);
        }

        /// <summary>Contentsで編集したテキスト/リッチテキストを保存する。</summary>
        public async Task UpdateContentAsync(int id, string? text, string? rtf, string? html)
        {
            if (id == 0)
                return;

            await _connection.ExecuteAsync(
                "UPDATE ClipboardItemEntity SET Text = ?, Rtf = ?, Html = ? WHERE Id = ?",
                text, rtf, html, id);
        }

        /// <summary>Shape/Image項目の表示名を変更する(未設定に戻す場合はnameにnullを渡す)。</summary>
        public async Task RenameItemAsync(int id, string? name)
        {
            if (id == 0)
                return;

            // NameだけだとFTS/LIKE検索(どちらもTextしか見ない)に掛からず、名前を付けても
            // 検索で見つけられないため、検索対象のTextへも同じ値をミラーする。
            // Shape/Image項目は挿入時Text=nullでFTS行自体が存在しないため、
            // 既存行の削除→(名前が付いていれば)挿入し直す形で同期する
            await _connection.ExecuteAsync(
                "UPDATE ClipboardItemEntity SET Name = ?, Text = ? WHERE Id = ?",
                name, name, id);

            await _connection.ExecuteAsync("DELETE FROM ClipboardItemFts WHERE ItemId = ?", id);
            if (!string.IsNullOrEmpty(name))
            {
                await _connection.ExecuteAsync(
                    "INSERT INTO ClipboardItemFts(Text, ItemId) VALUES (?, ?)",
                    name, id);
            }
        }

        // 一覧表示ではフルサイズのImageを使わないため、GetAllAsync/SearchAsyncでは
        // Thumbnail/ImageFilePathだけを取得する。フルサイズはペースト/詳細表示時に
        // ImageFilePathの指すファイルから読み込む。
        private const string ListColumns = "Id, Type, Text, Rtf, Html, ImageFilePath, ThumbnailFilePath, IsGif, IsShape, RawFormatsFilePath, FilesJson, Timestamp, SourceAppName, Name";
        private const string ListColumnsPrefixed = "e.Id, e.Type, e.Text, e.Rtf, e.Html, e.ImageFilePath, e.ThumbnailFilePath, e.IsGif, e.IsShape, e.RawFormatsFilePath, e.FilesJson, e.Timestamp, e.SourceAppName, e.Name";

        /// <summary>新しい順に読み込む。limitを指定すると直近limit件だけ取得する。</summary>
        public async Task<List<ClipboardItem>> GetAllAsync(int? limit = null)
        {
            var sql = $"SELECT {ListColumns} FROM ClipboardItemEntity ORDER BY Timestamp DESC";

            var entities = limit is int n
                ? await _connection.QueryAsync<ClipboardItemEntity>(sql + " LIMIT ?", n)
                : await _connection.QueryAsync<ClipboardItemEntity>(sql);

            return entities.Select(ToClipboardItem).ToList();
        }

        // trigramトークナイザは3文字未満だと有効なトリグラムを作れずヒットしないため、
        // それより短いクエリはLIKEにフォールバックする
        private const int MinFtsQueryLength = 3;

        /// <summary>Textを検索し、新しい順に返す。3文字以上ならFTS5(全文検索)、
        /// それより短ければLIKE(部分一致)で検索する。</summary>
        public async Task<List<ClipboardItem>> SearchAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<ClipboardItem>();

            var entities = query.Length < MinFtsQueryLength
                ? await _connection.QueryAsync<ClipboardItemEntity>(
                    $"SELECT {ListColumns} FROM ClipboardItemEntity WHERE Text LIKE ? ORDER BY Timestamp DESC",
                    $"%{query}%")
                : await _connection.QueryAsync<ClipboardItemEntity>(
                    $"""
                    SELECT {ListColumnsPrefixed} FROM ClipboardItemEntity e
                    JOIN ClipboardItemFts f ON f.ItemId = e.Id
                    WHERE ClipboardItemFts MATCH ?
                    GROUP BY e.Id
                    ORDER BY e.Timestamp DESC
                    """,
                    ToFtsPhraseQuery(query));

            return entities.Select(ToClipboardItem).ToList();
        }

        /// <summary>指定した1件を削除する(画像ファイルがあればそれも削除する)。</summary>
        public async Task DeleteAsync(ClipboardItem item)
        {
            if (item.Id == 0)
                return;

            var entity = await _connection.Table<ClipboardItemEntity>()
                .Where(x => x.Id == item.Id)
                .FirstOrDefaultAsync();

            if (entity is not null)
                await DeleteEntityAsync(entity);
        }

        /// <summary>
        /// 保持件数の上限(設定変更時など)に合わせて、新しい順にmaxCount件だけ残し、
        /// はみ出した古い項目をDB上からも削除する(画像ファイルも合わせて削除する)。
        /// </summary>
        public async Task TrimToCountAsync(int maxCount)
        {
            if (maxCount < 0)
                return;

            var entities = await _connection.Table<ClipboardItemEntity>()
                .OrderByDescending(x => x.Timestamp)
                .ToListAsync();

            foreach (var entity in entities.Skip(maxCount))
            {
                await DeleteEntityAsync(entity);
            }
        }

        public async Task DeleteAllAsync()
        {
            var entities = await _connection.Table<ClipboardItemEntity>().ToListAsync();
            foreach (var entity in entities)
            {
                DeleteImageFileIfExists(entity.ImageFilePath);
                DeleteImageFileIfExists(entity.ThumbnailFilePath);
                DeleteImageFileIfExists(entity.RawFormatsFilePath);
            }

            await _connection.DeleteAllAsync<ClipboardItemEntity>();
            await _connection.ExecuteAsync("DELETE FROM ClipboardItemFts");
        }

        private async Task DeleteEntityAsync(ClipboardItemEntity entity)
        {
            DeleteImageFileIfExists(entity.ImageFilePath);
            DeleteImageFileIfExists(entity.ThumbnailFilePath);
            DeleteImageFileIfExists(entity.RawFormatsFilePath);

            await _connection.DeleteAsync(entity);
            await _connection.ExecuteAsync("DELETE FROM ClipboardItemFts WHERE ItemId = ?", entity.Id);
        }

        private static void DeleteImageFileIfExists(string? imageFilePath)
        {
            if (string.IsNullOrEmpty(imageFilePath))
                return;

            try
            {
                if (File.Exists(imageFilePath))
                    File.Delete(imageFilePath);
            }
            catch
            {
                // 削除に失敗しても履歴の削除自体は続行する(孤立ファイルが残るだけで実害は小さい)
            }
        }

        // GIFはマジックバイト("GIF87a"/"GIF89a")の先頭3バイト"GIF"で判定する
        private static bool IsGifImage(byte[] bytes)
        {
            return bytes.Length >= 3
                && bytes[0] == (byte)'G'
                && bytes[1] == (byte)'I'
                && bytes[2] == (byte)'F';
        }

        // ユーザー入力をそのままMATCHに渡すと、"-"や"*"などがFTS5のクエリ構文として
        // 解釈されエラーになることがあるため、二重引用符で囲んだフレーズとして扱う
        private static string ToFtsPhraseQuery(string query)
        {
            return "\"" + query.Replace("\"", "\"\"") + "\"";
        }

        private static ClipboardItem ToClipboardItem(ClipboardItemEntity x) => new()
        {
            Id = x.Id,
            Type = (ClipboardContentType)x.Type,
            Text = x.Text,
            Rtf = x.Rtf,
            Html = x.Html,
            ImageFilePath = x.ImageFilePath,
            ThumbnailFilePath = x.ThumbnailFilePath,
            IsGif = x.IsGif,
            IsShape = x.IsShape,
            RawFormatsFilePath = x.RawFormatsFilePath,
            Files = x.FilesJson == null
                ? null
                : JsonSerializer.Deserialize<List<string>>(x.FilesJson),
            Timestamp = x.Timestamp,
            SourceAppName = x.SourceAppName,
            Name = x.Name
        };
    }
}
