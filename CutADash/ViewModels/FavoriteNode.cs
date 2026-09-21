using CommunityToolkit.Mvvm.ComponentModel;
using CutADash.Models;
using System;
using System.Collections.ObjectModel;

namespace CutADash.ViewModels
{
    /// <summary>
    /// FavoriteListFrameのTreeViewが表示する1ノード。フォルダかアイテムかのどちらかを表す。
    /// FavoriteRepository.GetTreeEntitiesAsync()が返すフラットな行(Lft順)から、
    /// FavoriteListViewModelが親子関係を組み立てて生成する。
    /// </summary>
    public partial class FavoriteNode : ObservableObject
    {
        public int Id { get; }
        public bool IsFolder { get; }

        /// <summary>フォルダ名(IsFolder=trueの時のみ意味を持つ)。名前変更で書き換わる。</summary>
        [ObservableProperty]
        private string name = string.Empty;

        /// <summary>アイテムの中身(IsFolder=falseの時のみnullでない)。</summary>
        public ClipboardItem? Item { get; }

        public int? ParentId { get; }

        public ObservableCollection<FavoriteNode> Children { get; } = new();

        /// <summary>
        /// TreeViewでの展開状態(IsFolder=trueの時のみ意味を持つ)。既定は展開。
        /// XAML側でTreeViewItem.IsExpandedとTwoWayバインディングしており、ユーザーが
        /// 開閉するたびにOnIsExpandedChangedが発火し、コンストラクタで受け取った
        /// onExpandedChangedコールバック経由でFavoriteRepositoryへ即時保存する。
        /// </summary>
        [ObservableProperty]
        private bool isExpanded = true;

        private readonly Action<int, bool>? _onExpandedChanged;

        // TreeView.ItemTemplateSelector/ContentControl.ContentTemplateSelectorのどちらも
        // 実機で正しく描画されなかった(WinUIの不具合)ため、1つのDataTemplate内で
        // これらのbool値をVisibilityへ変換して出し分ける方式にしている
        public bool IsTextItem => !IsFolder && Item?.Type is null or ClipboardContentType.Text or ClipboardContentType.Unknown;
        public bool IsImageItem => !IsFolder && Item?.Type == ClipboardContentType.Image;
        public bool IsFilesItem => !IsFolder && Item?.Type == ClipboardContentType.Files;

        /// <summary>フォルダではなく実際の項目(コンテキストメニューの表示切り替えに使う)。</summary>
        public bool IsItem => !IsFolder;

        public FavoriteNode(int id, bool isFolder, string name, ClipboardItem? item, int? parentId,
            bool isExpanded = true, Action<int, bool>? onExpandedChanged = null)
        {
            Id = id;
            IsFolder = isFolder;
            this.name = name;
            Item = item;
            ParentId = parentId;
            // フィールドへ直接代入し、セッター(OnIsExpandedChanged)を経由しない。
            // DBから読み込んだ初期値をセットしただけなのに、無駄な保存が走るのを避けるため
            this.isExpanded = isExpanded;
            _onExpandedChanged = onExpandedChanged;
        }

        partial void OnIsExpandedChanged(bool value)
        {
            if (IsFolder)
                _onExpandedChanged?.Invoke(Id, value);
        }
    }
}
