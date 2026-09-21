using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace CutADash.Utils
{
    /// <summary>
    /// ペーストの前面化・キー送信がどこまで成功したかを追う診断ログ。
    /// %LOCALAPPDATA%\CutADash\paste_debug.log へ追記する。
    ///
    /// アプリごとの相性問題(Excelのコピーモード中は貼り付けを受け付けない等)の
    /// 切り分けに使うため残してあるが、通常のビルドでは動かしたくない。
    /// WriteにConditional属性を付けてあるので、DEVDEBUG未定義のビルドでは
    /// 呼び出しそのものが引数の評価ごとコンパイラに除去される
    /// (=呼び出し側にifを書かなくてよい)。
    /// </summary>
    internal static class PasteDiagnosticsLog
    {
        private static readonly string FilePath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CutADash", "paste_debug.log");

        private static readonly object Gate = new();

        // ペースト処理の最中にファイルへ書き込むと、その入出力の待ち時間だけ処理が遅くなり、
        // 測りたい対象そのもの(貼り付け先アプリとのタイミング)を変えてしまう。
        // 実際、ログの有無でペーストの成否が変わる状態に陥って原因が追えなくなった。
        // 記録中はメモリに溜めるだけにして、ペーストが終わってからFlushでまとめて書き出す
        private static readonly List<string> Pending = new();

        [Conditional("DEVDEBUG")]
        public static void Write(string message)
        {
            lock (Gate)
            {
                Pending.Add($"{DateTime.Now:HH:mm:ss.fff} {message}");
            }
        }

        /// <summary>溜めたログをファイルへ書き出す。ペーストが完了してから呼ぶこと。</summary>
        [Conditional("DEVDEBUG")]
        public static void Flush()
        {
            try
            {
                string[] lines;
                lock (Gate)
                {
                    if (Pending.Count == 0)
                        return;

                    lines = Pending.ToArray();
                    Pending.Clear();
                }

                File.AppendAllLines(FilePath, lines);
            }
            catch
            {
                // ログの失敗で本来の処理を妨げない
            }
        }

        /// <summary>ウィンドウハンドルを「ハンドル(クラス名: タイトル)」の形で読める文字列にする。</summary>
        public static string Describe(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
                return "0x0(null)";

            var className = new StringBuilder(256);
            GetClassName(hWnd, className, className.Capacity);

            var title = new StringBuilder(256);
            GetWindowText(hWnd, title, title.Capacity);

            return $"0x{hWnd.ToInt64():X}({className}: {title})";
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    }
}
