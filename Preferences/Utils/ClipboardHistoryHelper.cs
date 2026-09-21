using Microsoft.Win32;

namespace Preferences.Utils
{
    /// <summary>
    /// Windows標準のクリップボード履歴(Win+Vパネル)を、グループポリシー相当のレジストリ値で
    /// 無効化するためのヘルパー。
    ///
    /// HKEY_CURRENT_USER\Software\Policies\Microsoft\Windows\System の
    /// AllowClipboardHistory(DWORD)を0にすると、Win+Vパネル自体が無効になる。
    /// HKCU配下のため管理者権限は不要。
    /// </summary>
    internal static class ClipboardHistoryHelper
    {
        private const string PolicyKeyPath = @"Software\Policies\Microsoft\Windows\System";
        private const string ValueName = "AllowClipboardHistory";

        /// <summary>
        /// AllowClipboardHistoryを設定する。disabled=trueならWin+Vパネルを無効化(値を0)、
        /// falseならポリシー自体を削除して既定の動作(Windowsの設定に従う)に戻す。
        /// </summary>
        public static void SetEnabled(bool disabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(PolicyKeyPath, writable: true);
            if (key is null)
                return;

            if (disabled)
                key.SetValue(ValueName, 0, RegistryValueKind.DWord);
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
