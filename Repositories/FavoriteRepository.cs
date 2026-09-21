using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CutADash.Repositories
{
    using CutADash.Models;
    using CutADash.Repositories.Data.Entities;
    using SQLite;
    using System.Text.Json;

    /// <summary>
    /// お気に入りのSQLiteによる永続化。ClipboardRepository(履歴)とは完全に別の
    /// DBファイル・画像ディレクトリを持ち、履歴側の上限削除やクリアの影響を受けない。
    /// お気に入りへの追加は、履歴の項目を「複製」する形で行う(画像ファイルもコピーする)。
    ///
    /// フォルダを含む階層構造は入れ子集合モデル(Lft/Rgt)で表す。お気に入りは想定件数が
    /// 小さい(履歴と違って数十〜数百件程度)ため、隙間シフトのようなインクリメンタルな
    /// SQLは実装せず、構造が変わるたび全行をメモリへ読み込んでツリーを組み替え、
    /// DFSでLft/Rgtを振り直して1トランザクションで書き戻す方式にしている。
    /// </summary>
    public class FavoriteRepository
    {
        private readonly SQLiteAsyncConnection _connection;
        private readonly string _imageDirectory;

        public FavoriteRepository(string dbPath, string imageDirectory)
        {
            var connectionString = new SQLiteConnectionString(
                dbPath, storeDateTimeAsTicks: true, key: Common.Db.DbEncryptionKeyProvider.GetOrCreateKey());
            _connection = new SQLiteAsyncConnection(connectionString);
            _imageDirectory = imageDirectory;
            Directory.CreateDirectory(_imageDirectory);

            // コンストラクタは同期呼び出し(DIコンテナ構築時、UIスレッド上)のため、ここだけ
            // 例外的に .Wait() でブロックする。ただし直接 .Wait() すると、内部のawaitが
            // 呼び出し元のSynchronizationContext(UIスレッド)への復帰を待とうとして、
            // そのUIスレッド自身が.Wait()でブロックされているためデッドロックする
            // (実際にアプリが起動時に固まって再現した)。Task.Runで一旦コンテキストの
            // 無いスレッドプールへ逃がしてから待つことでこれを避ける。
            Task.Run(() => _connection.CreateTableAsync<FavoriteItemEntity>()).Wait();
            Task.Run(BackfillNestedSetAsync).Wait();
        }

        /// <summary>
        /// Lft/Rgtが未採番(0)のまま残っている行を、FavoritedAt降順(従来の表示順)で
        /// ルート直下の兄弟としてDFS採番する。フォルダ機能を追加する前からある行が対象。
        /// 一度採番されればLft/Rgtが0に戻ることはないため、実質的に初回起動時にしか動かない。
        /// </summary>
        private async Task BackfillNestedSetAsync()
        {
            var unassigned = await _connection.Table<FavoriteItemEntity>()
                .Where(x => x.Lft == 0 && x.Rgt == 0)
                .OrderByDescending(x => x.FavoritedAt)
                .ToListAsync();

            if (unassigned.Count == 0)
                return;

            var counter = 1;
            foreach (var entity in unassigned)
            {
                entity.Lft = counter++;
                entity.Rgt = counter++;
                entity.ParentId = null;
                entity.IsFolder = false;
            }

            await SaveAllAsync(unassigned);
        }

        // ツリー操作中だけ使う、メモリ上の親子関係表現
        private sealed class TreeNode
        {
            public required FavoriteItemEntity Entity { get; init; }
            public List<TreeNode> Children { get; } = new();
        }

        private static (List<TreeNode> Roots, Dictionary<int, TreeNode> ById) BuildForest(List<FavoriteItemEntity> entities)
        {
            var byId = new Dictionary<int, TreeNode>();
            foreach (var entity in entities)
                byId[entity.Id] = new TreeNode { Entity = entity };

            var roots = new List<TreeNode>();

            // Lft昇順で見ていくと、ある親の子だけを取り出しても兄弟の並び順は保たれる
            // (ソート済み列の部分列は常にソート済みであるため)
            foreach (var entity in entities.OrderBy(x => x.Lft))
            {
                var node = byId[entity.Id];
                if (entity.ParentId is int parentId && byId.TryGetValue(parentId, out var parent))
                    parent.Children.Add(node);
                else
                    roots.Add(node);
            }

            return (roots, byId);
        }

        /// <summary>木構造全体をDFSでたどり、Lft/Rgtを1から振り直す。</summary>
        private static void Renumber(List<TreeNode> roots)
        {
            var counter = 1;

            void Visit(TreeNode node)
            {
                node.Entity.Lft = counter++;
                foreach (var child in node.Children)
                    Visit(child);
                node.Entity.Rgt = counter++;
            }

            foreach (var root in roots)
                Visit(root);
        }

        private static bool IsDescendantOf(TreeNode ancestor, int candidateId)
        {
            foreach (var child in ancestor.Children)
            {
                if (child.Entity.Id == candidateId || IsDescendantOf(child, candidateId))
                    return true;
            }

            return false;
        }

        private async Task SaveAllAsync(List<FavoriteItemEntity> entities)
        {
            await _connection.RunInTransactionAsync(conn =>
            {
                foreach (var entity in entities)
                    conn.Update(entity);
            });
        }

        /// <summary>
        /// 履歴の項目をお気に入りへ複製して追加する。画像があればファイルごと
        /// お気に入り専用ディレクトリへコピーする(履歴側のファイルを共有しない)。
        /// parentIdを指定すると、そのフォルダの最後の子として追加する(null=ルート直下)。
        /// </summary>
        public async Task<ClipboardItem> AddAsync(ClipboardItem source, int? parentId = null)
        {
            string? imageFilePath = null;
            string? thumbnailFilePath = null;

            if (!string.IsNullOrEmpty(source.ImageFilePath) && File.Exists(source.ImageFilePath))
            {
                imageFilePath = Path.Combine(_imageDirectory, Path.GetFileName(source.ImageFilePath));
                File.Copy(source.ImageFilePath, imageFilePath, overwrite: true);
            }

            if (!string.IsNullOrEmpty(source.ThumbnailFilePath) && File.Exists(source.ThumbnailFilePath))
            {
                thumbnailFilePath = Path.Combine(_imageDirectory, Path.GetFileName(source.ThumbnailFilePath));
                File.Copy(source.ThumbnailFilePath, thumbnailFilePath, overwrite: true);
            }

            // Excelの図形などをOLEオブジェクトのまま貼り戻すには、画像本体だけでなく
            // 生フォーマット一式も必要。これをコピーしないと、お気に入り経由の貼り付けが
            // 単純な画像になってしまう(履歴側の項目を消すと参照先も消えるため、
            // パスを共有せずお気に入り側の領域へ複製する)
            string? rawFormatsFilePath = null;
            if (!string.IsNullOrEmpty(source.RawFormatsFilePath) && File.Exists(source.RawFormatsFilePath))
            {
                rawFormatsFilePath = Path.Combine(_imageDirectory, Path.GetFileName(source.RawFormatsFilePath));
                File.Copy(source.RawFormatsFilePath, rawFormatsFilePath, overwrite: true);
            }

            // 図形がお気に入り経由だと画像になってしまう件の調査用。原因特定後に削除すること
            System.Diagnostics.Debug.WriteLine(
                $"[Shape][お気に入り登録] IsShape={source.IsShape} " +
                $"元パス={source.RawFormatsFilePath ?? "(null)"} " +
                $"元ファイルあり={(!string.IsNullOrEmpty(source.RawFormatsFilePath) && File.Exists(source.RawFormatsFilePath))} " +
                $"複製先={rawFormatsFilePath ?? "(null)"}");

            var entity = new FavoriteItemEntity
            {
                Type = (int)source.Type,
                Text = source.Text,
                Rtf = source.Rtf,
                Html = source.Html,
                ImageFilePath = imageFilePath,
                ThumbnailFilePath = thumbnailFilePath,
                IsGif = source.IsGif,
                IsShape = source.IsShape,
                RawFormatsFilePath = rawFormatsFilePath,
                FilesJson = source.Files == null
                    ? null
                    : JsonSerializer.Serialize(source.Files),
                Timestamp = source.Timestamp,
                SourceAppName = source.SourceAppName,
                Name = source.Name,
                FavoritedAt = DateTime.Now,
                IsFolder = false,
                ParentId = parentId
            };

            await AppendAsLastChildAsync(entity, parentId);

            return ToClipboardItem(entity);
        }

        /// <summary>「新しいフォルダ」という名前でフォルダを1つ作り、Idを返す。</summary>
        public async Task<int> CreateFolderAsync(string name, int? parentId)
        {
            var entity = new FavoriteItemEntity
            {
                IsFolder = true,
                Name = name,
                ParentId = parentId,
                FavoritedAt = DateTime.Now
            };

            await AppendAsLastChildAsync(entity, parentId);
            return entity.Id;
        }

        /// <summary>新規行をDBへ挿入した上で、指定した親の最後の子として木に組み込み、全体を振り直す。</summary>
        private async Task AppendAsLastChildAsync(FavoriteItemEntity entity, int? parentId)
        {
            await _connection.InsertAsync(entity);

            var entities = await LoadAllAsync();
            var (roots, byId) = BuildForest(entities);

            var node = byId[entity.Id];
            var siblings = parentId is int pid && byId.TryGetValue(pid, out var parent)
                ? parent.Children
                : roots;

            // InsertAsyncで追加された時点でもroots/parent.Childrenのどちらかに
            // BuildForestが自動的に入れてしまっているため、一旦除いてから末尾に積み直す
            siblings.Remove(node);
            siblings.Add(node);

            Renumber(roots);
            await SaveAllAsync(entities);
        }

        /// <summary>
        /// 指定した項目/フォルダを、newParentIdの子のnewIndex番目の位置へ移動する
        /// (同じ親内であれば並び替え、異なる親であればフォルダ間移動になる)。
        /// フォルダを自分自身または自分の子孫の中へ移動しようとした場合は例外を投げる。
        /// </summary>
        public async Task MoveNodeAsync(int nodeId, int? newParentId, int newIndex)
        {
            var entities = await LoadAllAsync();
            var (roots, byId) = BuildForest(entities);

            if (!byId.TryGetValue(nodeId, out var node))
                return;

            if (newParentId == nodeId)
                throw new InvalidOperationException("フォルダを自分自身の中には移動できません。");

            if (newParentId is int newParentIdValue && IsDescendantOf(node, newParentIdValue))
                throw new InvalidOperationException("フォルダを自分の子孫の中には移動できません。");

            var currentSiblings = node.Entity.ParentId is int currentParentId && byId.TryGetValue(currentParentId, out var currentParent)
                ? currentParent.Children
                : roots;
            currentSiblings.Remove(node);

            node.Entity.ParentId = newParentId;

            var targetSiblings = newParentId is int targetParentId && byId.TryGetValue(targetParentId, out var targetParent)
                ? targetParent.Children
                : roots;
            var clampedIndex = Math.Clamp(newIndex, 0, targetSiblings.Count);
            targetSiblings.Insert(clampedIndex, node);

            Renumber(roots);
            await SaveAllAsync(entities);
        }

        /// <summary>フォルダ名を変更する。</summary>
        public async Task RenameFolderAsync(int id, string name)
        {
            var entity = await _connection.Table<FavoriteItemEntity>()
                .Where(x => x.Id == id)
                .FirstOrDefaultAsync();

            if (entity is null || !entity.IsFolder)
                return;

            entity.Name = name;
            await _connection.UpdateAsync(entity);
        }

        /// <summary>
        /// アイテム(Shape/Image等)の表示名を変更する。検索(SearchText、Textしか見ない)に
        /// 掛かるよう、Nameと同じ値をTextへもミラーする。
        /// </summary>
        public async Task RenameItemAsync(int id, string? name)
        {
            var entity = await _connection.Table<FavoriteItemEntity>()
                .Where(x => x.Id == id)
                .FirstOrDefaultAsync();

            if (entity is null || entity.IsFolder)
                return;

            entity.Name = name;
            entity.Text = name;
            await _connection.UpdateAsync(entity);
        }

        /// <summary>FavoriteListFrameで最後に選択していたフォルダのIdを取得する。ルート直下ならnull。</summary>
        public async Task<int?> GetSelectedFolderIdAsync()
        {
            var entity = await _connection.Table<FavoriteItemEntity>()
                .Where(x => x.IsFolder && x.IsLastSelectedFolder)
                .FirstOrDefaultAsync();

            return entity?.Id;
        }

        /// <summary>FavoriteListFrameで最後に選択していたフォルダのIdを記録する。</summary>
        public async Task SetSelectedFolderIdAsync(int? folderId)
        {
            var previous = await _connection.Table<FavoriteItemEntity>()
                .Where(x => x.IsFolder && x.IsLastSelectedFolder)
                .FirstOrDefaultAsync();

            if (previous is not null && previous.Id != folderId)
            {
                previous.IsLastSelectedFolder = false;
                await _connection.UpdateAsync(previous);
            }

            if (folderId is not int id || previous?.Id == id)
                return;

            var target = await _connection.Table<FavoriteItemEntity>()
                .Where(x => x.Id == id && x.IsFolder)
                .FirstOrDefaultAsync();

            if (target is null)
                return;

            target.IsLastSelectedFolder = true;
            await _connection.UpdateAsync(target);
        }

        /// <summary>
        /// FavoriteListFrameのTreeViewでフォルダを開閉した時に、即座にその状態を保存する。
        /// </summary>
        public async Task SetFolderExpandedAsync(int id, bool isExpanded)
        {
            var entity = await _connection.Table<FavoriteItemEntity>()
                .Where(x => x.Id == id && x.IsFolder)
                .FirstOrDefaultAsync();

            if (entity is null || entity.IsExpanded == isExpanded)
                return;

            entity.IsExpanded = isExpanded;
            await _connection.UpdateAsync(entity);
        }

        /// <summary>Contentsでの編集内容(テキスト/リッチテキスト)を保存する。</summary>
        public async Task UpdateContentAsync(int id, string? text, string? rtf, string? html)
        {
            var entity = await _connection.Table<FavoriteItemEntity>()
                .Where(x => x.Id == id)
                .FirstOrDefaultAsync();

            if (entity is null)
                return;

            entity.Text = text;
            entity.Rtf = rtf;
            entity.Html = html;
            await _connection.UpdateAsync(entity);
        }

        /// <summary>木構造の全行をLft昇順(表示順)で読み込む。フォルダ・アイテムの両方を含む。</summary>
        public async Task<List<FavoriteItemEntity>> GetTreeEntitiesAsync()
        {
            return await _connection.Table<FavoriteItemEntity>()
                .OrderBy(x => x.Lft)
                .ToListAsync();
        }

        /// <summary>検索用。フォルダを除いたアイテムだけを、階層に関係なくフラットに読み込む。</summary>
        public async Task<List<ClipboardItem>> GetAllAsync()
        {
            var entities = await _connection.Table<FavoriteItemEntity>()
                .Where(x => !x.IsFolder)
                .OrderBy(x => x.Lft)
                .ToListAsync();

            return entities.Select(ToClipboardItem).ToList();
        }

        /// <summary>
        /// 指定した1件を削除する。フォルダの場合はその中身(子孫)も道連れに削除する
        /// (画像ファイルも含む)。残った木は詰めて振り直す。
        /// </summary>
        public async Task DeleteNodeAsync(int nodeId)
        {
            var entities = await LoadAllAsync();
            var (roots, byId) = BuildForest(entities);

            if (!byId.TryGetValue(nodeId, out var node))
                return;

            var toDelete = new List<FavoriteItemEntity>();
            void Collect(TreeNode n)
            {
                toDelete.Add(n.Entity);
                foreach (var child in n.Children)
                    Collect(child);
            }
            Collect(node);

            foreach (var entity in toDelete)
            {
                DeleteImageFileIfExists(entity.ImageFilePath);
                DeleteImageFileIfExists(entity.ThumbnailFilePath);
                DeleteImageFileIfExists(entity.RawFormatsFilePath);
            }

            var siblings = node.Entity.ParentId is int parentId && byId.TryGetValue(parentId, out var parent)
                ? parent.Children
                : roots;
            siblings.Remove(node);

            var remaining = entities.Except(toDelete).ToList();
            Renumber(roots);

            await _connection.RunInTransactionAsync(conn =>
            {
                foreach (var entity in toDelete)
                    conn.Delete(entity);
                foreach (var entity in remaining)
                    conn.Update(entity);
            });
        }

        /// <summary>単一項目の削除(IClipboardItemListViewModel.DeleteAsync用)。フォルダなら中身ごと削除する。</summary>
        public async Task DeleteAsync(ClipboardItem item)
        {
            if (item.Id == 0)
                return;

            await DeleteNodeAsync(item.Id);
        }

        private async Task<List<FavoriteItemEntity>> LoadAllAsync()
        {
            return await _connection.Table<FavoriteItemEntity>().ToListAsync();
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
                // 削除に失敗しても、お気に入りの削除自体は続行する(孤立ファイルが残るだけで実害は小さい)
            }
        }

        private static ClipboardItem ToClipboardItem(FavoriteItemEntity x) => new()
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
            Name = x.IsFolder ? null : x.Name
        };
    }
}
