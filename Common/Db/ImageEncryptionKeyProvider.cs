using Common.Utils;
using System;
using System.IO;
using System.Security.Cryptography;

namespace Common.Db
{
    /// <summary>
    /// 画像ファイル(ClipboardImages/FavoriteImages配下)の暗号化(AES-256-GCM)用の鍵を管理する。
    /// 仕組みはDbEncryptionKeyProvider(DBのSQLCipher用鍵)と同じだが、ファイルと用途を分けるため
    /// 別の鍵として持つ。ランダムな32バイトを、DPAPI(CurrentUserスコープ)で保護して保存する。
    /// </summary>
    public static class ImageEncryptionKeyProvider
    {
        private const int KeyLengthBytes = 32;
        private const string KeyFileName = "image.key";

        private static readonly byte[] Entropy = System.Text.Encoding.UTF8.GetBytes("CutADash.Image.v1");

        /// <summary>AES-256-GCMに渡す生の鍵バイト列を取得する。無ければ新規生成して保存する。</summary>
        public static byte[] GetOrCreateKey()
        {
            var path = AppPaths.GetDataFilePath(KeyFileName);

            if (File.Exists(path))
            {
                var protectedBytes = File.ReadAllBytes(path);
                return ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            }

            var newKeyBytes = RandomNumberGenerator.GetBytes(KeyLengthBytes);
            var newProtectedBytes = ProtectedData.Protect(newKeyBytes, Entropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(path, newProtectedBytes);

            return newKeyBytes;
        }
    }
}
