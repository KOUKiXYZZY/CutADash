using System;

namespace CutADash.Repositories.Data.Entities
{
    using SQLite;

    public class ClipboardItemEntity
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public int Type { get; set; }

        public string? Text { get; set; }

        // リッチテキスト(Type=Textのときのみ意味を持つ)。DB自体がSQLCipherで暗号化
        // されているため、画像のように別ファイルへ切り出さずそのまま列に持つ
        public string? Rtf { get; set; }
        public string? Html { get; set; }

        // 旧バージョンでフルサイズの画像を直接保存していた列。マイグレーションはせず、
        // 新規書き込みでは使わない(Thumbnail/ImageFilePathに置き換え)。
        public byte[]? Image { get; set; }

        public string? ImageFilePath { get; set; }

        // 一覧表示用の縮小画像。バイト列としてDBに持たず、ImageFilePathと同様
        // ファイルとして書き出し、パスだけを持つ
        public string? ThumbnailFilePath { get; set; }

        public bool IsGif { get; set; }

        // Excelの図形など、OLEオブジェクトとしてコピーされた画像かどうか
        public bool IsShape { get; set; }

        // Excelの図形など、OLEオブジェクトとして編集可能な状態のまま復元するための、
        // コピー時の全クリップボードフォーマットを生バイト列で保存したファイルへのパス
        public string? RawFormatsFilePath { get; set; }

        public string? FilesJson { get; set; }

        public DateTime Timestamp { get; set; }

        public string? SourceAppName { get; set; }

        /// <summary>
        /// ユーザーが付けた名前(Shape/Image項目向け)。未設定ならnullで、
        /// 一覧では「名称未設定」と表示する(NameOrPlaceholderConverter参照)。
        /// RenameItemAsyncが、検索に掛かるよう同じ値をTextへもミラーする。
        /// </summary>
        public string? Name { get; set; }
    }
}
