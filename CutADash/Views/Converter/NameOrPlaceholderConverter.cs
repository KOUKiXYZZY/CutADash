using Microsoft.UI.Xaml.Data;
using System;

namespace CutADash.Views.Converter
{
    /// <summary>
    /// お気に入りのShape/Image項目のユーザー付与名を表示用テキストへ変換する。
    /// 未設定(null/空文字)なら「名称未設定」にする。
    /// </summary>
    public class NameOrPlaceholderConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var name = value as string;
            return string.IsNullOrWhiteSpace(name) ? "名称未設定" : name;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotSupportedException();
    }
}
