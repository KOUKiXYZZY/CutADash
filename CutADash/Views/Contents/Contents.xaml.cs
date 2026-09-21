using CutADash.Models;
using CutADash.Utils;
using CutADash.ViewModels;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CutADash.Views.Contents
{
    /// <summary>
    /// 選択中のクリップボード履歴項目を詳細表示するページ。
    /// ViewModel(ClipboardDetailViewModel)をDataContextとして持ち、
    /// Itemが変わるたびにCodeEditor/ImagePreviewへ反映する。
    ///
    /// CodeEditorは表示専用(IsReadOnly=True)で、編集機能は持たない
    /// (以前は保存ボタン付きで編集できたが、編集機能自体を廃止した)。
    /// </summary>
    public sealed partial class Contents : Page
    {
        // 選択をすばやく切り替えたとき、古い表示処理が後から終わって新しい選択の
        // 表示を上書きしてしまわないよう、呼び出しごとに発行するトークン
        private int _displayToken;

        // 表示中の項目の保存先(History/Favoriteどちらの一覧から開かれたか)。
        // 編集機能は無いため現在は未使用だが、呼び出し側のAPI(ApplyContext等)との
        // 互換のために残してある
        private IClipboardItemListViewModel? _ownerViewModel;

        public ClipboardDetailViewModel ViewModel { get; } = new();

        public Contents()
        {
            InitializeComponent();
            DataContext = ViewModel;

            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            ViewModel.QrCodeNotFound += OnQrCodeNotFound;

            LocalizeUi();

            ApplyCatContentsBackground();
            // ListFrameと同じ理由(タブを開いたまま設定画面でテーマだけ変えた場合、
            // 再ナビゲーションが起きずOnNavigatedToが呼ばれない)で購読が別途必要
            Preferences.PreferencesGateway.WindowBackdropChanged += OnWindowBackdropChanged;
            this.Unloaded += (_, _) => Preferences.PreferencesGateway.WindowBackdropChanged -= OnWindowBackdropChanged;

            // テキスト項目の文字色はActualThemeを見て決めている(ResetCodeEditorDefaultFormat)。
            // 表示中に実行時にシステムのテーマが変わった場合も、選び直してすぐ反映する
            this.ActualThemeChanged += (_, _) => _ = DisplayItemAsync(ViewModel.Item, ++_displayToken);
        }

        private void OnWindowBackdropChanged(Common.Models.WindowBackdropKind kind)
            => DispatcherQueue.TryEnqueue(ApplyCatContentsBackground);

        /// <summary>
        /// テーマが「猫」の間だけ、編集面の背後に肉球を薄っすらと表示する。
        /// </summary>
        private void ApplyCatContentsBackground()
        {
            var isCatTheme = Preferences.PreferencesGateway.GetWindowBackdrop() == Common.Models.WindowBackdropKind.Cat;
            CatPawBackground.Visibility = isCatTheme ? Visibility.Visible : Visibility.Collapsed;
        }

        private void LocalizeUi()
        {
            EmptyText.Text = Common.Utils.AppStrings.Get("Empty_Text");
            PasteButtonText.Text = Common.Utils.AppStrings.Get("Contents_Paste");
            ToolTipService.SetToolTip(PasteButton, Common.Utils.AppStrings.Get("Contents_Paste"));
            PasteFormattedMenuItem.Text = Common.Utils.AppStrings.Get("Contents_PasteFormatted");
            PastePlainMenuItem.Text = Common.Utils.AppStrings.Get("Contents_PastePlain");
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is ContentsNavigationParameter param)
            {
                ViewModel.MainWindow = param.MainWindow;
                ApplyContext(param.Item, param.OwnerViewModel);
            }
            else
            {
                ApplyContext(e.Parameter as ClipboardItem, null);
            }
        }

        /// <summary>
        /// 表示する項目と、その編集の保存先を、確認なしで即座に差し替える。
        /// ページを新規作成する初回のOnNavigatedTo専用(その時点では表示中の編集が
        /// 存在しないため確認は不要)。既存ページを再利用する経路では
        /// RequestSetContextAsyncを使うこと。
        /// </summary>
        private void ApplyContext(ClipboardItem? item, IClipboardItemListViewModel? ownerViewModel)
        {
            _ownerViewModel = ownerViewModel;
            ViewModel.Item = item;
        }

        /// <summary>確認なしで即座に切り替える。</summary>
        public void ForceSetContext(ClipboardItem? item, IClipboardItemListViewModel? ownerViewModel)
            => ApplyContext(item, ownerViewModel);

        /// <summary>
        /// 表示する項目を切り替える。編集機能が無いため確認は不要で、常にtrueを返す
        /// (呼び出し側のAPIとの互換のためTask&lt;bool&gt;のまま残してある)。
        /// </summary>
        public Task<bool> RequestSetContextAsync(ClipboardItem? item, IClipboardItemListViewModel? ownerViewModel)
        {
            ApplyContext(item, ownerViewModel);
            return Task.FromResult(true);
        }

        /// <summary>
        /// 編集機能が無いため、未保存の変更が発生することは無い。常にtrueを返す
        /// (呼び出し側のAPIとの互換のためTask&lt;bool&gt;のまま残してある)。
        /// </summary>
        public Task<bool> ConfirmDiscardChangesAsync() => Task.FromResult(true);

        /// <summary>
        /// タブ切り替えなどでこのページから離れる前に呼ぶ。表示中の画像を破棄しないまま
        /// ページごと捨てると、GCされるまで画像がメモリに残り続けてしまうため。
        /// </summary>
        public void ReleaseImage()
        {
            ImagePreview.Source = null;
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // ViewModel.Itemが変わるたびにCodeEditor/ImagePreviewへ反映する。
            // Encode/Decodeボタンの有効/可視状態はContents.xaml側でViewModelのプロパティに
            // 直接バインドしているため、ここでは表示の切り替えだけを行う。
            // 非同期メソッドだがイベントハンドラはasync void化したくないので、あえて待たない。
            if (e.PropertyName == nameof(ClipboardDetailViewModel.Item))
            {
                _ = DisplayItemAsync(ViewModel.Item, ++_displayToken);
            }
        }

        private async void OnQrCodeNotFound()
        {
            var dialog = new ContentDialog
            {
                Title = "QRコードが見つかりませんでした",
                Content = "画像からQRコードを読み取れませんでした。",
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
        }

        private async Task DisplayItemAsync(ClipboardItem? item, int token)
        {
            // 他の項目に切り替わったら、表示中の画像を即座に破棄してメモリを解放する。
            // 新しい項目が画像でなければこのままでよいし、画像であれば下でSourceを設定し直す。
            ImagePreview.Source = null;

            if (item is null)
            {
                CodeEditor.Visibility = Visibility.Collapsed;
                ImageAreaGrid.Visibility = Visibility.Collapsed;
                FilesPreview.Visibility = Visibility.Collapsed;
                return;
            }

            if (item.Type == ClipboardContentType.Image)
            {
                var bitmap = await LoadBitmapAsync(item);

                // 読み込んでいる間にさらに別の項目へ切り替わっていたら、
                // 今表示すべきなのはその新しい項目なので、ここでの反映は捨てる
                if (token != _displayToken)
                    return;

                if (bitmap != null)
                {
                    ImagePreview.Source = bitmap;
                    ImageAreaGrid.Visibility = Visibility.Visible;
                    FadeInAnimationHelper.FadeInFromBottom(ImagePreview);
                }
                else
                {
                    ImageAreaGrid.Visibility = Visibility.Collapsed;
                }
                CodeEditor.Visibility = Visibility.Collapsed;
                FilesPreview.Visibility = Visibility.Collapsed;
            }
            else if (item.Type == ClipboardContentType.Files && item.Files != null)
            {
                if (token != _displayToken)
                    return;

                FilesPreview.ItemsSource = item.Files;
                FilesPreview.Visibility = Visibility.Visible;
                CodeEditor.Visibility = Visibility.Collapsed;
                ImageAreaGrid.Visibility = Visibility.Collapsed;
                FadeInAnimationHelper.FadeInFromBottom(FilesPreview);
            }
            else
            {
                // 言語判定・シンタックスハイライトはCPU負荷があるため、既に次の選択で
                // 上書きされているならここで打ち切り、無駄な描画更新をしない
                if (token != _displayToken)
                    return;

                // Rtf/Htmlを保持していても書式は再現せず、常にプレーンテキストとして表示する
                // (履歴から項目を見分けられれば十分という用途のため、書式付き表示はやめた)。
                //
                // 直前の項目がリッチテキスト(FormatRtf)だった場合、そこで変わった既定の
                // 文字書式(色・太字等)がSetText(None, ...)後も引き継がれてしまうため、
                // 表示前に既定の書式へ戻す
                ResetCodeEditorDefaultFormat();

                var text = item.Text ?? string.Empty;

                // 文字色は「既定書式のリセット」だけに頼らず、RTFのcolortbl+\cfで明示的に
                // 指定する。GetDefaultCharacterFormat/SetDefaultCharacterFormatはIsReadOnly=True
                // の間は反映されないことがあり、それだけに頼ると常に前回の色が残ってしまう
                // (ダークテーマなのに文字が黒いままになる不具合の原因だった)。
                // 背景色(ハイライト)はITextCharacterFormatに公開プロパティが無く、
                // 既定書式のリセットでは戻せないため、\highlight0(なし)も明示する
                var isDark = this.ActualTheme == ElementTheme.Dark;
                SetEditorText(TextSetOptions.FormatRtf, BuildPlainRtf(text, isDark));

                // EditorTextはEncode/Decodeコマンドが読み書きするスナップショットのため、
                // RichEditBoxへ表示した内容と同期しておく
                ViewModel.EditorText = text;

                CodeEditor.Visibility = Visibility.Visible;
                ImageAreaGrid.Visibility = Visibility.Collapsed;
                FilesPreview.Visibility = Visibility.Collapsed;
                FadeInAnimationHelper.FadeInFromBottom(CodeEditor);
            }
        }

        // FormatRtfで表示した直後は、RTFが指定した既定の文字色・太字等がCodeEditorの
        // 既定書式として残ってしまう(SetText(None, ...)しても引き継がれる)ため、
        // プレーンテキストを表示する前に呼んで、テーマに合わせた素の書式へ戻す
        /// <summary>
        /// プレーンテキストを、書式・背景色を持たない最小限のRTFとして組み立てる。
        /// \highlight0を明示することで、直前の項目に付いていた背景色がRichEditBox内部の
        /// 状態として残るのを防ぐ(既定書式のリセットだけでは背景色は戻せないため)。
        /// 文字色もcolortbl+\cf1でテーマに応じた色を明示する(既定書式のリセットだけに
        /// 頼ると反映されないことがあったため)。
        /// </summary>
        private static string BuildPlainRtf(string text, bool isDark)
        {
            // CrLf/Cr/Lfのどれで改行されていても表示上は1つの改行として扱う。
            // CrLfをLfへ寄せてからCrをLfへ寄せることで、CrLfを2回の改行として
            // 二重に\parしてしまわないようにする
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');

            var escaped = new System.Text.StringBuilder();
            foreach (var ch in text)
            {
                switch (ch)
                {
                    case '\\': case '{': case '}':
                        escaped.Append('\\').Append(ch);
                        break;
                    case '\n':
                        escaped.Append(@"\par ");
                        break;
                    default:
                        if (ch < 128)
                            escaped.Append(ch);
                        else
                            escaped.Append(System.Globalization.CultureInfo.InvariantCulture, $@"\u{(short)ch}?");
                        break;
                }
            }

            var (r, g, b) = isDark ? (0xFF, 0xFF, 0xFF) : (0x1C, 0x1C, 0x1C);
            return $@"{{\rtf1\ansi\deff0{{\fonttbl{{\f0 Segoe UI;}}}}{{\colortbl;\red{r}\green{g}\blue{b};}}\f0\cf1\highlight0 {escaped}}}";
        }

        // CodeEditorはIsReadOnly=Trueだが、RichEditBoxのDocument.SetTextは読み取り専用中は
        // UnauthorizedAccessExceptionを投げて失敗する(プログラムからの書き込みもブロックされる)。
        // そのため表示用の差し替え(ユーザー操作ではない)のときだけ、SetTextの間だけ
        // 一時的にIsReadOnlyを解除する
        private void SetEditorText(TextSetOptions options, string text)
        {
            CodeEditor.IsReadOnly = false;
            try
            {
                CodeEditor.Document.SetText(options, text);
            }
            finally
            {
                CodeEditor.IsReadOnly = true;
            }
        }

        private void ResetCodeEditorDefaultFormat()
        {
            // Application.Current.RequestedThemeは起動時の既定値のまま更新されず、実行中に
            // システムのテーマ(明/暗)が変わっても追従しない。実際に効いている見た目の
            // テーマを見るには、要素のActualTheme(ActualThemeChangedで追従する)を使う
            var isDark = this.ActualTheme == ElementTheme.Dark;
            var format = CodeEditor.Document.GetDefaultCharacterFormat();
            format.ForegroundColor = isDark
                ? Windows.UI.Color.FromArgb(255, 0xFF, 0xFF, 0xFF)
                : Windows.UI.Color.FromArgb(255, 0x1C, 0x1C, 0x1C);
            format.Bold = FormatEffect.Off;
            format.Italic = FormatEffect.Off;
            format.Underline = UnderlineType.None;
            format.Strikethrough = FormatEffect.Off;
            CodeEditor.Document.SetDefaultCharacterFormat(format);
        }

        // Encode/Decode操作の前後で、RichEditBoxとViewModel.EditorText(スナップショット)を
        // 同期する。業務ロジック(実際のEncode/Decode・履歴追加)はViewModelのCommandが持つ

        private void Base64EncodeButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.EditorText = GetEditorText();
            ViewModel.Base64EncodeCommand.Execute(null);
            ReplaceEditorText(ViewModel.EditorText);
        }

        private void Base64DecodeButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.EditorText = GetEditorText();
            ViewModel.Base64DecodeCommand.Execute(null);
            ReplaceEditorText(ViewModel.EditorText);
        }

        private void UrlEncodeButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.EditorText = GetEditorText();
            ViewModel.UrlEncodeCommand.Execute(null);
            ReplaceEditorText(ViewModel.EditorText);
        }

        private void UrlDecodeButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.EditorText = GetEditorText();
            ViewModel.UrlDecodeCommand.Execute(null);
            ReplaceEditorText(ViewModel.EditorText);
        }

        private void QrEncodeButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.EditorText = GetEditorText();
            ViewModel.QrEncodeCommand.Execute(null);
        }

        // Decodeドロップダウンを開くたびに、現在表示中のテキストをViewModelへ同期する。
        // Base64/URL項目のIsEnabled・Visibility(Image項目かどうか)はContents.xaml側で
        // ViewModelのCanDecodeBase64/CanDecodeUrl/IsDecodeTargetImage等に直接バインドしている
        private void DecodeFlyout_Opening(object sender, object e)
        {
            if (ViewModel.Item?.Type != ClipboardContentType.Image)
                ViewModel.EditorText = GetEditorText();
        }

        private string GetEditorText()
        {
            // RichEditBoxのGetTextは末尾に常に\r(キャリッジリターン)を1つ付け足すうえ、
            // 複数行の場合は各行の区切りも\rのみ(CRLFではなくCR単体)で表現される。
            // そのままエンコードすると%0Dが混入してしまうため、
            // 末尾の余分な\rを除いたうえで、行区切りを一般的な\r\nへ正規化する
            CodeEditor.Document.GetText(TextGetOptions.None, out var text);
            text = text.TrimEnd('\r');
            return System.Text.RegularExpressions.Regex.Replace(text, "\r(?!\n)", "\r\n");
        }

        // CodeEditorの表示を置き換える
        private void ReplaceEditorText(string text)
        {
            SetEditorText(TextSetOptions.None, text);
        }

        // プレビュー欄はせいぜい数百px幅なのに、スクリーンショットは4K解像度などフルサイズの
        // まま渡すと、そのピクセル数分デコードされてメモリに載ってしまう
        // (例: 3840x2160のスクリーンショットなら1枚で数十MB)。DecodePixelWidthで上限を
        // 指定し、必要以上の解像度でデコードしないようにする(元がこれより小さければ縮小されない)。
        private const int MaxDecodePixelWidth = 1600;

        // Contentsではフルサイズではなく、DB登録時に作った縮小サムネイル
        // (Item.ThumbnailFilePath)を表示する。ImageThumbnailGeneratorが常に
        // 先頭フレームのみをPNGへ変換して作るため、GIFでもアニメーションしない。
        // Uriから直接BitmapImageを作ると、バイト列をいったんマネージドメモリに
        // 読み込む必要がなく、メモリの解放も確実になる。
        // サムネイルがまだ無い(生成に失敗した等)場合のみ、フルサイズにフォールバックする。
        private static async Task<Microsoft.UI.Xaml.Media.Imaging.BitmapImage?> LoadBitmapAsync(ClipboardItem item)
        {
            if (!string.IsNullOrEmpty(item.ThumbnailFilePath) && File.Exists(item.ThumbnailFilePath))
            {
                var bitmap = await TryLoadFromFileAsync(item.ThumbnailFilePath);
                if (bitmap is not null)
                    return bitmap;
            }

            if (!string.IsNullOrEmpty(item.ImageFilePath) && File.Exists(item.ImageFilePath))
            {
                var bitmap = await TryLoadFromFileAsync(item.ImageFilePath);
                if (bitmap is not null)
                    return bitmap;
            }

            if (item.Image is not null)
            {
                return await CreateBitmapFromBytesAsync(item.Image);
            }

            return null;
        }

        // UriSourceへ直接パスを渡す方式は、原因不明のままImageOpened/ImageFailedの
        // どちらも発火せず永久にハングすることが確認されたため使わない。
        // ファイルをバイト列として読み込み、ストリーム経由でSetSourceAsyncする方式は
        // 同じ状況で確実に完了するため、こちらに統一する。
        private static async Task<Microsoft.UI.Xaml.Media.Imaging.BitmapImage?> TryLoadFromFileAsync(string path)
        {
            try
            {
                var bytes = await Common.Db.ImageFileCipher.ReadDecryptedFileAsync(path);
                return await CreateBitmapFromBytesAsync(bytes);
            }
            catch (Exception ex)
            {
                LogImageLoadFailure(path, ex.ToString());
                return null;
            }
        }

        private static void LogImageLoadFailure(string path, string reason)
        {
            try
            {
                var logPath = Common.Utils.AppPaths.GetDataFilePath("image_load_errors.log");
                File.AppendAllText(logPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} path={path} reason={reason}{Environment.NewLine}");
            }
            catch
            {
                // ログ自体の失敗で表示処理を止めたくないため無視する
            }
        }

        private static async Task<Microsoft.UI.Xaml.Media.Imaging.BitmapImage> CreateBitmapFromBytesAsync(byte[] imageBytes)
        {
            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage
            {
                DecodePixelWidth = MaxDecodePixelWidth
            };
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            await stream.WriteAsync(imageBytes.AsBuffer());
            stream.Seek(0);
            await bitmap.SetSourceAsync(stream);
            return bitmap;
        }
    }
}
