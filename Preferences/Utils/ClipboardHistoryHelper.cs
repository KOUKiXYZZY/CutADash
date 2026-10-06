using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace Preferences.Utils
{
    /// <summary>
    /// Windows標準のクリップボード履歴(Win+Vパネル)を、グループポリシー相当のレジストリ値で
    /// 無効化するためのヘルパー。
    ///
    /// HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\System の
    /// AllowClipboardHistory(DWORD)を0にすると、Win+Vパネル自体が無効になる
    /// (HKCU側に書いても効かない)。HKLMへの書き込みには管理者権限が必要なため、
    /// reg.exeを管理者として起動して(UACの確認を出して)書き込む。
    /// </summary>
    internal static class ClipboardHistoryHelper
    {
        private const string PolicyKeyPath = @"SOFTWARE\Policies\Microsoft\Windows\System";
        private const string ValueName = "AllowClipboardHistory";

        // ユーザーがUACの確認をキャンセルした時にWin32Exceptionが持つエラーコード
        private const int ErrorCancelled = 1223;

        /// <summary>Win+Vパネルが、ポリシーで無効化されているかどうか(HKLMの実際の値)。</summary>
        public static bool IsDisabled()
        {
            using var key = Registry.LocalMachine.OpenSubKey(PolicyKeyPath, writable: false);
            return key?.GetValue(ValueName) is int value && value == 0;
        }

        /// <summary>
        /// AllowClipboardHistoryを設定する。disabled=trueならWin+Vパネルを無効化(値を0)、
        /// falseならポリシー自体を削除して既定の動作(Windowsの設定に従う)に戻す。
        /// 管理者権限の確認を出すため、ユーザーが操作するまで待つ。
        /// 成功したら(または既に目的の状態なら)true、キャンセル・失敗ならfalseを返す。
        /// </summary>
        public static bool TrySetDisabled(bool disabled)
        {
            // 以前の版が書いていたHKCU側の値は効かないので、残っていれば消しておく(権限は不要)
            RemoveLegacyUserPolicy();

            if (IsDisabled() == disabled)
                return true;

            var keyPath = @"HKLM\" + PolicyKeyPath;
            var arguments = disabled
                ? $"add \"{keyPath}\" /v {ValueName} /t REG_DWORD /d 0 /f"
                : $"delete \"{keyPath}\" /v {ValueName} /f";

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.SystemDirectory, "reg.exe"),
                    Arguments = arguments,
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden,
                };

                using var process = Process.Start(startInfo);
                if (process is null)
                    return false;

                process.WaitForExit();
                return process.ExitCode == 0 && IsDisabled() == disabled;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
            {
                return false;
            }
            catch (Win32Exception)
            {
                return false;
            }
        }

        private static void RemoveLegacyUserPolicy()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(PolicyKeyPath, writable: true);
                key?.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            catch
            {
                // 消せなくても動作には影響しない
            }
        }
    }
}
