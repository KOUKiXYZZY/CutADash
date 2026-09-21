using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using WinAPI;
using static WinAPI.WinUser;

namespace SequentialPaste
{
    /// <summary>
    /// システム全体のキー入力を監視し、Ctrl+Vが押された瞬間を検知する
    /// (SelectionToolbar.SelectionWatcherと同じ、WH_KEYBOARD_LLの低レベルフックを使う仕組み)。
    ///
    /// RegisterHotKeyを試したが、RegisterHotKeyは本物のキー入力とSendInputによる合成入力を
    /// 区別できない。このためActivateAndPasteAsyncが実際の貼り付けのために送る合成Ctrl+Vまで
    /// 自分自身のホットキーとして横取りしてしまい、(1)合成Ctrl+Vが貼り付け先アプリへ一切
    /// 届かない(何もペーストされない)、(2)横取りしたイベントで再度キューが消費される、
    /// という2つの不具合が起きた。
    ///
    /// 低レベルフックのKBDLLHOOKSTRUCT.flagsにはLLKHF_INJECTED(SendInput/keybd_eventによる
    /// 合成入力かどうか)が立っているため、こちらならその場で見分けて合成入力だけ無条件に
    /// 素通しできる。これが低レベルフックに戻した理由。
    ///
    /// 検知した「本物の」物理Ctrl+Vはここで握りつぶし(CallNextHookExを呼ばない)、貼り付け先
    /// アプリへは届かせない。代わりにPasteRequestedを発火するだけに留め、実際の処理
    /// (クリップボードへの書き込み→合成キーでのCtrl+V送信)は購読側
    /// (SequentialPasteWindow)に任せる。ユーザーが押した物理キーをそのまま素通しするのでは
    /// なく、書き込み後に合成キーで貼り付けることで、「クリップボードの更新が終わってから
    /// Ctrl+Vが届く」という順序を保証できる(素通しさせると、更新前の古い内容が
    /// 貼り付けられる可能性がある)。
    /// </summary>
    public sealed class SequentialPasteHotkeyWatcher : IDisposable
    {
        // KBDLLHOOKSTRUCT.flagsのビット。SendInput/keybd_eventによる合成入力ならセットされる
        private const int LLKHF_INJECTED = 0x10;

        private readonly KeyboardHook.LowLevelKeyboardProc _proc;
        private IntPtr _hookHandle;
        private Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue;

        private bool _ctrlDown;

        // Ctrl+Vとして実際に握りつぶした(=PasteRequestedを発火した)最中かどうか。
        // キーを押しっぱなしにするとWM_KEYDOWNが連射されるため、押しっぱなしの間は
        // 再発火させず、対応するキーアップだけ確実に握りつぶすために使う
        private bool _isSwallowingV;

        /// <summary>Ctrl+Vが押されるたびに発火する。UIスレッド上で発火する。</summary>
        public event Action? PasteRequested;

        /// <summary>
        /// キューに項目が残っているかどうかを問い合わせるコールバック。
        /// 空の時は素通しし、通常のCtrl+Vとして貼り付け先へ届かせる
        /// (キューを使い切った後も、普通に貼り付けができなくなると困るため)。
        /// </summary>
        public Func<bool>? HasItems { get; set; }

        public SequentialPasteHotkeyWatcher()
        {
            _proc = OnKeyboardEvent;
        }

        public bool IsRunning => _hookHandle != IntPtr.Zero;

        public void Start()
        {
            if (IsRunning)
                return;

            _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

            var moduleHandle = Kernel32.GetModuleHandle(null);
            _hookHandle = KeyboardHook.SetWindowsHookEx(KeyboardHook.WH_KEYBOARD_LL, _proc, moduleHandle, 0);
            Debug.WriteLine($"[SeqPaste][Hotkey] Start hookHandle={_hookHandle}");
        }

        public void Stop()
        {
            if (!IsRunning)
                return;

            KeyboardHook.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
            _ctrlDown = false;
            _isSwallowingV = false;
        }

        private IntPtr OnKeyboardEvent(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode == KeyboardHook.HC_ACTION)
            {
                var message = (int)wParam;
                var isKeyDown = message == KeyboardHook.WM_KEYDOWN || message == KeyboardHook.WM_SYSKEYDOWN;
                var isKeyUp = message == KeyboardHook.WM_KEYUP || message == KeyboardHook.WM_SYSKEYUP;

                if (isKeyDown || isKeyUp)
                {
                    // KBDLLHOOKSTRUCT: vkCode(0), scanCode(4), flags(8), time(12), dwExtraInfo(16)
                    var vkCode = Marshal.ReadInt32(lParam);
                    var flags = Marshal.ReadInt32(lParam, 8);
                    var isInjected = (flags & LLKHF_INJECTED) != 0;

                    // 自分(ForegroundPaster)がSendInputで送った合成キーは無条件に素通しする。
                    // ここで判定せず素通ししないと、貼り付け用の合成Ctrl+Vまで
                    // 「新たなCtrl+V押下」として扱われ、キューが連鎖的に消費されてしまう
                    if (isInjected)
                        return KeyboardHook.CallNextHookEx(_hookHandle, nCode, wParam, lParam);

                    if (vkCode == VK_CONTROL || vkCode == VK_LCONTROL || vkCode == VK_RCONTROL)
                    {
                        _ctrlDown = isKeyDown;
                    }
                    else if (vkCode == VK_V)
                    {
                        if (isKeyDown && _ctrlDown && !_isSwallowingV)
                        {
                            // キューが空なら素通しし、通常通り今のクリップボードを貼り付けさせる
                            if (HasItems?.Invoke() != true)
                                return KeyboardHook.CallNextHookEx(_hookHandle, nCode, wParam, lParam);

                            _isSwallowingV = true;
                            _dispatcherQueue?.TryEnqueue(() => PasteRequested?.Invoke());
                            return (IntPtr)1;
                        }

                        if (isKeyDown && _isSwallowingV)
                        {
                            // 押しっぱなしによる連射。再発火はさせず、引き続き握りつぶすだけにする
                            return (IntPtr)1;
                        }

                        if (isKeyUp && _isSwallowingV)
                        {
                            _isSwallowingV = false;
                            return (IntPtr)1;
                        }
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

        ~SequentialPasteHotkeyWatcher()
        {
            Stop();
        }
    }
}
