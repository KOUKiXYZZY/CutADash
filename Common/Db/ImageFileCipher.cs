using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Common.Db
{
    /// <summary>
    /// ClipboardImages/FavoriteImages配下の画像ファイルを、AES-256-GCMで暗号化/復号する。
    /// ファイル形式は [12バイトnonce][暗号文][16バイトタグ] を連結しただけの単純なもの。
    /// 鍵はImageEncryptionKeyProviderがDPAPIで管理する。
    /// </summary>
    public static class ImageFileCipher
    {
        private const int NonceSizeBytes = 12;
        private const int TagSizeBytes = 16;

        public static byte[] Encrypt(byte[] plaintext)
        {
            var key = ImageEncryptionKeyProvider.GetOrCreateKey();
            var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[TagSizeBytes];

            using var aesGcm = new AesGcm(key, TagSizeBytes);
            aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);

            var result = new byte[NonceSizeBytes + ciphertext.Length + TagSizeBytes];
            Buffer.BlockCopy(nonce, 0, result, 0, NonceSizeBytes);
            Buffer.BlockCopy(ciphertext, 0, result, NonceSizeBytes, ciphertext.Length);
            Buffer.BlockCopy(tag, 0, result, NonceSizeBytes + ciphertext.Length, TagSizeBytes);
            return result;
        }

        public static byte[] Decrypt(byte[] fileBytes)
        {
            var key = ImageEncryptionKeyProvider.GetOrCreateKey();

            var nonce = new byte[NonceSizeBytes];
            Buffer.BlockCopy(fileBytes, 0, nonce, 0, NonceSizeBytes);

            var tag = new byte[TagSizeBytes];
            Buffer.BlockCopy(fileBytes, fileBytes.Length - TagSizeBytes, tag, 0, TagSizeBytes);

            var ciphertextLength = fileBytes.Length - NonceSizeBytes - TagSizeBytes;
            var ciphertext = new byte[ciphertextLength];
            Buffer.BlockCopy(fileBytes, NonceSizeBytes, ciphertext, 0, ciphertextLength);

            var plaintext = new byte[ciphertextLength];
            using var aesGcm = new AesGcm(key, TagSizeBytes);
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
            return plaintext;
        }

        /// <summary>
        /// PNG/GIFの先頭マジックバイトかどうかで、平文の画像ファイル(旧形式・未暗号化)か
        /// どうかを判定する。暗号化済みファイルはランダムなnonceで始まるため、
        /// 偶然一致する確率は無視できるほど小さい。
        /// </summary>
        public static bool LooksLikePlainImage(byte[] fileBytes)
        {
            if (fileBytes.Length >= 8 &&
                fileBytes[0] == 0x89 && fileBytes[1] == 0x50 && fileBytes[2] == 0x4E && fileBytes[3] == 0x47)
                return true; // PNG

            if (fileBytes.Length >= 3 &&
                fileBytes[0] == (byte)'G' && fileBytes[1] == (byte)'I' && fileBytes[2] == (byte)'F')
                return true; // GIF

            return false;
        }

        public static async Task WriteEncryptedFileAsync(string path, byte[] plaintext)
        {
            var encrypted = Encrypt(plaintext);
            await File.WriteAllBytesAsync(path, encrypted);
        }

        public static async Task<byte[]> ReadDecryptedFileAsync(string path)
        {
            var fileBytes = await File.ReadAllBytesAsync(path);
            return Decrypt(fileBytes);
        }
    }
}
