using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CutADash.Models;
using CutADash.Repositories;
using CutADash.Repositories.Data.Entities;
using SequentialPaste;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace CutADash.ViewModels
{
    /// <summary>ListFrameの「お気に入りに追加」サブメニュー用の選択肢1件。</summary>
    public sealed record FolderChoice(int Id, string Name, int Depth);

    /// <summary>
    /// Favoriteタブ用のViewModel。フォルダを含む階層構造(入れ子集合モデル)を
    /// FavoriteRepositoryから読み込み、FavoriteNodeのツリーとして保持する。
    ///
    /// 検索中はフォルダを無視し、全アイテムをフラットにテキスト検索した結果を
    /// DisplayItemsへ出す(ツリー表示とは別に、旧来のフラット一覧と同じ扱いで良いため)。
    /// </summary>
    public partial class FavoriteListViewModel : ObservableObject, IClipboardItemListViewModel
    {
        private readonly FavoriteRepository _repository;
        private readonly SequentialPasteQueueViewModel _sequentialPasteQueue;
        private readonly Dictionary<int, FavoriteNode> _nodesById = new();

        /// <summary>ツリー表示のルート。FavoriteListFrame側がこれをTreeView.RootNodesへ変換する。</summary>
        public ObservableCollection<FavoriteNode> RootNodes { get; } = new();

        // 検索用。フォルダを含まない全アイテムをフラットに保持し、SearchTextで絞り込む
        private List<ClipboardItem> _allItemsFlat = new();

        /// <summary>検索結果(アイテムのみ、フォルダは出さない)。</summary>
        public ObservableCollection<ClipboardItem> DisplayItems { get; } = new();

        [ObservableProperty]
        private string searchText = string.Empty;

        public bool IsSearching => !string.IsNullOrWhiteSpace(SearchText);

        /// <summary>現在表示中のビュー(ツリー or 検索結果)が空かどうか。「なし」表示の切り替えに使う。</summary>
        public bool IsCurrentViewEmpty => IsSearching ? DisplayItems.Count == 0 : RootNodes.Count == 0;

        /// <summary>
        /// ツリーの読み込み(初回・再読み込みとも)が終わるたびに発火する。
        /// FavoriteListFrame側が、最後に選択していたフォルダの選択復元に使う。
        /// </summary>
        public event Action? TreeLoaded;

        public FavoriteListViewModel(FavoriteRepository repository, SequentialPasteQueueViewModel sequentialPasteQueue)
        {
            _repository = repository;
            _sequentialPasteQueue = sequentialPasteQueue;
            DisplayItems.CollectionChanged += (s, e) => OnPropertyChanged(nameof(IsCurrentViewEmpty));
            RootNodes.CollectionChanged += (s, e) => OnPropertyChanged(nameof(IsCurrentViewEmpty));

            _ = LoadTreeAsync();
        }

        partial void OnSearchTextChanged(string value)
        {
            OnPropertyChanged(nameof(IsSearching));

            DisplayItems.Clear();
            if (string.IsNullOrWhiteSpace(value))
            {
                OnPropertyChanged(nameof(IsCurrentViewEmpty));
                return;
            }

            foreach (var item in _allItemsFlat.Where(x => x.Text?.Contains(value, StringComparison.OrdinalIgnoreCase) == true))
                DisplayItems.Add(item);

            OnPropertyChanged(nameof(IsCurrentViewEmpty));
        }

        /// <summary>
        /// DBの状態からツリーを読み直す。ドラッグ&amp;ドロップでTreeViewが既に見た目上の
        /// 移動を反映してしまった後、その移動が不正(フォルダを自分の子孫へ移動する等)と
        /// 分かった場合に、見た目を実際の状態へ戻すために使う。
        /// </summary>
        public Task ReloadAsync() => LoadTreeAsync();

        /// <summary>
        /// TreeViewでフォルダを開閉した時(FavoriteNode.IsExpandedのTwoWayバインディング経由)に
        /// 呼ばれ、即座にDBへ保存する。再起動時にツリーをDBから読み直す際、
        /// 直前の開閉状態がそのまま復元される。
        /// </summary>
        private void OnNodeExpandedChanged(int id, bool isExpanded) => _ = _repository.SetFolderExpandedAsync(id, isExpanded);

        /// <summary>DBからツリー全体を読み直し、RootNodes/検索用フラット一覧を作り直す。</summary>
        private async Task LoadTreeAsync()
        {
            var entities = await _repository.GetTreeEntitiesAsync();

            _nodesById.Clear();
            RootNodes.Clear();

            var childrenByParent = new Dictionary<int, List<FavoriteNode>>();
            var roots = new List<FavoriteNode>();

            foreach (var entity in entities)
            {
                var item = entity.IsFolder ? null : ToClipboardItem(entity);
                var node = new FavoriteNode(entity.Id, entity.IsFolder, entity.Name ?? string.Empty, item, entity.ParentId,
                    isExpanded: entity.IsExpanded, onExpandedChanged: OnNodeExpandedChanged);
                _nodesById[entity.Id] = node;

                if (entity.ParentId is int parentId)
                {
                    if (!childrenByParent.TryGetValue(parentId, out var list))
                        childrenByParent[parentId] = list = new List<FavoriteNode>();
                    list.Add(node);
                }
                else
                {
                    roots.Add(node);
                }
            }

            // entitiesはLft昇順(=表示順)で来ているため、親ごとに集めた順序もそのまま兄弟順になる
            void AttachChildren(FavoriteNode node)
            {
                if (childrenByParent.TryGetValue(node.Id, out var children))
                {
                    foreach (var child in children)
                    {
                        node.Children.Add(child);
                        AttachChildren(child);
                    }
                }
            }

            foreach (var root in roots)
            {
                RootNodes.Add(root);
                AttachChildren(root);
            }

            _allItemsFlat = _nodesById.Values.Where(n => !n.IsFolder && n.Item is not null).Select(n => n.Item!).ToList();

            if (IsSearching)
                OnSearchTextChanged(SearchText);

            OnPropertyChanged(nameof(IsCurrentViewEmpty));
            TreeLoaded?.Invoke();
        }

        /// <summary>履歴の項目をお気に入りへ複製して追加する(ListFrameの右クリックメニューから呼ばれる)。</summary>
        public async Task AddAsync(ClipboardItem source, int? parentId = null)
        {
            await _repository.AddAsync(source, parentId);
            await LoadTreeAsync();
        }

        /// <summary>ListFrameの「お気に入りに追加」サブメニュー用。既存フォルダを階層深さ付きで返す。</summary>
        public async Task<List<FolderChoice>> GetFolderChoicesAsync()
        {
            var entities = await _repository.GetTreeEntitiesAsync();
            var depthById = new Dictionary<int, int>();
            var choices = new List<FolderChoice>();

            foreach (var entity in entities.Where(x => x.IsFolder))
            {
                var depth = entity.ParentId is int parentId && depthById.TryGetValue(parentId, out var parentDepth)
                    ? parentDepth + 1
                    : 0;
                depthById[entity.Id] = depth;
                choices.Add(new FolderChoice(entity.Id, entity.Name ?? string.Empty, depth));
            }

            return choices;
        }

        /// <summary>「新しいフォルダ」という名前でフォルダを作る。</summary>
        [RelayCommand]
        public async Task CreateFolderAsync(int? parentId)
        {
            await _repository.CreateFolderAsync("新しいフォルダ", parentId);
            await LoadTreeAsync();
        }

        /// <summary>
        /// アイテム(フォルダは対象外)をシーケンシャルペーストのキューへ積む。
        /// キューへの参照(DI経由で注入されたSequentialPasteQueueViewModel)をこちらで持つことで、
        /// View側(FavoriteListFrame)がMainWindowRef.Provider経由でサービスロケーター的に
        /// 取得する必要が無くなる。
        /// </summary>
        [RelayCommand]
        private void AddToSequentialPaste(FavoriteNode? node)
        {
            if (node is not { IsFolder: false, Item: not null })
                return;

            _sequentialPasteQueue.Enqueue(node.Item);
        }

        /// <summary>
        /// アイテムのTextを改行で分割し、空行を除いた各行を個別のClipboardItemとして
        /// シーケンシャルペーストのキューへ積む(Text系項目のみ)。
        /// </summary>
        [RelayCommand]
        private void AddLinesToSequentialPaste(FavoriteNode? node)
        {
            if (node is not { IsTextItem: true, Item.Text: { } text })
                return;

            foreach (var rawLine in text.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                _sequentialPasteQueue.Enqueue(new ClipboardItem
                {
                    Type = ClipboardContentType.Text,
                    Text = line,
                    Timestamp = DateTime.Now,
                    SourceAppName = node.Item.SourceAppName
                });
            }
        }

        /// <summary>フォルダ名を変更する。</summary>
        public async Task RenameFolderAsync(int id, string name)
        {
            if (_nodesById.TryGetValue(id, out var node) && node.IsFolder)
                node.Name = name;

            await _repository.RenameFolderAsync(id, name);
        }

        /// <summary>アイテム(Shape/Image等)の表示名を変更する。</summary>
        public async Task RenameItemAsync(int id, string? name)
        {
            if (_nodesById.TryGetValue(id, out var node) && !node.IsFolder && node.Item is not null)
            {
                node.Item.Name = name;
                // 検索(SearchText)がTextしか見ないため、名前が検索に掛かるようミラーする
                node.Item.Text = name;
            }

            await _repository.RenameItemAsync(id, name);
        }

        /// <summary>FavoriteListFrameで最後に選択していたフォルダのIdを取得する。ルート直下ならnull。</summary>
        public Task<int?> GetSelectedFolderIdAsync() => _repository.GetSelectedFolderIdAsync();

        /// <summary>FavoriteListFrameで最後に選択していたフォルダのIdを記録する。</summary>
        public Task SetSelectedFolderIdAsync(int? folderId) => _repository.SetSelectedFolderIdAsync(folderId);

        /// <summary>ドラッグ&amp;ドロップによる並び替え/フォルダ間移動。</summary>
        public async Task MoveNodeAsync(int nodeId, int? newParentId, int newIndex)
        {
            await _repository.MoveNodeAsync(nodeId, newParentId, newIndex);
            await LoadTreeAsync();
        }

        /// <summary>フォルダ/アイテムを1件削除する(フォルダなら中身ごと)。TreeView側の削除操作から呼ぶ。</summary>
        public async Task DeleteNodeAsync(int nodeId)
        {
            await _repository.DeleteNodeAsync(nodeId);
            await LoadTreeAsync();
        }

        /// <summary>
        /// Idからノードを引く。削除後にフォーカスを移す先を探すため、FavoriteListFrame側が
        /// 兄弟リスト(親フォルダのChildren、ルート直下ならRootNodes)を取得するのに使う。
        /// </summary>
        public FavoriteNode? FindNode(int id) => _nodesById.TryGetValue(id, out var node) ? node : null;

        /// <summary>指定した親の直下の兄弟一覧(表示順)。parentIdがnullならルート直下。</summary>
        public IReadOnlyList<FavoriteNode> GetSiblings(int? parentId)
        {
            if (parentId is int id)
                return (IReadOnlyList<FavoriteNode>?)FindNode(id)?.Children ?? Array.Empty<FavoriteNode>();

            return RootNodes;
        }

        /// <summary>指定した1件をお気に入りから削除する(検索結果一覧からの削除で使う)。履歴側には影響しない。</summary>
        public async Task DeleteAsync(ClipboardItem item)
        {
            DisplayItems.Remove(item);
            await _repository.DeleteAsync(item);
            await LoadTreeAsync();
        }

        /// <summary>
        /// お気に入りは追加した順(ツリー上の位置)で並べる設計のため、ペーストしても
        /// 並び順は変えない(履歴のClipboardListViewModelとは異なり何もしない)。
        /// </summary>
        public Task BumpToTopAsync(ClipboardItem item) => Task.CompletedTask;

        /// <summary>Contentsで編集したテキスト/リッチテキストを、DBと画面上のItemへ反映する。</summary>
        public async Task UpdateContentAsync(ClipboardItem item, string? text, string? rtf, string? html)
        {
            item.Text = text;
            item.Rtf = rtf;
            item.Html = html;

            await _repository.UpdateContentAsync(item.Id, text, rtf, html);
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
            // これが無いと、Excelの図形をお気に入りから貼り付けた時に、OLEオブジェクトでは
            // なく単純な画像になってしまう(ClipboardContentWriterはIsShapeではなく、
            // このパスから生フォーマットを読めるかどうかで復元方法を決めるため)。
            // FavoriteRepository側にも同名の変換があるので、項目を増やす時は両方直すこと
            RawFormatsFilePath = x.RawFormatsFilePath,
            Files = x.FilesJson == null
                ? null
                : System.Text.Json.JsonSerializer.Deserialize<List<string>>(x.FilesJson),
            Timestamp = x.Timestamp,
            SourceAppName = x.SourceAppName,
            Name = x.Name
        };
    }
}
