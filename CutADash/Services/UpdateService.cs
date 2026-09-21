using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace CutADash.Services
{
    /// <summary>
    /// Velopackを使ったGitHub Releases経由の自動更新。CutADashは非パッケージアプリ
    /// (MSIX/ストア不使用)のため、Velopackの自己更新機構(差分ダウンロード+再起動)に乗せる。
    ///
    /// App()コンストラクタの最初でVelopackApp.Build().Run()を呼ぶ必要がある
    /// (インストール/アンインストール/更新後の再起動時に渡ってくる特殊な起動引数を
    /// このタイミングで処理させるため。それより後だと正しく動かない)。
    /// </summary>
    public sealed class UpdateService
    {
        private const string GitHubRepoUrl = "https://github.com/KOUKiXYZZY/CutADash";

        private readonly UpdateManager _manager = new(new GithubSource(GitHubRepoUrl, null, false));

        /// <summary>アプリ起動直後、バックグラウンドで更新の有無だけ確認する(ダウンロード・適用はしない)。</summary>
        public async Task<bool> CheckForUpdatesQuietlyAsync()
        {
            try
            {
                if (!_manager.IsInstalled)
                    return false; // 開発環境(Velopackでインストールされていない)では何もしない

                var updateInfo = await _manager.CheckForUpdatesAsync();
                return updateInfo is not null;
            }
            catch (Exception ex)
            {
                // オフライン等でのチェック失敗はアプリの起動を妨げてはいけないため、
                // ログにだけ残して握りつぶす
                Debug.WriteLine($"[UpdateService] 更新チェックに失敗しました: {ex}");
                return false;
            }
        }

        /// <summary>
        /// 更新を確認し、あればダウンロードして適用後、アプリを再起動する。
        /// タスクトレイの「アップデートを確認」から呼ばれる、ユーザー操作起点の経路。
        /// </summary>
        /// <returns>更新が無かった場合はfalse(呼び出し元がその旨を表示できるように)。</returns>
        public async Task<bool> CheckDownloadAndApplyAsync()
        {
            if (!_manager.IsInstalled)
                return false;

            var updateInfo = await _manager.CheckForUpdatesAsync();
            if (updateInfo is null)
                return false;

            await _manager.DownloadUpdatesAsync(updateInfo);

            // プロセスはここで終了し、新バージョンが再起動される
            _manager.ApplyUpdatesAndRestart(updateInfo);
            return true;
        }
    }
}
