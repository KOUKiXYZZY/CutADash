using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using WinAPI;
using static WinAPI.WinUser;

namespace SelectionToolbar
{
    /// <summary>
    /// システム全体のマウス押下を監視し、指定したウィンドウの外がクリックされたことを通知する。
    /// CutADash本体のOutsideClickWatcherと同じ役割だが、プロジェクトを分けているため
    /// (循環参照を避けるため)ここに同等のものを持つ。
    /// </summary>
    internal sealed class OutsideClickWatcher : IDisposable
    {
        private readonly MouseHook.LowLevelMouseProc _proc;
        private IntPtr _hookHandle;
        private IntPtr _targetWindow;

        public event EventHandler? ClickedOutside;

        public OutsideClickWatcher()
        {
            _proc = OnMouseEvent;
        }

        public bool IsRunning => _hookHandle != IntPtr.Zero;

        public void Start(IntPtr targetWindow)
        {
            _targetWindow = targetWindow;

            if (IsRunning)
                return;

            var moduleHandle = Kernel32.GetModuleHandle(null);
            _hookHandle = MouseHook.SetWindowsHookEx(MouseHook.WH_MOUSE_LL, _proc, moduleHandle, 0);

            if (_hookHandle == IntPtr.Zero)
                Debug.WriteLine("[OutsideClickWatcher] SetWindowsHookExに失敗しました");
        }

        public void Stop()
        {
            if (!IsRunning)
                return;

            MouseHook.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }

        private IntPtr OnMouseEvent(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && IsButtonDown((int)wParam) && IsOutsideTarget(lParam))
            {
                try
                {
                    ClickedOutside?.Invoke(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            }

            return MouseHook.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        private static bool IsButtonDown(int message)
            => message == MouseHook.WM_LBUTTONDOWN
            || message == MouseHook.WM_RBUTTONDOWN
            || message == MouseHook.WM_MBUTTONDOWN;

        private bool IsOutsideTarget(IntPtr lParam)
        {
            if (_targetWindow == IntPtr.Zero)
                return false;

            if (!GetWindowRect(_targetWindow, out var rect))
                return false;

            var x = Marshal.ReadInt32(lParam, 0);
            var y = Marshal.ReadInt32(lParam, 4);

            return x < rect.Left || x >= rect.Right || y < rect.Top || y >= rect.Bottom;
        }

        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }

        ~OutsideClickWatcher()
        {
            Stop();
        }
    }
}
