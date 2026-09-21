using System;
using System.IO;

namespace Common.Utils
{
    /// <summary>
    /// 設定ファイル・DB・ログなどの保存先をまとめるユーティリティ。
    /// カレントディレクトリを使うと、ビルド構成や起動方法によって実行時の
    /// 作業ディレクトリが変わり、毎回別のファイルを見てしまうため、
    /// %LOCALAPPDATA%\CutADash 配下の固定パスを使う。
    /// </summary>
    public static class AppPaths
    {
        // アプリ名がCutADashからCutADashへ変わった(2026-09-20)ため、フォルダ名も
        // 合わせて変更した。旧フォルダにデータが残っている既存ユーザーの履歴・お気に入りが
        // 消えたように見えないよう、新フォルダが無ければ初回だけ丸ごとリネームして引き継ぐ
        private const string OldAppDataFolderName = "CutADash";
        private const string AppDataFolderName = "CutADash";

        public static string GetDataFilePath(string fileName)
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir = Path.Combine(localAppData, AppDataFolderName);

            if (!Directory.Exists(dir))
            {
                var oldDir = Path.Combine(localAppData, OldAppDataFolderName);
                if (Directory.Exists(oldDir))
                {
                    try
                    {
                        Directory.Move(oldDir, dir);
                    }
                    catch
                    {
                        // 移行に失敗しても(他プロセスが掴んでいる等)アプリ自体は起動させたいため、
                        // ここでは無視して新フォルダを作る側にフォールバックする
                    }
                }
            }

            Directory.CreateDirectory(dir);
            return Path.Combine(dir, fileName);
        }
    }
}
