using CutADash.Models;
using System.Threading.Tasks;

namespace CutADash.ViewModels
{
    /// <summary>
    /// ListFrameBaseが「削除」を振り分けるために必要な最小限の窓口。
    /// ClipboardListViewModel(履歴)とFavoriteListViewModel(お気に入り)の両方が実装する。
    /// </summary>
    public interface IClipboardItemListViewModel
    {
        Task DeleteAsync(ClipboardItem item);

        /// <summary>
        /// 既存の項目を一覧の先頭へ移動する。ペーストした項目を「一番上に出す」ために使う。
        /// 対応しない一覧(お気に入り等)では何もしなくてよい。
        /// </summary>
        Task BumpToTopAsync(ClipboardItem item);

        /// <summary>
        /// Contents画面で編集したテキスト/リッチテキストを、この項目の永続化先へ書き戻す。
        /// 併せて一覧上のClipboardItemインスタンス(Text/Rtf/Html)も更新し、バッジや表示へ反映する。
        /// htmlは呼び出し側(Contents)がRtfToHtmlConverterで作り直したものを渡す想定。
        /// RTFに対応しないHTML専用アプリへの貼り付けでも、編集後の内容が反映されるようにするため。
        /// </summary>
        Task UpdateContentAsync(ClipboardItem item, string? text, string? rtf, string? html);
    }
}
