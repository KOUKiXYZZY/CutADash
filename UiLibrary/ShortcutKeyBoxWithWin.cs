namespace UiLibrary
{
    /// <summary>
    /// Winキーのチェックボックス付きの<see cref="ShortcutKeyBox"/>。
    /// Winキーは、押して入力するのではなく、右のチェックで指定する
    /// (Windowsが先に処理してしまい、アプリに届かない組み合わせが多いため。Dittoと同じ方式)。
    /// チェックボックスの無い版は<see cref="ShortcutKeyBox"/>を使う。
    /// </summary>
    public sealed class ShortcutKeyBoxWithWin : ShortcutKeyBox
    {
        public ShortcutKeyBoxWithWin()
        {
            ShowWindowsKeyOption = true;
        }
    }
}
