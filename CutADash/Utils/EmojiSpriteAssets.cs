using CutADash.Repositories;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;

namespace CutADash.Utils
{
    /// <summary>
    /// kindごとに用意した絵文字スプライトシート(Assets/Emoji/{kind}.scale-*.png、10列、
    /// ビルド時にAssetsSrc/EmojiGenerateScript/build_emoji_data.pyで生成済み)を読み込み、キャッシュする。
    ///
    /// 画像はMicrosoftのFluent Emoji(microsoft/fluentui-emoji、MITライセンス、
    /// リポジトリ直下にサブモジュールとして追加済み)の3Dスタイルアセットを使用しており、
    /// Segoe UI Emojiフォントのように再配布に制約のあるものではないため、ビルドに
    /// 同梱できる(以前試したフォントからの実行時レンダリング方式は、WinUIの
    /// RenderTargetBitmap周りが不安定だったため取りやめた)。
    /// </summary>
    public static class EmojiSpriteAssets
    {
        public const int Columns = 10;
        public const int VariantColumns = 5;

        /// <summary>1セルの表示サイズ(DIP)。スプライトの実ピクセルサイズ(32/64/96)に
        /// 関わらず、Imageの表示サイズをこれに固定してStretch=Fillで合わせるため、
        /// どのDPI向けファイルを読み込んでもセル位置の計算(Index%Columns等)は変わらない。</summary>
        public const double CellDip = 32;

        private sealed record Entry(BitmapImage Image, double Width, double Height);

        private static readonly Dictionary<string, Entry> _cache = new();

        // 直近で読み込んだkind(カテゴリ)名。カテゴリを跨いだらそれまでのスプライトシートは
        // 不要になるため、キャッシュには常に「現在表示中のカテゴリ」ぶんだけを残す
        // (kindごとに最大96px×数百pxのBitmapImageになるため、複数カテゴリぶん貯め込むと
        // メモリ使用量が際限なく増えていく不具合があった)
        private static string? _cachedKind;

        /// <summary>
        /// キャッシュしているBitmapImageを全て破棄する。CompositionTarget.SurfaceContentsLost
        /// (GPUデバイスロスト等でコンポジション面が失われた時に発生)を受けて、
        /// 絵文字/Navアイコンがときおり表示されなくなる不具合の対策として、
        /// MainWindow側から呼び、次回アクセス時にディスクから読み直させる。
        /// </summary>
        public static void ClearCache()
        {
            _cache.Clear();
            _cachedKind = null;
        }

        /// <summary>
        /// 指定したkindのメインスプライトシートを取得する(初回はディスクから読み込み、以降はキャッシュを返す)。
        /// </summary>
        /// <param name="kind">絵文字の分類(例: "Food &amp; Drink")。</param>
        /// <param name="itemCount">そのkindの絵文字件数。スプライトの行数(=表示サイズ)の算出に使う。</param>
        /// <param name="dpiScale">表示先のDPIスケール(1.0/1.25/1.5/2.0等)。近い解像度のPNGを選ぶ。</param>
        public static (BitmapImage Image, double Width, double Height) GetOrLoad(string kind, int itemCount, double dpiScale)
        {
            // メインシートは10列に折り返して並べているため、行数は件数から割り出す
            var rows = Math.Max(1, (int)Math.Ceiling(itemCount / (double)Columns));
            return GetOrLoadCore(kind, kind, "", Columns, rows, dpiScale);
        }

        /// <summary>
        /// 指定したkindのバリエーション用スプライトシート(5列)を取得する。
        /// </summary>
        /// <param name="kind">絵文字の分類。</param>
        /// <param name="variantRowCount">そのkindでバリエーションを持つ絵文字の件数。</param>
        /// <param name="dpiScale">表示先のDPIスケール。</param>
        public static (BitmapImage Image, double Width, double Height) GetOrLoadVariants(string kind, int variantRowCount, double dpiScale)
        {
            // バリエーションシートは項目1件=1行を直接割り当てている(折り返しではない)ため、
            // 行数はそのままvariantRowCount
            var rows = Math.Max(1, variantRowCount);
            return GetOrLoadCore(kind, kind + "variants", ".variants", VariantColumns, rows, dpiScale);
        }

        private static (BitmapImage Image, double Width, double Height) GetOrLoadCore(
            string kind, string cacheKey, string fileSuffix, int columns, int rows, double dpiScale)
        {
            // 別のkindに切り替わったら、それまでキャッシュしていた画像(メイン+
            // バリエーションシート)は不要になるため先に解放する
            if (_cachedKind != kind)
            {
                _cache.Clear();
                _cachedKind = kind;
            }

            if (_cache.TryGetValue(cacheKey, out var cached))
                return (cached.Image, cached.Width, cached.Height);

            var scale = dpiScale switch
            {
                >= 2.5 => 300,
                >= 1.5 => 200,
                _ => 100,
            };

            // 非パッケージ(unpackaged)アプリではms-appx:///によるパッケージリソース解決が
            // 実行時に効かない(MainWindow.xamlのSizerBase.xaml読み込み失敗と同じ制約)ため、
            // 実行ファイルと同じ場所に配置されたAssetsを直接ファイルパスで参照する。
            // new BitmapImage(Uri)はUriから非同期・遅延でデコードするため、タイミングに
            // よっては読み込みが失敗しても例外にならず画像が表示されないことがあった
            // (実際に踏んだ不具合: 絵文字/Navアイコンがときおり表示されなくなる)。
            // Navigation側のアイコン読み込み(MainWindow.GetNavIconAsync)と同じく、
            // File.OpenReadで確実にファイルを開いてからSetSourceで同期デコードする
            var fileStem = EmojiRepository.ToFileNameStem(kind);
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Emoji", $"{fileStem}{fileSuffix}.scale-{scale}.png");
            var bitmap = new BitmapImage();
            using (var stream = File.OpenRead(path))
            {
                bitmap.SetSource(stream.AsRandomAccessStream());
            }

            var width = columns * CellDip;
            var height = rows * CellDip;

            var entry = new Entry(bitmap, width, height);
            _cache[cacheKey] = entry;
            return (bitmap, width, height);
        }
    }
}
