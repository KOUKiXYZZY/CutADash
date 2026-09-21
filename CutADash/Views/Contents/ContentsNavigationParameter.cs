using CutADash.Models;
using CutADash.ViewModels;

namespace CutADash.Views.Contents
{
    /// <summary>
    /// Contentsページへ渡すパラメータ。表示する項目に加えて、
    /// エンコード/デコード結果を直前のフォアグラウンドウィンドウへペーストする際に使う
    /// MainWindow参照、および編集を保存する先(History/Favoriteどちらの一覧から
    /// 開かれたか)を示すOwnerViewModelを合わせて渡す。
    /// </summary>
    public sealed class ContentsNavigationParameter
    {
        public ClipboardItem? Item { get; init; }
        public required MainWindow MainWindow { get; init; }
        public IClipboardItemListViewModel? OwnerViewModel { get; init; }
    }
}
