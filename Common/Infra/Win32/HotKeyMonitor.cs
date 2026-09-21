using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WinAPI;
using Windows.UI.Popups;
using Windows.UI.UIAutomation;
using static WinAPI.Structs;
using static WinAPI.WinUser;

namespace Common.Infra.Win32
{
    /// <summary>
    /// RegisterHotKeyに渡す修飾キー(Modifiers)と仮想キーコード(VirtualKey)の組。
    /// 値が同じであれば同一のホットキーとして扱われる(recordなので値比較)。
    /// </summary>
    public record HotKeyDefinition(uint Modifiers, uint VirtualKey);

    /// <summary>
    /// Win32のRegisterHotKey/UnregisterHotKeyを薄くラップしたもの。
    /// </summary>
    public static class HotKeyRegistrar
    {
        public static bool Register(IntPtr hWnd, int id, HotKeyDefinition definition)
        {
            return HotKeyNative.RegisterHotKey(hWnd, id, definition.Modifiers, definition.VirtualKey);
        }

        public static void Unregister(IntPtr hWnd, int id)
        {
            HotKeyNative.UnregisterHotKey(hWnd, id);
        }
    }

    /// <summary>
    /// WindowMessageDispatcherを通じてWM_HOTKEYを受け取り、登録済みのコールバックを呼び出す。
    /// 1つのHotKeyDefinitionに対して複数のコールバックを登録できる(同じショートカットを
    /// 複数の場所から監視したいケースに対応)。
    /// </summary>
    public class HotKeyMonitor : IDisposable
    {
        /// <summary>
        /// RegisterHotKeyに渡すid(int)を発行する。RegisterHotKeyのidはプロセス内で一意であれば
        /// 良いため、アプリ全体で1つのジェネレータを共有してかぶらないようにする。
        /// </summary>
        private sealed class HotKeyIdGenerator
        {
            private static readonly Lazy<HotKeyIdGenerator> _instance
                = new(() => new HotKeyIdGenerator());

            public static HotKeyIdGenerator Instance => _instance.Value;

            private int _currentId = 0;

            private HotKeyIdGenerator()
            {
            }

            public int Next()
            {
                return Interlocked.Increment(ref _currentId);
            }
        }

        public delegate void HotKeyCallback();

        // RegisterHotKeyのid -> そのidに紐づくコールバック一覧
        private readonly Dictionary<int, List<HotKeyCallback>> _callbacks = new();

        // どのHotKeyDefinitionにどのidを割り当てたか(Register/Unregisterで定義からidを逆引きするため)
        private readonly Dictionary<HotKeyDefinition, int> _definitionToId = new();

        private readonly WindowMessageDispatcher _dispatcher;

        public HotKeyMonitor(WindowMessageDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            _dispatcher.AddHandler(WindowMessages.WM_HOTKEY, OnHotKeyMessage);
        }

        /// <summary>
        /// 指定したショートカットにコールバックを登録する。
        /// 同じdefinitionが既に登録されていれば、新たにRegisterHotKeyは呼ばず
        /// (Win32側は同一ホットキーの多重登録ができないため)、既存のidにコールバックを追加するだけにする。
        /// </summary>
        public void Register(HotKeyDefinition definition, HotKeyCallback callback)
        {
            HotKeyIdGenerator keyIdGen = HotKeyIdGenerator.Instance;

            // 既に同じ定義で登録済みなら同一の id を使う
            if (!_definitionToId.TryGetValue(definition, out var id)) {
                id = keyIdGen.Next();
                _definitionToId[definition] = id;

                var list = new List<HotKeyCallback> { callback };
                _callbacks[id] = list;

                HotKeyRegistrar.Register(_dispatcher.WindowHandler, id, definition);
            } else {
                if (!_callbacks.TryGetValue(id, out var list)) {
                    list = new List<HotKeyCallback>();
                    _callbacks[id] = list;
                }

                list.Add(callback);
            }
        }

        // コールバック単体を解除する既存メソッド
        // 指定したコールバックだけを解除する。同じdefinitionに他のコールバックが
        // まだ残っていればRegisterHotKey自体は維持し、誰もいなくなったときだけ
        // Win32のホットキー登録を解除する。
        public bool Unregister(HotKeyDefinition definition, HotKeyCallback callback)
        {
            if (!_definitionToId.TryGetValue(definition, out var id))
                return false;

            if (!_callbacks.TryGetValue(id, out var list))
                return false;

            bool removed = list.Remove(callback);

            // コールバックが残っていなければ HotKey を解除して関連データをクリーンアップ
            if (list.Count == 0) {
                _callbacks.Remove(id);
                _definitionToId.Remove(definition);
                HotKeyRegistrar.Unregister(_dispatcher.WindowHandler, id);
            }

            return removed;
        }

        // コールバック指定なしで定義を完全に解除するメソッド
        // そのdefinitionに紐づくコールバックを全て無視して、Win32の登録も即座に解除する。
        public bool Unregister(HotKeyDefinition definition)
        {
            if (!_definitionToId.TryGetValue(definition, out var id))
                return false;

            if (!_callbacks.ContainsKey(id))
                return false;

            _callbacks.Remove(id);
            _definitionToId.Remove(definition);
            HotKeyRegistrar.Unregister(_dispatcher.WindowHandler, id);
            return true;
        }

