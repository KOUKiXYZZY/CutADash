using Microsoft.UI.Xaml.Data;
using System;

namespace CutADash.Views.Converter
{
    /// <summary>
    /// クリップボード履歴の画像バッジに表示するテキストを、GIF/Shape/通常画像かどうかで切り替える。
    /// </summary>
    public class ImageBadgeTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return value switch
            {
                CutADash.Models.ClipboardItem { IsGif: true } => "GIF",
                CutADash.Models.ClipboardItem { IsShape: true } => "Shape",
                _ => "Image"
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotSupportedException();
    }
}
