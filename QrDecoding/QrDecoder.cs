using System.Drawing;
using System.IO;
using ZXing;
using ZXing.Common;
using ZXing.Windows.Compatibility;

namespace QrDecoding
{
    /// <summary>QRコードデコーダの公開エントリポイント。実体はZXing.Netへの薄いラッパー。</summary>
    public static class QrDecoder
    {
        /// <summary>
        /// 画像バイト列(PNG/JPEG/BMP/GIF等)からQRコードを検出して文字列を復号する。
        /// 見つからない/復号できない場合はnullを返す(例外は投げない)。
        /// </summary>
        public static string? Decode(byte[] imageBytes)
        {
            try
            {
                using var stream = new MemoryStream(imageBytes);
                using var bitmap = new Bitmap(stream);

                var reader = new BarcodeReader
                {
                    AutoRotate = true,
                    Options = new DecodingOptions
                    {
                        TryHarder = true,
                        PossibleFormats = new[] { BarcodeFormat.QR_CODE }
                    }
                };

                var result = reader.Decode(bitmap);
                return result?.Text;
            }
            catch
            {
                // 画像が壊れている・対応形式でない等、どんな理由であれ「読み取れなかった」
                // として扱う(呼び出し側にはnullで伝える)
                return null;
            }
        }
    }
}
