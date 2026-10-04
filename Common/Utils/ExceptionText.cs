using System;
using System.Globalization;

namespace Common.Utils
{
    /// <summary>
    /// ログに書き出す例外の文字列を、英語で得るためのヘルパー。
    /// 例外のメッセージは、実行時のUIカルチャ(日本語Windowsなら日本語)で取得されることが
    /// あるため、書き出す間だけ英語(en-US)に切り替える。
    /// ただし、例外が作られた時点でOSが返した文字列(COMExceptionのメッセージ等)は
    /// 作成時の言語のまま変わらない。
    /// </summary>
    public static class ExceptionText
    {
        public static string ToEnglishString(Exception? ex)
        {
            var previous = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
                return ex?.ToString() ?? "(null)";
            }
            finally
            {
                CultureInfo.CurrentUICulture = previous;
            }
        }
    }
}
