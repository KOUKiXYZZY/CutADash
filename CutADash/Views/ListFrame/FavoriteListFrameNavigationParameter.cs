using CutADash.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace CutADash.Views.ListFrame
{
    /// <summary>
    /// MainWindowからFavoriteListFrame(お気に入り一覧)へNavigateする際に渡すパラメータ。
    /// DIで共有されているFavoriteListViewModelを、ContentFrameと合わせて1つの
    /// Navigateパラメータとして渡すためのラッパー(ListFrameNavigationParameterと同じ形)。
    /// </summary>
    public sealed class FavoriteListFrameNavigationParameter
    {
        public required Frame ContentFrame { get; init; }
        public required FavoriteListViewModel ViewModel { get; init; }
        public required MainWindow MainWindow { get; init; }
    }
}
