using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static WinAPI.Structs;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WinAPI
{
    public class Structs
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct GUITHREADINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public RECT rcCaret;
        }

        // SendInput用。keybd_eventより信頼性が高く、複数のキー入力を1回のOS呼び出しで
        // まとめて処理させられるため、修飾キー(Ctrl等)の押下状態が途中で欠落しにくい
        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        // 実際のWin32 INPUT構造体はMOUSEINPUT/KEYBDINPUT/HARDWAREINPUTのunionで、
        // x64では40バイト固定。SendInputはこのサイズと一致しないと失敗するため、
        // 使わないメンバーは省いてもSize=40で全体サイズを合わせておく
        [StructLayout(LayoutKind.Explicit, Size = 40)]
        public struct INPUT
        {
            [FieldOffset(0)] public uint type;
            [FieldOffset(8)] public KEYBDINPUT ki;
        }
    }
        
    public static class WindowStyles
    {
        public const uint WS_OVERLAPPED = 0x00000000;
        public const uint WS_POPUP = 0x80000000;
        public const uint WS_CHILD = 0x40000000;
        public const uint WS_MINIMIZE = 0x20000000;
        public const uint WS_VISIBLE = 0x10000000;
        public const uint WS_DISABLED = 0x08000000;
        public const uint WS_CLIPSIBLINGS = 0x04000000;
        public const uint WS_CLIPCHILDREN = 0x02000000;
        public const uint WS_MAXIMIZE = 0x01000000;
        public const uint WS_CAPTION = 0x00C00000;     // WS_BORDER | WS_DLGFRAME
        public const uint WS_BORDER = 0x00800000;
        public const uint WS_DLGFRAME = 0x00400000;
        public const uint WS_VSCROLL = 0x00200000;
        public const uint WS_HSCROLL = 0x00100000;
        public const uint WS_SYSMENU = 0x00080000;
        public const uint WS_THICKFRAME = 0x00040000;
        public const uint WS_GROUP = 0x00020000;
        public const uint WS_TABSTOP = 0x00010000;
    }

    public static class  ShowWindowCommands
    {
        public const int SW_HIDE = 0;
        public const int SW_SHOWNORMAL = 1;
        public const int SW_SHOWMINIMIZED = 2;
        public const int SW_SHOWMAXIMIZED = 3;
        public const int SW_SHOWNOACTIVATE = 4;
        public const int SW_SHOW = 5;
        public const int SW_MINIMIZE = 6;
        public const int SW_SHOWMINNOACTIVE = 7;
        public const int SW_SHOWNA = 8;
        public const int SW_RESTORE = 9;
    }

    public static class WindowMessages
    {
        // 別のアプリケーション(別スレッド)のウィンドウへアクティブ化が移る/から移ってくる
        // 時にだけ送られる。WM_ACTIVATE(WinUIのWindow.Activatedに対応)と違い、
        // 自分でSetForegroundWindowを呼んでいる最中に起きるような、アプリ境界を
        // またがない一時的なアクティブ化の揺れでは発火しない。
        // wParamが0なら非アクティブ化(他アプリに奪われた)、0以外ならアクティブ化
        public const int WM_ACTIVATEAPP = 0x001C;
        public const int WM_HOTKEY = 0x0312;
        public const int WM_CLIPBOARDUPDATE = 0x031D;
    }

    public static class WindowLongIndex
    {
        public const int GWL_WNDPROC = -4;
    }

    public class WndProc
    {
        public delegate IntPtr WndProcDelegate(IntPtr hWnd,int msg, IntPtr wParam, IntPtr lParam);
    }

    public partial class WinUser
    {

        /// <summary>
        ///
        /// </summary>
        /// <example>
        /// GUITHREADINFO guiInfo = new GUITHREADINFO();
        /// guiInfo.cbSize = Marshal.SizeOf(guiInfo);
        /// 
        /// if (GetGUIThreadInfo(0, ref guiInfo))         {
        ///    Console.WriteLine($"Caret位置: {guiInfo.rcCaret.Left}, {guiInfo.rcCaret.Top}");
        /// }
        /// </example>
        /// <param name="idThread"></param>
        /// <param name="lpgui"></param>
        /// <returns></returns>
        [DllImport("user32.dll")]
        public static extern bool GetGUIThreadInfo(uint idThread, ref Structs.GUITHREADINFO lpgui);

        // フレーム(トップレベル)ウィンドウをフォアグラウンドにしただけでは、MDI/タブ構成の
        // アプリ(サクラエディタ等)で実際に文字入力を受け付ける子コントロールへ
        // キーボードフォーカスが戻らないことがあるため、明示的にSetFocusする際に使う
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetFocus(IntPtr hWnd);

        // SetForegroundWindowと併せて呼ぶ。z順を上げてから前面化する方が確実に切り替わる
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool BringWindowToTop(IntPtr hWnd);


        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        public static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        // 指定した画面座標(物理ピクセル)にあるウィンドウのハンドルを取得する。
        // 子ウィンドウ(コントロール)を含む、最前面にある実際のウィンドウが返る
        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT point);

        // ウィンドウクラス名を取得する。クラシックなEdit/RichEditコントロールかどうかを
        // 判定するために使う(SelectionWatcherのEM_GETSELフォールバック参照)
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        // 現在のマウスカーソル位置(物理ピクセル、スクリーン座標)を取得する。
        // UI AutomationのTextSelectionChangedEventはトリガー位置を教えてくれないため、
        // SelectionWatcherがポップアップの表示位置を決めるのに使う
        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        // クリップボードへ実際に書き込んだウィンドウ(所有者)を取得する。
        // フォアグラウンドウィンドウの推測と違い、コピー元アプリを直接特定できる
        [DllImport("user32.dll")]
        public static extern IntPtr GetClipboardOwner();

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        // Altキーの疑似入力によるフォアグラウンドロック回避は、遷移先ウィンドウで
        // メニューのアクセスキーモードを誤って起動してしまうことがあるため、
        // 代わりに入力キューを一時的に結合してSetForegroundWindowを許可させる
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        // 複数のキー入力を1回のOS呼び出しでまとめて処理させられ、keybd_eventより
        // 修飾キーの押下状態が欠落しにくいSendInputを使う
        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        public const uint INPUT_KEYBOARD = 1;
        public const uint KEYEVENTF_KEYUP = 0x0002;
        public const byte VK_SHIFT = 0x10;
        public const byte VK_MENU = 0x12;
        public const byte VK_CONTROL = 0x11;
        public const byte VK_V = 0x56;
        public const byte VK_LSHIFT = 0xA0;
        public const byte VK_RSHIFT = 0xA1;
        public const byte VK_LCONTROL = 0xA2;
        public const byte VK_RCONTROL = 0xA3;
        public const byte VK_LMENU = 0xA4;
        public const byte VK_RMENU = 0xA5;
        public const byte VK_LWIN = 0x5B;
        public const byte VK_RWIN = 0x5C;

        // ペースト直前にフォアグラウンドロックの制限時間を一時的に0にするため
        // (Dittoの実装を参考。AttachThreadInputだけに頼るより確実にSetForegroundWindowが通る)
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref uint pvParam, uint fWinIni);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

        public const uint SPI_GETFOREGROUNDLOCKTIMEOUT = 0x2000;
        public const uint SPI_SETFOREGROUNDLOCKTIMEOUT = 0x2001;

        // wScanを0のまま送ると、スキャンコードでハードウェア入力かどうかを判定する
        // アプリ(Chromium系など)で無視/誤解釈されることがあるため、対応するスキャンコードを取得する
        [DllImport("user32.dll")]
        public static extern uint MapVirtualKey(uint uCode, uint uMapType);

        public const uint MAPVK_VK_TO_VSC = 0;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        public class ClipboardNative
        {
            [DllImport("user32.dll")]
            public static extern bool AddClipboardFormatListener(IntPtr hwnd);
            [DllImport("user32.dll")]
            public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

            /// <summary>
            /// クリップボードの内容が変わるたびに増える通し番号。
            /// 自分で書き戻した変更かどうかを、時間に頼らず確定的に判定するために使う
            /// (1回の書き込みでWM_CLIPBOARDUPDATEが複数回来ても、番号で区別できる)。
            /// </summary>
            [DllImport("user32.dll")]
            public static extern uint GetClipboardSequenceNumber();

            // Excelの図形など、OLEオブジェクトを構成する非標準フォーマット(Embed Source/
            // Object Descriptor/Biff12等)を生バイト列のまま読み書きするための、生のWin32
            // クリップボードAPI。WinRTのClipboard/DataPackageはこれら非標準フォーマットを
            // 扱えないため、完全な形での保存・貼り戻しにはこちらを直接使う必要がある

            [DllImport("user32.dll", SetLastError = true)]
            public static extern bool OpenClipboard(IntPtr hWndNewOwner);

            [DllImport("user32.dll", SetLastError = true)]
            public static extern bool CloseClipboard();

            [DllImport("user32.dll", SetLastError = true)]
            public static extern bool EmptyClipboard();

            [DllImport("user32.dll", SetLastError = true)]
            public static extern uint EnumClipboardFormats(uint format);

            [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern int GetClipboardFormatName(uint format, StringBuilder lpszFormatName, int cchMaxCount);

            [DllImport("user32.dll", SetLastError = true)]
            public static extern IntPtr GetClipboardData(uint uFormat);

            [DllImport("user32.dll", SetLastError = true)]
            public static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

            [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern uint RegisterClipboardFormat(string lpszFormat);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern IntPtr GlobalLock(IntPtr hMem);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool GlobalUnlock(IntPtr hMem);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern UIntPtr GlobalSize(IntPtr hMem);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern IntPtr GlobalFree(IntPtr hMem);

            public const uint GMEM_MOVEABLE = 0x0002;
        }


        public class HotKeyNative
        {
            public class HotKeyModifiers
            {
                public const uint MOD_ALT = 0x0001;
                public const uint MOD_CONTROL = 0x0002;
                public const uint MOD_SHIFT = 0x0004;
                public const uint MOD_WIN = 0x0008;

                // 押しっぱなしによるWM_HOTKEYの連続発火を抑止する(Windows 7以降)
                public const uint MOD_NOREPEAT = 0x4000;
            }

            [DllImport("user32.dll")]
            public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

            [DllImport("user32.dll")]
            public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        }

        public class Dpi
        {
            [DllImport("user32.dll")]
            public static extern uint GetDpiForWindow(IntPtr hwnd);
        }


        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(
            IntPtr hWnd,
            uint Msg,
            IntPtr wParam,
            IntPtr lParam);

        // WM_GETTEXT等、文字列バッファをlParamで受け取るメッセージ用のオーバーロード
        // (SelectionWatcherのEdit/RichEditフォールバック参照)
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
        public static extern IntPtr SendMessage(
            IntPtr hWnd,
            uint Msg,
            IntPtr wParam,
            System.Text.StringBuilder lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PostMessage(
            IntPtr hWnd,
            uint Msg,
            IntPtr wParam,
            IntPtr lParam);

        /// <summary>
        /// 全プロセス共通で同じIDになるメッセージ番号を発行する。多重起動防止で、
        /// 別プロセスへ「前面に出して」を伝えるために使う(WM_APPなどの固定値は
        /// 他アプリと衝突しうるため、文字列キーで一意な番号を割ってもらう)。
        /// </summary>
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern uint RegisterWindowMessage(string lpString);

        /// <summary>PostMessageのhWndにこれを渡すと、全トップレベルウィンドウへブロードキャストする。</summary>
        public static readonly IntPtr HWND_BROADCAST = new IntPtr(0xffff);

        public const uint WM_NCLBUTTONDOWN = 0xA1;
        public static readonly IntPtr HTCAPTION = new IntPtr(2);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;

        public const int WS_MINIMIZEBOX = 0x00020000;
        public const int WS_MAXIMIZEBOX = 0x00010000;

        /// <summary>
        /// このスタイルを付けたウィンドウは、表示・クリックしてもフォーカス(アクティブ)を
        /// 奪わない。Explorerのファイル名変更中など、相手の入力状態を壊さずに
        /// 前面へ出したい場合に使う。
        /// </summary>
        public const int WS_EX_NOACTIVATE = 0x08000000;

        /// <summary>タスクバーやAlt+Tabの切り替え一覧に出さないためのスタイル。</summary>
        public const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        /// <summary>SetWindowPosのhWndInsertAfterに渡すと、常に最前面(topmost)へ移す。</summary>
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        /// <summary>SetWindowPosのhWndInsertAfterに渡すと、topmost状態を解除して通常のz順位に戻す。</summary>
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out Structs.RECT lpRect);

        /// <summary>
        /// 低レベルキーボードフック。フォーカスを持たないウィンドウでも、
        /// システム全体のキー入力を監視するために使う。
        /// </summary>
        public class KeyboardHook
        {
            public const int WH_KEYBOARD_LL = 13;
            public const int HC_ACTION = 0;

            public const int WM_KEYDOWN = 0x0100;
            public const int WM_KEYUP = 0x0101;
            public const int WM_SYSKEYDOWN = 0x0104;
            public const int WM_SYSKEYUP = 0x0105;

            public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

            [DllImport("user32.dll", SetLastError = true)]
            public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

            [DllImport("user32.dll", SetLastError = true)]
            public static extern bool UnhookWindowsHookEx(IntPtr hhk);

            [DllImport("user32.dll")]
            public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        }

        /// <summary>
        /// 低レベルマウスフック。フォーカスを持たないウィンドウでも、
        /// 「ウィンドウの外がクリックされた」ことを検知するために使う。
        /// </summary>
        public class MouseHook
        {
            public const int WH_MOUSE_LL = 14;

            public const int WM_LBUTTONDOWN = 0x0201;
            public const int WM_LBUTTONUP = 0x0202;
            public const int WM_RBUTTONDOWN = 0x0204;
            public const int WM_MBUTTONDOWN = 0x0207;

            /// <summary>OSのダブルクリック判定間隔(ミリ秒)。中ボタンのダブルクリック検出に使う。</summary>
            [DllImport("user32.dll")]
            public static extern uint GetDoubleClickTime();

            public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

            [DllImport("user32.dll", SetLastError = true)]
            public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

            [DllImport("user32.dll", SetLastError = true)]
            public static extern bool UnhookWindowsHookEx(IntPtr hhk);

            [DllImport("user32.dll")]
            public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        }

        /// <summary>
        /// フォアグラウンドウィンドウの切り替わりを監視するためのフック。
        /// WS_EX_NOACTIVATEのウィンドウは自分がアクティブにならないため
        /// Deactivatedイベントが来ず、「他アプリへ移った」検知にはこれを使う。
        /// </summary>
        public class WinEventHook
        {
            public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
            public const uint WINEVENT_OUTOFCONTEXT = 0x0000;

            public delegate void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
                int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

            [DllImport("user32.dll", SetLastError = true)]
            public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax,
                IntPtr hmodWinEventProc, WinEventProc lpfnWinEventProc,
                uint idProcess, uint idThread, uint dwFlags);

            [DllImport("user32.dll", SetLastError = true)]
            public static extern bool UnhookWinEvent(IntPtr hWinEventHook);
        }
    }

}
