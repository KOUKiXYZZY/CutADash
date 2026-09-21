using System;

namespace CutADash.Repositories.Data.Entities
{
    using SQLite;

    /// <summary>
    /// お気に入りとして複製保存したクリップボード項目。ClipboardItemEntity(履歴)とは
    /// 別テーブル・別ファイル(画像)として独立させており、履歴側が上限で削除されても
    /// 影響を受けない。
    /// </summary>
    public class FavoriteItemEntity
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public int Type { get; set; }

        public string? Text { get; set; }

        public string? Rtf { get; set; }
        public string? Html { get; set; }

        public string? ImageFilePath { get; set; }

        public string? ThumbnailFilePath { get; set; }

        public bool IsGif { get; set; }

        public bool IsShape { get; set; }

        /// <summary>
        /// RawFormats(Excelの図形などOLEオブジェクトを構成する非標準フォーマット)を
        /// シリアライズ・暗号化して保存したファイルのパス。ClipboardItemEntity側と同じく、
        /// これが無いと書き戻し時に単純なビットマップとしてしか貼り付けられない
        /// (ClipboardContentWriterがIsShapeではなくこのパスを見て復元するため)。
        /// </summary>
        public string? RawFormatsFilePath { get; set; }

        public string? FilesJson { get; set; }

        /// <summary>元のコピー日時(履歴側のTimestampをそのまま引き継ぐ)。</summary>
        public DateTime Timestamp { get; set; }

        public string? SourceAppName { get; set; }

        /// <summary>お気に入りに追加した日時。表示用の記録として残すのみで、並び順には使わない。</summary>
        public DateTime FavoritedAt { get; set; }

        /// <summary>フォルダかどうか。trueの時はNameが意味を持ち、ClipboardItem系の列は使わない。</summary>
        public bool IsFolder { get; set; }

        /// <summary>フォルダ名(IsFolder=trueの時のみ使う)。</summary>
        public string? Name { get; set; }

        /// <summary>親フォルダのId。nullはルート直下。</summary>
        public int? ParentId { get; set; }

        /// <summary>
        /// FavoriteListFrameで最後に選択していたフォルダかどうか(IsFolder=trueの行のみ意味を持つ)。
        /// 常に高々1行だけtrueになる。専用テーブルを用意せず、対象のフォルダ行自体に
        /// フラグを立てることで記録する。
        /// </summary>
        public bool IsLastSelectedFolder { get; set; }

        /// <summary>
        /// FavoriteListFrameのTreeViewでこのフォルダが展開状態かどうか(IsFolder=trueの行のみ
        /// 意味を持つ)。既定はtrue(展開)。開閉のたびに即時保存し、アプリ再起動後も
        /// 直前の開閉状態を復元できるようにする。
        /// </summary>
        public bool IsExpanded { get; set; } = true;

        /// <summary>
        /// 入れ子集合モデルの左値/右値。兄弟の並び順と親子関係の両方をこの2値だけで表す。
        /// 木構造が変わるたびFavoriteRepository側で全行まとめて振り直す。
        /// </summary>
        public int Lft { get; set; }
        public int Rgt { get; set; }
    }
}
