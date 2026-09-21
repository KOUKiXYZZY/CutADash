using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace CutADash.Views.Converter
{
    /// <summary>値がnull(または空文字)ならCollapsed、それ以外ならVisibleを返す。</summary>
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var isEmpty = value is null || (value is string s && string.IsNullOrEmpty(s));
            return isEmpty ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotSupportedException();
    }
}
