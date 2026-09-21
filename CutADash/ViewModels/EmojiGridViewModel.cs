using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CutADash.Models;
using CutADash.Repositories;
using CutADash.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace CutADash.ViewModels
{
    /// <summary>コンパクト表示のカテゴリタブ1件ぶん(アイコン代わりに代表絵文字を持つ)。</summary>
    public sealed class EmojiCategoryTab
    {
        public required string Kind { get; init; }
        public required string DisplayName { get; init; }
        public required string RepresentativeEmoji { get; init; }
    }

    /// <summary>
    /// Emojiページ(絵文字グリッド)のViewModel。選択中のカテゴリ(Kind)に応じて
    /// EmojiRepository(Assets/Emoji配下のkind別JSON)から絵文字を読み込む。
    /// スクロールではなく明示的なページ送り(PipsPager)で切り替える
    /// (スクロールだと目当ての絵文字を探すのにスクロール量が読めず使いづらかったため)。
    ///
    /// コンパクト表示は、以前は全カテゴリを見出し付きでまとめて表示していたが、
    /// Windowsの絵文字ピッカーのようにアイコンタブで1カテゴリだけを選んで表示する方式に
    /// 変えた(通常表示と同じPage/ページングをそのまま使い、タブでCategoryを切り替えるだけ)。
    /// </summary>
    public partial class EmojiGridViewModel : ObservableObject
    {
        private readonly EmojiRepository _repository;

        // ペースト先(直前のフォアグラウンドウィンドウ)を得るために必要。
        // Emoji.xaml.csがOnNavigatedTo時点でしか受け取れないため、コンストラクタで受ける
        private readonly Views.MainWindow? _mainWindow;

        [ObservableProperty]
        private string? category;

        // 現在のページぶんだけの絵文字(通常表示/コンパクト表示で共用)
        public ObservableCollection<EmojiItem> Page { get; } = new();

        // コンパクト表示のカテゴリタブ(アイコン代わりに各Kindの代表絵文字を持つ)
        public ObservableCollection<EmojiCategoryTab> CompactCategories { get; } = new();

        private bool _compactCategoriesLoaded;

        public EmojiGridViewModel(EmojiRepository repository, Views.MainWindow? mainWindow)
        {
            _repository = repository;
            _mainWindow = mainWindow;
        }

        /// <summary>
        /// 絵文字1件をOSクリップボードへ書き戻し、直前のフォアグラウンドウィンドウへ
        /// ペーストする(History一覧のEnter/ダブルクリックと同じForegroundPasteHelperに任せる)。
        /// クリック・Enter/Space・低レベルフック経由のEnter、すべてここに集約する。
        /// </summary>
        [RelayCommand]
        private Task PasteAsync(EmojiItem? item)
        {
            if (item is null)
                return Task.CompletedTask;

            var clipboardItem = new ClipboardItem
            {
                Type = ClipboardContentType.Text,
                Text = item.Emoji,
                Timestamp = DateTime.Now
            };
            return ForegroundPasteHelper.PasteToPreviousWindowAsync(_mainWindow, clipboardItem);
        }

        // ビューポートの大きさが取れない(レイアウト前など)場合に使う件数
        private const int FallbackPageSize = 48;

        // 現在のカテゴリの全件。Pageへは現在のページぶんだけを移す
        private List<EmojiItem> _source = new();

        private int _pageSize = FallbackPageSize;

        [ObservableProperty]
        private int pageIndex;

        /// <summary>現在のカテゴリの総ページ数(最低1)。</summary>
        public int PageCount => Math.Max(1, (int)Math.Ceiling(_source.Count / (double)Math.Max(1, _pageSize)));

        // Categoryのsetter自身では読み込みを開始しない。呼び出し側が明示的に
        // LoadEmojisAsyncをawaitすること
        /// <param name="pageSize">
        /// 1ページに収まる件数。呼び出し側がビューポートの大きさから算出して渡す。
        /// 0以下ならレイアウト前とみなして既定値を使う。
        /// </param>
        public async Task LoadEmojisAsync(int pageSize = 0)
        {
            _pageSize = pageSize > 0 ? pageSize : FallbackPageSize;

            _source = Category is null
                ? await _repository.GetAllAsync()
                : await _repository.GetByKindAsync(Category);

            SetPage(0);
        }

        /// <summary>
        /// 1ページぶんの件数だけを変えて、現在のページ位置を保ったまま組み直す
        /// (ウィンドウリサイズで列数/行数が変わった時に呼ぶ)。
        /// </summary>
        public void Relayout(int pageSize)
        {
            _pageSize = pageSize > 0 ? pageSize : FallbackPageSize;
            SetPage(PageIndex);
        }

        /// <summary>
        /// 表示ページを切り替える。範囲外の指定は範囲内に丸める。
        /// PipsPagerのクリック・マウスホイールのどちらからもこのCommand経由で呼ぶ
        /// (呼び出し側は絶対ページ番号を渡すだけで、実際の切り替え・件数計算はここに集約する)。
        /// </summary>
        [RelayCommand]
        public void SetPage(int pageIndex)
        {
            var clamped = Math.Clamp(pageIndex, 0, PageCount - 1);
            PageIndex = clamped;

            var start = clamped * _pageSize;
            var end = Math.Min(start + _pageSize, _source.Count);

            Page.Clear();
            for (var i = start; i < end; i++)
                Page.Add(_source[i]);

            OnPropertyChanged(nameof(PageCount));

            // GoToNextPage/GoToPreviousPageのCanExecuteが範囲(PageIndex/PageCount)に
            // 依存するため、ページが変わるたびに再評価させる
            GoToNextPageCommand.NotifyCanExecuteChanged();
            GoToPreviousPageCommand.NotifyCanExecuteChanged();
        }

        /// <summary>
        /// マウスホイールでの1ページ送り(MouseWheelPagingBehaviorから呼ぶ)。
        /// 最後のページでは実行不可(CanExecute=false)にし、境界を超えて回し続けても
        /// 同じ内容でPage(ObservableCollection)を無駄に作り直さないようにする。
        /// 以前はSetPage側でガードしていたが、通常のページ送りまで巻き込んで動かなくなる
        /// 不具合を起こしたため、Command自体を実行不可にする形に直した
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanGoToNextPage))]
        private void GoToNextPage() => SetPage(PageIndex + 1);

        private bool CanGoToNextPage() => PageIndex < PageCount - 1;

        /// <summary>マウスホイールでの1ページ戻し(MouseWheelPagingBehaviorから呼ぶ)。最初のページでは実行不可。</summary>
        [RelayCommand(CanExecute = nameof(CanGoToPreviousPage))]
        private void GoToPreviousPage() => SetPage(PageIndex - 1);

        private bool CanGoToPreviousPage() => PageIndex > 0;

        /// <summary>
        /// コンパクト表示のカテゴリタブ一覧を読み込む(初回のみDBから読み込み、以降はキャッシュを使う)。
        /// 各Kindの先頭の絵文字をタブのアイコン代わりに使う。
        /// </summary>
        public async Task LoadCompactCategoriesAsync()
        {
            if (_compactCategoriesLoaded)
                return;

            var items = await _repository.GetAllAsync();

            CompactCategories.Clear();
            foreach (var group in items.GroupBy(i => i.Kind))
            {
                if (group.Key is null)
                    continue;

                var first = group.FirstOrDefault();
                if (first is null)
                    continue;

                CompactCategories.Add(new EmojiCategoryTab
                {
                    Kind = group.Key,
                    DisplayName = Views.Emoji.EmojiKindLocalizer.GetDisplayName(group.Key),
                    RepresentativeEmoji = first.Emoji,
                });
            }

            _compactCategoriesLoaded = true;
        }
    }
}
