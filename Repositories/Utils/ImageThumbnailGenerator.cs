using System;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace CutADash.Repositories.Utils
{
    /// <summary>
    /// フルサイズの画像バイト列から、DBへ保存する縮小サムネイルを作る。
    /// </summary>
    internal static class ImageThumbnailGenerator
    {
        public static async Task<byte[]?> CreateThumbnailAsync(byte[] originalBytes, uint maxDimension)
        {
            try
            {
                using var sourceStream = new InMemoryRandomAccessStream();
                await sourceStream.WriteAsync(originalBytes.AsBuffer());
                sourceStream.Seek(0);

                var decoder = await BitmapDecoder.CreateAsync(sourceStream);

                // 先頭フレームだけを取得する。GIFのような複数フレームを持つ形式を
                // CreateForTranscodingAsyncでそのままの形式にエンコードしようとすると
                // 失敗しやすいため、常にPNGへ明示的にエンコードする(結果は常に静止画になる)
                var frame = await decoder.GetFrameAsync(0);
                var pixelData = await frame.GetPixelDataAsync();

                var scale = Math.Min(1.0, (double)maxDimension / Math.Max(frame.PixelWidth, frame.PixelHeight));
                var width = (uint)Math.Max(1, frame.PixelWidth * scale);
                var height = (uint)Math.Max(1, frame.PixelHeight * scale);

                using var destStream = new InMemoryRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, destStream);
                encoder.SetPixelData(
                    frame.BitmapPixelFormat,
                    frame.BitmapAlphaMode,
                    frame.PixelWidth,
                    frame.PixelHeight,
                    frame.DpiX,
                    frame.DpiY,
                    pixelData.DetachPixelData());
                encoder.BitmapTransform.ScaledWidth = width;
                encoder.BitmapTransform.ScaledHeight = height;
                encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
                await encoder.FlushAsync();

                var bytes = new byte[destStream.Size];
                await destStream.ReadAsync(bytes.AsBuffer(), (uint)destStream.Size, InputStreamOptions.None);
                return bytes;
            }
            catch
            {
                // サムネイル生成に失敗しても履歴登録自体は続けたいので、諦めてnullを返す
                return null;
            }
        }
    }
}
