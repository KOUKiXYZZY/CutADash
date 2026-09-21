using Common.Utils;
using System;
using System.IO;
using System.Security.Cryptography;

namespace Common.Db
{
    /// <summary>
    /// DB暗号化(SQLCipher)用の鍵を管理する。鍵そのものはランダムな32バイトで、
    /// ファイルにはWindows DPAPI(現在のユーザーアカウントに紐づく、CurrentUserスコープ)で
    /// 保護した状態で保存する。パスワード入力なしで、このPC・このユーザーでだけ復号できる。
    /// CutADash(通常起動時)とMigration.exe(マイグレーション実行時)の両方から
    /// 同じ鍵を参照できるよう、ここに集約する。
    /// </summary>
    public static class DbEncryptionKeyProvider
    {
        private const int KeyLengthBytes = 32;
        private const string KeyFileName = "db.key";

        // DPAPIのCurrentUserスコープに追加で混ぜるエントロピー。他アプリが同じスコープで
        // 保護したデータと混同しないようにするためのもので、秘匿する必要は無い
        private static readonly byte[] Entropy = System.Text.Encoding.UTF8.GetBytes("CutADash.Db.v1");

        /// <summary>
        /// SQLCipherに渡す鍵(16進文字列)を取得する。無ければ新規生成して保存する。
        /// </summary>
        public static string GetOrCreateKey()
        {
            var path = AppPaths.GetDataFilePath(KeyFileName);

            if (File.Exists(path))
            {
                var protectedBytes = File.ReadAllBytes(path);
                var keyBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
                return Convert.ToHexString(keyBytes);
            }

            var newKeyBytes = RandomNumberGenerator.GetBytes(KeyLengthBytes);
            var newProtectedBytes = ProtectedData.Protect(newKeyBytes, Entropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(path, newProtectedBytes);

            return Convert.ToHexString(newKeyBytes);
        }
    }
}
