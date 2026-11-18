using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace CutADash.Views.TaskTray
{
    /// <summary>
    /// タスクトレイの右クリックメニューを、Windows標準のポップアップメニュー(TrackPopupMenu)で出す。
    /// WinUIExのMenuFlyoutは、画面に見えない補助ウィンドウの上に出す方式で、そこから開いた
    /// ウィンドウをドラッグして離すと、カーソルが2つ見える不具合が出ていたため、補助ウィンドウを
    /// 使わない標準メニューにしている。TrackPopupMenuはメニューが完全に閉じてから戻る。
    /// </summary>
    internal static class NativeTrayMenu
    {
        /// <summary>メニューの1項目。</summary>
        /// <param name="Id">選ばれた時に返す番号(1以上)。区切り線は0。</param>
        /// <param name="Text">表示する文字。区切り線は空でよい。</param>
        /// <param name="IsChecked">チェックを付けるか。</param>
        internal readonly record struct Item(int Id, string Text, bool IsChecked = false)
        {
            /// <summary>区切り線。</summary>
            public static Item Separator => new(0, string.Empty);
        }

        private const uint MF_STRING = 0x0000;
        private const uint MF_CHECKED = 0x0008;
        private const uint MF_SEPARATOR = 0x0800;
        private const uint TPM_RIGHTBUTTON = 0x0002;
        private const uint TPM_BOTTOMALIGN = 0x0020;
        private const uint TPM_RETURNCMD = 0x0100;
        private const uint WM_NULL = 0x0000;
        private const uint WS_POPUP = 0x80000000;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenuW(IntPtr hMenu, uint flags, UIntPtr id, string? text);
        [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr hMenu);
        [DllImport("user32.dll")] private static extern int TrackPopupMenuEx(IntPtr hMenu, uint flags, int x, int y, IntPtr hWnd, IntPtr tpmParams);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowExW(uint exStyle, string className, string? windowName, uint style,
            int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
        [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hWnd);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadLibraryW(string fileName);
        [DllImport("kernel32.dll")] private static extern IntPtr GetProcAddress(IntPtr module, IntPtr ordinal);

        private delegate int SetPreferredAppModeDelegate(int mode);
        private delegate void FlushMenuThemesDelegate();

        private static bool _darkModeInitialized;

        // 標準メニューをシステムの明暗テーマに追従させる(uxthemeの非公開API。序数135/136)。
        // 失敗してもメニューは出せるため、例外は握りつぶす
        private static void EnsureDarkModeSupport()
        {
            if (_darkModeInitialized)
                return;
            _darkModeInitialized = true;

            try
            {
                var uxtheme = LoadLibraryW("uxtheme.dll");
                var setMode = GetProcAddress(uxtheme, (IntPtr)135);
                var flush = GetProcAddress(uxtheme, (IntPtr)136);
                if (setMode != IntPtr.Zero)
                    Marshal.GetDelegateForFunctionPointer<SetPreferredAppModeDelegate>(setMode)(1 /* AllowDark */);
                if (flush != IntPtr.Zero)
                    Marshal.GetDelegateForFunctionPointer<FlushMenuThemesDelegate>(flush)();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NativeTrayMenu] ダークテーマの設定に失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 現在のマウスカーソルの位置にメニューを出し、選ばれた項目の番号を返す。
        /// メニューが完全に閉じてから戻る。何も選ばれなかった場合は0を返す。
        /// </summary>
        /// <param name="items">表示する項目。</param>
        /// <returns>選ばれた項目のId。選ばれなかった場合は0。</returns>
        public static int Show(IReadOnlyList<Item> items)
        {
            EnsureDarkModeSupport();

            // メニューの持ち主にする、見えないウィンドウ。メニューの外をクリックした時に閉じるには、
            // 持ち主が前面になっている必要がある。毎回作って、終わったら壊す
            var owner = CreateWindowExW(0, "STATIC", null, WS_POPUP, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            var menu = CreatePopupMenu();
            try
            {
                foreach (var item in items)
                {
                    if (item.Id == 0)
                        AppendMenuW(menu, MF_SEPARATOR, UIntPtr.Zero, null);
                    else
                        AppendMenuW(menu, MF_STRING | (item.IsChecked ? MF_CHECKED : 0), (UIntPtr)(uint)item.Id, item.Text);
                }

                GetCursorPos(out var point);
                SetForegroundWindow(owner);
                var selected = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_BOTTOMALIGN, point.X, point.Y, owner, IntPtr.Zero);

                // メニューを閉じた後にWM_NULLを送るのは、TrackPopupMenuの公式の注意事項
                // (メニューの外をクリックして閉じた直後の、次のメニューが出ない不具合を避ける)
                PostMessageW(owner, WM_NULL, IntPtr.Zero, IntPtr.Zero);
                return selected;
            }
            finally
            {
                DestroyMenu(menu);
                DestroyWindow(owner);
            }
        }
    }
}
