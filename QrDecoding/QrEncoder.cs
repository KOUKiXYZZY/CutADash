using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using ZXing.Windows.Compatibility;

namespace QrDecoding
{
    /// <summary>QRコードエンコーダの公開エントリポイント。実体はZXing.Netへの薄いラッパー。</summary>
    public static class QrEncoder
    {
        private const int SizePixels = 512;

        /// <summary>
        /// 文字列をQRコード画像(PNGバイト列)へ変換する。
        /// </summary>
        public static byte[] Encode(string text)
        {
            var writer = new BarcodeWriter
            {
                Format = BarcodeFormat.QR_CODE,
                Options = new QrCodeEncodingOptions
                {
                    Width = SizePixels,
                    Height = SizePixels,
                    Margin = 1,
                    ErrorCorrection = ZXing.QrCode.Internal.ErrorCorrectionLevel.M,
                    // 既定(ISO-8859-1相当)のままだと日本語などの非ASCII文字が正しく
                    // エンコードされない。UTF-8を明示することで、ECIセグメント付きで
                    // 出力され、対応するデコーダ側で文字化けせず読み取れるようになる
                    CharacterSet = "UTF-8"
                }
            };

            using var bitmap = writer.Write(text);
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
    }
}
