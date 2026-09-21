using Microsoft.UI.Xaml.Data;
using System;
using System.Collections.Generic;
using System.IO;

namespace CutADash.Views.Converter
{
    /// <summary>クリップボード履歴のFilesを、一覧表示用の短い要約テキストに変換する。</summary>
    public class FilesSummaryConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is not IList<string> files || files.Count == 0)
                return "ファイル";

            var firstName = Path.GetFileName(files[0]);
            return files.Count == 1
                ? firstName
                : $"{firstName} ほか{files.Count - 1}件";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotSupportedException();
    }
}
