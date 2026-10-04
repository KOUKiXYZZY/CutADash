namespace Common.Models
{
    /// <summary>アプリ全体の明/暗の表示テーマ。バックドロップ(素材)の種類とは別の設定。</summary>
    public enum AppColorTheme
    {
        Light,
        Dark,

        /// <summary>既定。OSのテーマに合わせる。保存済みの設定(Light=0/Dark=1)の値を
        /// ずらさないよう、末尾に追加している。</summary>
        Default
    }
}
