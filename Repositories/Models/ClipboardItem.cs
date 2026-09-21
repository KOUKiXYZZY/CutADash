using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;

namespace CutADash.Models
{
    public enum ClipboardContentType
    {
        Unknown,
        Text,
        Image,
        Files
    }

    public partial class ClipboardItem : ObservableObject
    {
        public int Id { get; set; }
        public ClipboardContentType Type { get; set; }

        // Contents画面での編集時、保存後にViewModel.Item経由のバインディングへ
        // 即座に反映させたいため、ObservablePropertyにして変更通知を出す
        // (TextとRtfだけ観測可能にしているのは、今のところこの用途がここだけのため)。
        // Shape/Image項目(Type=Image)では、Nameの値をここへもミラーする(Renameから
        // 呼ばれる各RenameItemAsyncを参照)。検索(FTS/LIKE)がTextしか見ないため、
        // Nameだけに入れると名前を付けても検索で見つけられなかった対策
        [ObservableProperty]
        private string? text;

        /// <summary>
        /// リッチテキストのRTF形式(Type=Textのときのみ意味を持つ)。存在すれば貼り付け時に
        /// 書式付きで復元し、Contents画面のプレビューでも書式付きで表示する。
        /// </summary>
        [ObservableProperty]
        private string? rtf;

        /// <summary>
        /// リッチテキストのHTML形式(Type=Textのときのみ意味を持つ)。CF_HTMLのラッパー
        /// ヘッダーを含む、Windows.ApplicationModel.DataTransferがそのまま扱える形式。
        /// 貼り付け時に書式付きで復元するために使う(プレビュー表示はしない)。
        /// </summary>
        public string? Html { get; set; }

        /// <summary>
        /// フルサイズの画像バイト列。クリップボード取り込み直後、DBへの永続化前のみ保持する
        /// 一時的な値。永続化後はメモリに残さないため、一覧から読み込んだ項目では常にnull。
        /// ペースト/表示時はImageFilePathの指すファイルから読み込む。
        /// </summary>
        public byte[]? Image { get; set; }

        /// <summary>フルサイズ画像の保存先ファイルパス。</summary>
        public string? ImageFilePath { get; set; }

        /// <summary>一覧表示用の縮小画像の保存先ファイルパス。</summary>
        public string? ThumbnailFilePath { get; set; }

        /// <summary>画像がGIF形式かどうか(Type=Imageのときのみ意味を持つ)。</summary>
        public bool IsGif { get; set; }

        /// <summary>
        /// Excelの図形など、OLEオブジェクトとしてコピーされた画像かどうか
        /// (Type=Imageのときのみ意味を持つ)。RawFormatsにEmbed Source等の
        /// マーカーフォーマットが含まれている場合にtrueになる。
        /// </summary>
        public bool IsShape { get; set; }

        /// <summary>
        /// コピー時にクリップボードへ乗っていた全フォーマットの生バイト列
        /// (フォーマット名→データ)。Excelの図形など、OLEオブジェクトとして貼り付け先で
        /// 編集可能な状態のまま復元したい場合に使う。クリップボード取り込み直後、
        /// DBへの永続化前のみ保持する一時的な値。永続化後はメモリに残さないため、
        /// 一覧から読み込んだ項目では常にnull。書き戻し時はRawFormatsFilePathから読み込む。
        /// </summary>
        public Dictionary<string, byte[]>? RawFormats { get; set; }

        /// <summary>RawFormatsをシリアライズ・暗号化して保存したファイルのパス。</summary>
        public string? RawFormatsFilePath { get; set; }

        public List<string>? Files { get; set; }
        public DateTime Timestamp { get; set; }

        /// <summary>コピー元と推測されるアプリの名前(プロセス名)。取得できなければnull。</summary>
        public string? SourceAppName { get; set; }

        /// <summary>
        /// お気に入り/履歴でユーザーが付けた名前(Shape/Image項目向け)。未設定ならnull/空文字で、
        /// 一覧では「名称未設定」と表示する(NameOrPlaceholderConverter参照)。
        /// 検索に掛かるよう、値が変わるたびTextへも同じ値をミラーする
        /// (ClipboardRepository/FavoriteRepositoryのRenameItemAsync参照)。
        /// </summary>
        [ObservableProperty]
        private string? name;

        /// <summary>
        /// シーケンシャルペーストで既にペースト済みかどうか(SequentialPasteWindowでの
        /// チェックマーク表示専用、DBには保存しない)。History/Favorite一覧と同じ
        /// ClipboardItemインスタンスを指すことがあるが、このフラグを参照する場所は
        /// SequentialPasteWindowのテンプレートだけなので、他の一覧の見た目には影響しない。
        /// </summary>
        [ObservableProperty]
        private bool isPasted;
    }
}
