using Microsoft.UI.Xaml.Data;
using System;

namespace SequentialPaste
{
    /// <summary>
    /// キューの件数(int)を「N件」という表示用の文言へ変換する。件数はViewModelが持つが、
    /// 表示文言の組み立て自体はView側の責務としてここに置く。
    /// </summary>
    public sealed class CountTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => value is int count ? $"{count}件" : string.Empty;

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// IsWatching(bool)を開始/停止ボタンの表示文言へ変換する。
    /// </summary>
    public sealed class WatchingToButtonTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => value is true ? "停止" : "開始";

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotSupportedException();
    }
}
