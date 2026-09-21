using Microsoft.UI.Xaml.Data;
using System;

namespace CutADash.Views.Converter
{
    public class TruncateTextConverter : IValueConverter
    {
        private const int MaxLength = 40;

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var text = value as string ?? string.Empty;
            text = text.TrimStart();
            // 先頭が空行(空白のみの行)だと一覧に何も見えなくなるため、
            // 文字列がある最初の行から表示する
            text = SkipLeadingBlankLines(text);

            return text.Length > MaxLength
                ? text[..MaxLength] + "…"
                : text;
        }

        private static string SkipLeadingBlankLines(string text)
        {
            var start = 0;
            while (start < text.Length)
            {
                var newlineIndex = text.IndexOfAny(new[] { '\r', '\n' }, start);
                var lineEnd = newlineIndex < 0 ? text.Length : newlineIndex;

                if (!string.IsNullOrWhiteSpace(text[start..lineEnd]))
                    return text[start..];

                if (newlineIndex < 0)
                    break;

                start = text[newlineIndex] == '\r' && newlineIndex + 1 < text.Length && text[newlineIndex + 1] == '\n'
                    ? newlineIndex + 2
                    : newlineIndex + 1;
            }

            return text;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotSupportedException();
    }
}
