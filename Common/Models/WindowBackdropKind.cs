namespace Common.Models
{
    /// <summary>ウィンドウの背景素材(バックドロップ)の種類。</summary>
    public enum WindowBackdropKind
    {
        Mica,
        Acrylic,
        Blur,

        /// <summary>見た目だけのテーマ。素材自体はMicaを流用し、暖色のティントを
        /// 重ねる(WindowExtensionsHelpers.ApplyBackdrop、
        /// MainWindow.ApplyBackdropWithTintOverlay参照)。</summary>
        Cat
    }
}
