using CutADash.Models;
using CutADash.Repositories;
using CutADash.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using CutADash.Utils;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Threading.Tasks;

namespace CutADash.Views.ListFrame
{
    /// <summary>
    /// emojiの種類(カテゴリ)だけを一覧表示するListFrame側のページ。
    /// カテゴリの読み込みはEmojiListViewModelが担い、ここではNavigate/画面遷移のみを扱う。
    /// J/K移動・フォーカス外での選択保持等の共通動作はListFrameBaseに任せる。
    /// </summary>
    public sealed partial class EmojiListFrame : ListFrameBase
    {
        private Frame? _contentFrame;
        private MainWindow? _mainWindow;

        // 基底のSelectedItem(object?)を、EmojiCategoryItem専用の型として扱うための隠蔽プロパティ
        private new EmojiCategoryItem? SelectedItem
        {
            get => base.SelectedItem as EmojiCategoryItem;
            set => base.SelectedItem = value;
        }

        protected override ListView ItemsListView => CategoryListView;

        public EmojiListFrame()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is EmojiListFrameNavigationParameter param)
            {
                _contentFrame = param.ContentFrame;
                _mainWindow = param.MainWindow;
            }
            else
            {
                _contentFrame = e.Parameter as Frame;
            }

            ContentFrame = _contentFrame;
            MainWindowRef = _mainWindow;

            var repository = _mainWindow?.Provider?.GetService<EmojiRepository>();
            if (repository is not null)
            {
                var viewModel = new EmojiListViewModel(repository);
                DataContext = viewModel;

                // タブを開いた(切り替えた)時点で一番上のカテゴリを選んでおく。
                // Categoriesの読み込みは非同期なので、最初に1件でも入った時点で選ぶ
                viewModel.Categories.CollectionChanged += SelectFirstCategoryOnceLoaded;
            }
        }

        private void SelectFirstCategoryOnceLoaded(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (sender is not System.Collections.ObjectModel.ObservableCollection<EmojiCategoryItem> categories
                || categories.Count == 0)
            {
                return;
            }

            categories.CollectionChanged -= SelectFirstCategoryOnceLoaded;
            CategoryListView.SelectedIndex = 0;
        }

        // J(下)/K(上)でカテゴリを移動する。ListFrame/FavoriteListFrameと同じ扱い
        private void CategoryListView_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (sender is ListView listView && HandleVimUpDownKey(e.Key, listView))
                e.Handled = true;
        }

        private async void EmojiListFrame_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // ClearSelectionIfFocusOutsideによる見た目だけの選択解除(何も追加されていない)では、
            // 表示中のカテゴリを巻き込みたくないため、実際に選択が変わった場合だけ反映する
            if (e.AddedItems.Count == 0)
                return;

            if ((sender as ListView)?.SelectedItem is EmojiCategoryItem item)
            {
                SelectedItem = item;
                await SwitchCategoryAsync(item);
            }
        }

        // カテゴリを選ぶたびにNavigateし直すと、その都度Emojiページ(+EmojiGridViewModel)が
        // 新しく作り直され、古いインスタンスがGCされるまでメモリに残ってしまう。
        // 既にEmojiページが表示されていればCategoryを差し替えるだけにする。
        private async Task SwitchCategoryAsync(EmojiCategoryItem item)
        {
            // スプライトシートのキャッシュはEmojiSpriteAssets側が「現在のカテゴリ1つ分」だけを
            // 自動的に保持する(切り替わったら前のカテゴリぶんを自動で解放する)ため、
            // ここで明示的にキャッシュを操作する必要はない
            if (_contentFrame?.Content is Emoji.Emoji existingEmoji && existingEmoji.ViewModel is not null)
            {
                await existingEmoji.ShowCategoryAsync(item.Key);
                return;
            }

            object? parameter = _mainWindow is not null
                ? new Emoji.EmojiNavigationParameter { Category = item.Key, CategorySpecified = true, MainWindow = _mainWindow }
                : item.Key;

            _contentFrame?.NavigateWithoutAnimation(typeof(Emoji.Emoji), parameter);
            _contentFrame?.BackStack.Clear();
        }
    }
}