        /// <summary>
        /// WindowMessageDispatcherからWM_HOTKEYのハンドラを外す。
        /// 個々のホットキーのRegisterHotKey解除はしないため、呼び出し側で
        /// 必要ならUnregisterを先に呼んでおくこと。
        /// </summary>
        public void Dispose() {
            _dispatcher.RemoveHandler(WindowMessages.WM_HOTKEY, OnHotKeyMessage);
        }

        // WM_HOTKEYのwParamに入っているidから対象のコールバックを引いて呼び出す。
        // コールバック側の例外は他のコールバックの実行やメッセージループを止めないよう、
        // ここで握りつぶしてDebug出力するだけにしている。
        private bool OnHotKeyMessage(IntPtr wParam, IntPtr lParam) {
            int id = wParam.ToInt32();
            try {
                if (_callbacks.TryGetValue(id, out var callbacks)) {
                    foreach (var callback in callbacks) {
                        try {
                            callback();
                        }
                        catch (Exception ex) {
                            Debug.WriteLine(ex);
                        }
                    }
                }
            } catch (Exception ex) {
                Debug.WriteLine(ex);
            }

            return false;
        }

    }
}


    //class HotKeyProc
    //{
    //    private delegate IntPtr WndProcDelegate(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    //    public delegate void HotKeyCallback(int left, int top);

    //    private IntPtr _oldWndProc;
    //    private WndProcDelegate? _newWndProc;

    //    private IntPtr hWnd;

    //    private const int WM_HOTKEY = 0x0312;
    //    private Dictionary<int, HotKeyCallback> _hotKeyCallbacks = new Dictionary<int, HotKeyCallback>();

    //    public HotKeyProc(IntPtr hWnd)
    //    {
    //        _oldWndProc = IntPtr.Zero;
    //        _newWndProc = null;
    //        this.hWnd = hWnd;
    //    }

    //    public void RegisterHotKey(int id, HotKeyCallback callback)
    //    {
    //        _hotKeyCallbacks.Add(id, callback);

    //        _newWndProc = WndProc;

    //        // ホットキーを登録
    //        // Ctrl + Shift + A
    //        HotKeyNative.RegisterHotKey(this.hWnd,
    //            id,
    //            HotKeyNative.HotKeyModifiers.MOD_CONTROL | HotKeyNative.HotKeyModifiers.MOD_SHIFT,
    //            0x41);
    //        // ウィンドウプロシージャをフック
    //        _oldWndProc = WinUser.SetWindowLongPtr(this.hWnd, WindowLongIndex.GWL_WNDPROC, Marshal.GetFunctionPointerForDelegate(_newWndProc));
    //    }

    //    public void UnregisterHotKey(int id)
    //    {
    //        if (_hotKeyCallbacks.ContainsKey(id))
    //        {
    //            _hotKeyCallbacks.Remove(id);
    //        }
    //        // ホットキーを解除
    //        HotKeyNative.UnregisterHotKey(hWnd, id);
    //        // ウィンドウプロシージャのフックを解除
    //        WinUser.SetWindowLongPtr(hWnd, WindowLongIndex.GWL_WNDPROC, _oldWndProc);
    //    }

    //    private IntPtr WndProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam)
    //    {
    //        //　ホットキーが押されたときの処理
    //        if (msg == WM_HOTKEY)
    //        {
    //            int id = wParam.ToInt32();
    //            Rectangle? caretRect = null;
    //            if (id == 1)
    //            {
    //                // フォーカスのあるウィンドウのスレッドIDを取得
    //                var foregroundHandleWnd = WinUser.GetForegroundWindow();
    //                caretRect = GetCaretRectByGuiThreadInfo(foregroundHandleWnd);
    //            }

    //            if (_hotKeyCallbacks.TryGetValue(id, out var callback))
    //            {
    //                callback(caretRect?.Left ?? 0, caretRect?.Top ?? 0);
    //            }


    //        }

    //        // 必ず元に流す
    //        return WinUser.CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
    //    }



    //    // --- Win32 fallback using GUITHREADINFO ---
    //    public static Rectangle? GetCaretRectByGuiThreadInfo(IntPtr hwnd)
    //    {
    //        if ( hwnd == IntPtr.Zero ) return null;
    //        uint threadId = GetWindowThreadProcessId( hwnd, out _ );

    //        var gui = new GUITHREADINFO();
    //        gui.cbSize = Marshal.SizeOf<GUITHREADINFO>();
    //        if ( !GetGUIThreadInfo( threadId, ref gui ) ) return null;

    //        // rcCaret はクライアント座標。左上と右下を ClientToScreen で変換する
    //        var leftTop = new POINT { X = gui.rcCaret.Left, Y = gui.rcCaret.Top };
    //        var rightBottom = new POINT { X = gui.rcCaret.Right, Y = gui.rcCaret.Bottom };

    //        if ( !WinUser.ClientToScreen( hwnd, ref leftTop ) ) return null;
    //        if ( !WinUser.ClientToScreen( hwnd, ref rightBottom ) ) return null;

    //        int x = leftTop.X;
    //        int y = leftTop.Y;
    //        int w = Math.Max( 0, rightBottom.X - leftTop.X );
    //        int h = Math.Max( 0, rightBottom.Y - leftTop.Y );

    //        return new Rectangle( x, y, w, h );
    //    }
    //}
