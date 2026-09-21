using System;
using Microsoft.Win32;

namespace Preferences.Utils
{
    /// <summary>
    /// アプリケーションのスタートアップ登録を管理するヘルパークラス。
    /// アプリはパッケージ化していない(WindowsPackageType=None)ため、StartupTask APIは使えず、
    /// レジストリの"Run"キーへの登録/削除で「ログイン時に起動」を実現する。
    /// </summary>
    internal static class StartupHelper
    {
        private const string AppName = "CutADash";
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        // レジストリの "Run" キーに登録されているかどうか
        public static bool IsRegistered()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            return key?.GetValue(AppName) is not null;
        }

        // レジストリの "Run" キーにアプリケーションを登録する
        public static void RegisterStartup()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true)
                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

            string exePath = Environment.ProcessPath!;

            key.SetValue(AppName, $"\"{exePath}\"");
        }

        // レジストリの "Run" キーからアプリケーションを削除する
        public static void UnregisterStartup()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            if (key != null)
            {
                key.DeleteValue(AppName, false);
            }
        }
    }
}
