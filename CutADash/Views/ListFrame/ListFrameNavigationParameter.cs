using CutADash.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace CutADash.Views.ListFrame
{
    /// <summary>
    /// MainWindowからListFrame(履歴一覧)へNavigateする際に渡すパラメータ。
    /// DIで共有されているClipboardListViewModelを、ContentFrameと合わせて1つの
    /// Navigateパラメータとして渡すためのラッパー。
    /// </summary>
    public sealed class ListFrameNavigationParameter
    {
        public required Frame ContentFrame { get; init; }
        public required ClipboardListViewModel ViewModel { get; init; }
        public required MainWindow MainWindow { get; init; }
    }
}
