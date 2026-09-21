using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CutADash.Models;
using CutADash.Utils;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CutADash.ViewModels
{
    /// <summary>
    /// Contentsページ(選択中の履歴項目の詳細表示)専用のViewModel。
    /// Itemがnullなら「未選択/履歴なし」を表す。
    ///
    /// RichEditBox/BitmapImageの直接操作(表示用の書式付けや画像デコード)はWinUIの
    /// コントロールAPIに強く依存するためContents.xaml.cs(View)側に残すが、
    /// Encode/Decode・ペースト・QR化などの「業務ロジック」はこちらに集約する。
    /// EditorTextは、RichEditBoxに現在表示されているテキストのスナップショットで、
    /// View側がEncode/Decode系コマンドの前後でRichEditBoxとの同期を行う
    /// (RichEditBoxのDocumentはx:Bindで直接同期できないWinUI固有のAPIのため)。
    /// </summary>
    public partial class ClipboardDetailViewModel : ObservableObject
    {
        // ForegroundPasteHelper呼び出し・BumpToTopAsync解決(Provider経由)に必要。
        // Contents.xaml.csがOnNavigatedTo時点でしか受け取れないため、後から設定できるようにする
        public Views.MainWindow? MainWindow { get; set; }

        /// <summary>QRコードが見つからなかった時に発火する。ContentDialog表示はView側の責務。</summary>
        public event Action? QrCodeNotFound;

        [ObservableProperty]
        private ClipboardItem? item;

        [ObservableProperty]
        private string editorText = string.Empty;

        public bool IsEmpty => Item is null;

        public bool IsImage => Item is { Type: ClipboardContentType.Image, Image: not null };

        public bool CanEncode => Item?.Type == ClipboardContentType.Text;

        public bool CanDecode => Item?.Type is ClipboardContentType.Text or ClipboardContentType.Image;

        public bool ShowPasteButton => Item?.Type == ClipboardContentType.Text;

        public bool ShowPasteFormatted => Item?.Rtf is not null || Item?.Html is not null;

        public bool IsDecodeTargetImage => Item?.Type == ClipboardContentType.Image;

        public bool IsDecodeTargetText => !IsDecodeTargetImage;

        public bool CanDecodeBase64 => !IsDecodeTargetImage && IsLikelyBase64(EditorText);

        public bool CanDecodeUrl => !IsDecodeTargetImage && IsLikelyUrlEncoded(EditorText);

        partial void OnItemChanged(ClipboardItem? value)
        {
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(IsImage));
            OnPropertyChanged(nameof(CanEncode));
            OnPropertyChanged(nameof(CanDecode));
            OnPropertyChanged(nameof(ShowPasteButton));
            OnPropertyChanged(nameof(ShowPasteFormatted));
            OnPropertyChanged(nameof(IsDecodeTargetImage));
            OnPropertyChanged(nameof(IsDecodeTargetText));
        }

        partial void OnEditorTextChanged(string value)
        {
            OnPropertyChanged(nameof(CanDecodeBase64));
            OnPropertyChanged(nameof(CanDecodeUrl));
        }

        // リッチテキスト項目を、書式を保ったまま直前のフォアグラウンドウィンドウへ貼り付ける
        [RelayCommand]
        private async Task PasteFormatted()
        {
            if (Item is not { } item)
                return;

            await ForegroundPasteHelper.PasteToPreviousWindowAsync(MainWindow, item, plainTextOnly: false);
            await BumpItemToTopAsync(item);
        }

        // リッチテキスト項目を、書式を捨ててプレーンテキストとして貼り付ける
        [RelayCommand]
        private async Task PastePlain()
        {
            if (Item is not { } item)
                return;

            await ForegroundPasteHelper.PasteToPreviousWindowAsync(MainWindow, item, plainTextOnly: true);
            await BumpItemToTopAsync(item);
        }

        // ペーストした項目を履歴の先頭へ上げる(お気に入り等、履歴に存在しない項目には何もしない)
        private Task BumpItemToTopAsync(ClipboardItem item)
        {
            var viewModel = MainWindow?.Provider?.GetService<ClipboardListViewModel>();
            return viewModel?.BumpToTopAsync(item) ?? Task.CompletedTask;
        }

        // 表示中のテキストをBase64エンコードして置き換え、QRと同様に新規の履歴項目としても残す
        // (貼り付けは行わない。その場で結果を確認できるようパレットは開いたままにする)
        [RelayCommand]
        private void Base64Encode()
        {
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(EditorText));
            EditorText = encoded;
            AddTextResultToHistory(encoded);
        }

        // Base64として不正な文字列だった場合は何もしない
        [RelayCommand]
        private void Base64Decode()
        {
            try
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(EditorText.Trim()));
                EditorText = decoded;
                AddTextResultToHistory(decoded);
            }
            catch (FormatException)
            {
                // 不正なBase64文字列は無視する
            }
        }

        [RelayCommand]
        private void UrlEncode()
        {
            var encoded = Uri.EscapeDataString(EditorText);
            EditorText = encoded;
            AddTextResultToHistory(encoded);
        }

        // 不正な%エスケープ等が含まれる場合は何もしない
        [RelayCommand]
        private void UrlDecode()
        {
            try
            {
                var decoded = Uri.UnescapeDataString(EditorText);
                EditorText = decoded;
                AddTextResultToHistory(decoded);
            }
            catch (UriFormatException)
            {
                // 不正なURLエンコード文字列は無視する
            }
        }

        // Encode/Decode結果を、QRコードの読み取り結果と同じ扱いで新規の履歴項目として残す
        // (OSクリップボードへ書き戻すだけで、貼り付けは行わない)
        private void AddTextResultToHistory(string text)
        {
            var item = new ClipboardItem
            {
                Type = ClipboardContentType.Text,
                Text = text,
                Timestamp = DateTime.Now
            };
            _ = ForegroundPasteHelper.PasteToPreviousWindowAsync(MainWindow, item, addToHistory: true, paste: false);
        }

        // 表示中の画像からQRコードを読み取り、履歴に追加する(貼り付けはしない)。
        // 読み取れなければQrCodeNotFoundを発火する(ダイアログ表示はView側の責務)
        [RelayCommand]
        private async Task QrDecode()
        {
            if (Item is not { Type: ClipboardContentType.Image } item)
                return;

            byte[]? imageBytes = null;
            if (!string.IsNullOrEmpty(item.ImageFilePath) && System.IO.File.Exists(item.ImageFilePath))
                imageBytes = await Common.Db.ImageFileCipher.ReadDecryptedFileAsync(item.ImageFilePath);
            else if (item.Image is not null)
                imageBytes = item.Image;

            var decoded = imageBytes is null ? null : QrDecoding.QrDecoder.Decode(imageBytes);

            if (decoded is null)
            {
                QrCodeNotFound?.Invoke();
                return;
            }

            var decodedItem = new ClipboardItem
            {
                Type = ClipboardContentType.Text,
                Text = decoded,
                Timestamp = DateTime.Now
            };
            _ = ForegroundPasteHelper.PasteToPreviousWindowAsync(MainWindow, decodedItem, addToHistory: true, paste: false);
        }

        // 表示中のテキストをQRコード画像に変換し、直前のフォアグラウンドウィンドウへ
        // 画像としてペーストする
        [RelayCommand]
        private void QrEncode()
        {
            if (string.IsNullOrEmpty(EditorText))
                return;

            var pngBytes = QrDecoding.QrEncoder.Encode(EditorText);
            var clipboardItem = new ClipboardItem
            {
                Type = ClipboardContentType.Image,
                Image = pngBytes,
                Timestamp = DateTime.Now
            };
            _ = ForegroundPasteHelper.PasteToPreviousWindowAsync(MainWindow, clipboardItem, addToHistory: true, paste: false);
        }

        // 文字集合・長さ・実際にデコードできるかに加え、デコード結果が意味のある
        // テキスト(不正なUTF8バイト列でない)であることまで見て誤検知を減らす
        private static bool IsLikelyBase64(string text)
        {
            text = text.Trim();
            if (text.Length == 0 || text.Length % 4 != 0)
                return false;

            if (!Regex.IsMatch(text, @"^[A-Za-z0-9+/]*={0,2}$"))
                return false;

            try
            {
                var bytes = Convert.FromBase64String(text);
                if (bytes.Length == 0)
                    return false;

                new UTF8Encoding(false, true).GetString(bytes);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // "%XX"形式のパーセントエンコーディングを含むかどうかで判定する
        private static bool IsLikelyUrlEncoded(string text)
        {
            return Regex.IsMatch(text, "%[0-9A-Fa-f]{2}");
        }
    }
}
