using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using WinAPI;
using static WinAPI.WinUser;

namespace SelectionToolbar
{
    /// <summary>
    /// システム全体のキー入力を監視し、Escapeが押されたことを通知する。
    /// SelectionToolbarWindowはWS_EX_NOACTIVATEでフォーカスを持たないため、
    /// XAML側のKeyDown等では拾えず、OutsideClickWatcherと同じく低レベルフックで検知する。
    /// キーは握りつぶさず素通しするため、前面アプリ側の通常のEscape処理は妨げない。
    /// </summary>
    internal sealed class EscapeKeyWatcher : IDisposable
    {
        private const int VkEscape = 0x1B;

        private readonly KeyboardHook.LowLevelKeyboardProc _proc;
        private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;
        private IntPtr _hookHandle;

        public event EventHandler? EscapePressed;

        public EscapeKeyWatcher()
        {
            _proc = OnKeyboardEvent;
            _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        }

        public bool IsRunning => _hookHandle != IntPtr.Zero;

        public void Start()
        {
            if (IsRunning)
                return;

            var moduleHandle = Kernel32.GetModuleHandle(null);
            _hookHandle = KeyboardHook.SetWindowsHookEx(KeyboardHook.WH_KEYBOARD_LL, _proc, moduleHandle, 0);

            if (_hookHandle == IntPtr.Zero)
                Debug.WriteLine("[EscapeKeyWatcher] SetWindowsHookExに失敗しました");
        }

        public void Stop()
        {
            if (!IsRunning)
                return;

            KeyboardHook.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }

        private IntPtr OnKeyboardEvent(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode == KeyboardHook.HC_ACTION)
            {
                var message = (int)wParam;
                if (message == KeyboardHook.WM_KEYDOWN || message == KeyboardHook.WM_SYSKEYDOWN)
                {
                    // KBDLLHOOKSTRUCT: vkCode(0), scanCode(4), flags(8), time(12), dwExtraInfo(16)
                    var vkCode = Marshal.ReadInt32(lParam);
                    if (vkCode == VkEscape)
                    {
                        Debug.WriteLine("[EscapeKeyWatcher] Escape検知");

                        // フックのコールバック内でウィンドウ操作・フック解除をすると不安定に
                        // なりうる(低レベルフックにはタイムアウトもある)ため、UIスレッドへ
                        // 後回しにして、コールバック自体はすぐ返す
                        _dispatcherQueue.TryEnqueue(() => EscapePressed?.Invoke(this, EventArgs.Empty));
                    }
                }
            }

            return KeyboardHook.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }

        ~EscapeKeyWatcher()
        {
            Stop();
        }
    }
}
