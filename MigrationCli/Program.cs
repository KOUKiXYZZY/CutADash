using System;
using CutADash.Migration;

namespace CutADash.MigrationCli
{
    /// <summary>
    /// 開発時にDBの中身を覗くための道具。配布物には含めない。
    ///
    /// 暗号化されたDBは外部ツールでそのまま開けず、中身を確認する手段が無いと
    /// 不具合の調査が推測頼みになるため、Migrationの処理を手から叩けるようにしてある。
    /// マイグレーション自体はCutADashが起動時に自動で行うので、通常は使わない。
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            var command = args.Length > 0 ? args[0] : "--help";

            switch (command)
            {
                // 暗号化DBを"DB Browser for SQLCipher"等で開くための鍵(16進文字列)
                case "--print-key":
                    Console.WriteLine(Common.Db.DbEncryptionKeyProvider.GetOrCreateKey());
                    return 0;

                // 直近のImage項目が実際にどのクリップボードフォーマットを保存できていたか
                case "--dump-rawformats":
                    Migrator.DumpRawFormats();
                    return 0;

                // 直近のText項目のText/Rtf/Htmlの中身(先頭だけ)。空白項目が登録される不具合の調査用
                case "--dump-text":
                    Migrator.DumpText();
                    return 0;

                // ClipboardItemEntityの全件数と種別ごとの内訳
                case "--count":
                    Migrator.DumpCount();
                    return 0;

                // マイグレーションを手動で実行する
                case "--migrate":
                    return Migrator.Run();

                default:
                    Console.WriteLine("使い方: MigrationCli <コマンド>");
                    Console.WriteLine("  --print-key        暗号化DBを開くための鍵を表示する");
                    Console.WriteLine("  --dump-text        直近のText項目の中身を表示する");
                    Console.WriteLine("  --dump-rawformats  直近のImage項目の保持フォーマットを表示する");
                    Console.WriteLine("  --count            履歴の件数と種別ごとの内訳を表示する");
                    Console.WriteLine("  --migrate          マイグレーションを手動で実行する");
                    return command == "--help" ? 0 : 1;
            }
        }
    }
}
