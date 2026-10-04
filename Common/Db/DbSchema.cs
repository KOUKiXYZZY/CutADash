namespace Common.Db
{
    /// <summary>
    /// DBのスキーマバージョン定義。CutADash(起動時のチェック)とMigration
    /// (実際のマイグレーション実行)の両方から参照する、両者で唯一の共有定義。
    /// スキーマを変更したら、ここを上げてからMigration側に移行処理を追加する。
    ///
    /// 絵文字マスタはSQLite(emoji.db)をやめ、Assets/Emoji配下のJSON+PNGを
    /// 直接読み込む方式にしたため、ここでは扱わない(EmojiRepository参照)。
    /// </summary>
    public static class DbSchema
    {
        // v2: favorites.dbで、項目(フォルダでないノード)の下に別の項目がぶら下がっている
        // 壊れたデータ(TreeViewの標準ドラッグ&ドロップがフォルダかどうかを区別せず
        // ドロップを受け付けてしまっていた不具合の名残)を、ルート直下へ戻す移行を追加
        public const int CurrentVersion = 2;

        /// <summary>
        /// マイグレーション対象のDBファイル名一覧(AppPaths.GetDataFilePath基準)。
        /// CutADash起動時のチェックとMigration.exeの実行対象を一致させるため、
        /// 増減する場合はここだけ変更すればよいようにする。
        /// </summary>
        public static readonly string[] DatabaseFileNames =
        {
            "clipboard_history.db",
            "favorites.db",
        };
    }
}
