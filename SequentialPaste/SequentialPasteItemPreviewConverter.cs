using CutADash.Models;
using Microsoft.UI.Xaml.Data;
using System;

namespace SequentialPaste
{
    /// <summary>
    /// キュー内の1件を、種別込みの短い1行プレビューへ変換する。CutADash本体には
    /// サムネイル付きの専用Converter/テンプレートセレクタがあるが、このプロジェクトから
    /// 参照すると循環参照になるため、ここでは種別ラベル+テキストだけの簡易表示にする。
    /// </summary>
    public sealed class SequentialPasteItemPreviewConverter : IValueConverter
    {
        private const int MaxLength = 60;

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is not ClipboardItem item)
                return string.Empty;

            return item.Type switch
            {
                ClipboardContentType.Image => item.IsShape ? "[図形]" : "[画像]",
                ClipboardContentType.Files => item.Files is { Count: > 0 }
                    ? $"[ファイル] {string.Join(", ", item.Files)}"
                    : "[ファイル]",
                _ => Truncate(item.Text ?? string.Empty)
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotSupportedException();

        private static string Truncate(string text)
        {
            var singleLine = text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
            return singleLine.Length > MaxLength ? singleLine[..MaxLength] + "…" : singleLine;
        }
    }
}
