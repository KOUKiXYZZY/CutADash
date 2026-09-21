using CommunityToolkit.Mvvm.ComponentModel;
using CutADash.Models;
using CutADash.Repositories;
using CutADash.Views.Emoji;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace CutADash.ViewModels
{
    /// <summary>
    /// EmojiListFrame(カテゴリ一覧)のViewModel。emoji.dbに実際に登録されているKindを
    /// カテゴリとして読み込む。全件表示は件数が多く重いため「全部」は置かない。
    /// </summary>
    public partial class EmojiListViewModel : ObservableObject
    {
        private readonly EmojiRepository _repository;

        public ObservableCollection<EmojiCategoryItem> Categories { get; } = new();

        public EmojiListViewModel(EmojiRepository repository)
        {
            _repository = repository;
            _ = LoadCategoriesAsync();
        }

        private async Task LoadCategoriesAsync()
        {
            var kinds = await _repository.GetDistinctKindsAsync();

            Categories.Clear();
            foreach (var kind in kinds)
                Categories.Add(new EmojiCategoryItem(kind, EmojiKindLocalizer.GetDisplayName(kind)));
        }
    }
}
