using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using WinAPI;
using static WinAPI.WinUser;

namespace Common.Infra.Win32
{
    public class WindowMessageDispatcher : IDisposable
    {
        public delegate IntPtr WndProcDelegate(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        private readonly IntPtr _hWnd;
        public IntPtr WindowHandler { get { return _hWnd; } private set { } }
        private readonly IntPtr _oldWndProc;
        private readonly WndProcDelegate _newWndProc;
        private bool _disposed;
        private readonly Dictionary<int, List<Func<IntPtr, IntPtr, bool>>> _handlers = new();


        public WindowMessageDispatcher(IntPtr hWnd)
        {
            _hWnd = hWnd;
            _newWndProc = WndProc;
            _oldWndProc = SetWindowLongPtr(_hWnd, WindowLongIndex.GWL_WNDPROC, Marshal.GetFunctionPointerForDelegate(_newWndProc));
        }

        public void AddHandler(int message, Func<IntPtr, IntPtr, bool> handler)
        {
            if (!_handlers.TryGetValue(message, out var list)) {
                list = new List<Func<IntPtr, IntPtr, bool>>();
                _handlers[message] = list;
            }
            list.Add(handler);
        }

        public void RemoveHandler(int message, Func<IntPtr, IntPtr, bool> handler)
        {
            if (_handlers.TryGetValue(message, out var list))
            {
                list.Remove(handler);

                if (list.Count == 0)
                {
                    _handlers.Remove(message);
                }
            }
        }



        private IntPtr WndProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            if (_handlers.TryGetValue(msg, out var handlers))
            {
                foreach (var handler in handlers)
                {
                    try
                    {
                        bool handled = handler(wParam, lParam);

                        if (handled)
                        {
                            return IntPtr.Zero;
                        }
                    }
                    catch(Exception ex)
                    {
                        Debug.WriteLine(ex);    
                    }
                }
            }

            return CallWindowProc(_oldWndProc,hWnd, msg, wParam, lParam);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_hWnd != IntPtr.Zero && _oldWndProc != IntPtr.Zero)
            {
                SetWindowLongPtr(_hWnd, WindowLongIndex.GWL_WNDPROC, _oldWndProc);
            }

            _handlers.Clear();

            GC.SuppressFinalize(this);
        }

        ~WindowMessageDispatcher()
        {
            Dispose();
        }
    }
}
