using System;
using System.Threading.Tasks;
using static WinAPI.Structs;
using static WinAPI.WinUser;

namespace CutADash.Infra.Win32
{
    /// <summary>
    /// 指定したウィンドウをフォアグラウンドへ戻し、Ctrl+Vのキー入力を送ってペーストさせる。
    /// </summary>
    internal static class ForegroundPaster
    {
        /// <summary>SetForegroundWindow後、実際にフォアグラウンドが入れ替わるまで待つ最大時間(ミリ秒)。</summary>
        private const int ActivateTimeoutMs = 500;

        /// <summary>
        /// フォアグラウンドの切り替わりを確認してから、実際にCtrl+Vを送るまでの待ち時間(ミリ秒)。
        ///
        /// 貼り付け先アプリは、こちらのクリップボード書き込みによるWM_CLIPBOARDUPDATEを
        /// 受け取って自分の状態を更新し終えるまで、ペーストを受け付けられないことがある。
        /// 特にExcelでセルをコピーした直後(点線の選択枠が出ている状態)は、その通知で
        /// 自分のコピーモードを解除してペーストの可否を再評価するまでの間にCtrl+Vが届くと、
        /// 何も起きずに終わってしまう。
        ///
        /// Dittoも同じ位置に固定の待ち(SendKeysDelay、既定100ms)を入れているため、それに倣う
        /// </summary>
        private const int PasteSettleDelayMs = 100;

        /// <summary>Ctrl+Vのキーを押してから離すまでの時間(ミリ秒)。</summary>
        private const int KeyDownDurationMs = 30;

        /// <summary>
        /// 指定したトップレベルウィンドウ内で、実際にキーボードフォーカスを持っている
        /// 子コントロールを取得する。MDI/タブ構成のアプリ(サクラエディタ等)では、
        /// このフレームウィンドウ自体をフォアグラウンド化しただけでは編集領域に
        /// フォーカスが戻らないことがあるため、ペースト時にこれへ明示的にSetFocusし直す。
        /// 取得できない場合はforegroundWindow自身を返す
        /// </summary>
        public static IntPtr GetFocusedChildWindow(IntPtr foregroundWindow)
        {
            if (foregroundWindow == IntPtr.Zero)
                return IntPtr.Zero;

            var threadId = GetWindowThreadProcessId(foregroundWindow, out _);
            var info = new GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<GUITHREADINFO>() };

            if (threadId != 0 && GetGUIThreadInfo(threadId, ref info) && info.hwndFocus != IntPtr.Zero)
                return info.hwndFocus;

            return foregroundWindow;
        }

        public static async Task ActivateAndPasteAsync(IntPtr targetHwnd, IntPtr focusHwnd = default)
        {
            if (targetHwnd == IntPtr.Zero)
                return;

            ReleaseAllPressedKeys();

            Utils.PasteDiagnosticsLog.Write(
                $"[Paster] 開始 target={Utils.PasteDiagnosticsLog.Describe(targetHwnd)} " +
                $"focus={Utils.PasteDiagnosticsLog.Describe(focusHwnd)} " +
                $"現在の前面={Utils.PasteDiagnosticsLog.Describe(GetForegroundWindow())}");

            uint previousLockTimeout = 0;
            var lockTimeoutSaved = SystemParametersInfo(SPI_GETFOREGROUNDLOCKTIMEOUT, 0, ref previousLockTimeout, 0);
            SystemParametersInfo(SPI_SETFOREGROUNDLOCKTIMEOUT, 0, IntPtr.Zero, 0);

            try
            {
                // 重いアプリ/MDIアプリ(サクラエディタ等)では、SetForegroundWindowを呼んでも
                // 実際にフォアグラウンドが切り替わるまでに時間がかかることがある。固定時間
                // 待つだけでなく、実際に切り替わったことを確認できるまでポーリングで再試行する
                var start = Environment.TickCount;
                var attempts = 0;
                while (GetForegroundWindow() != targetHwnd
                    && Environment.TickCount - start < ActivateTimeoutMs)
                {
                    BringToForeground(targetHwnd);
                    attempts++;
                    await Task.Delay(15);
                }

                Utils.PasteDiagnosticsLog.Write(
                    $"[Paster] 前面化 試行={attempts}回 {Environment.TickCount - start}ms " +
                    $"結果={Utils.PasteDiagnosticsLog.Describe(GetForegroundWindow())} " +
                    $"一致={GetForegroundWindow() == targetHwnd}");

                // 貼り付け先がクリップボードの変更を処理し終えるのを待つ
                await Task.Delay(PasteSettleDelayMs);

                // フレームをフォアグラウンドにしただけでは、MDI/タブ構成のアプリで
                // 編集領域(子コントロール)にフォーカスが戻っていないことがあるため、
                // 記憶しておいた子コントロールへ明示的にフォーカスを戻す。
                //
                // 前面化の直後ではなく、ここまで待ってから行うのが重要。対象アプリ自身の
                // アクティブ化処理(WM_ACTIVATEに伴うフォーカス設定)が終わる前にSetFocusすると
                // こちらの指定が上書きされ、編集領域にフォーカスが載らないままCtrl+Vが届いて
                // 何も起きない。サクラエディタで実際にこれを踏んだ
                RestoreFocus(targetHwnd, focusHwnd);

                // 押下(Ctrl↓V↓)と解放(V↑Ctrl↑)は、それぞれ1回のSendInputでまとめて送る。
                // SendInputは「まとめた分の間に他の入力が割り込まない」ことを保証するため、
                // 1イベントずつ送るkeybd_eventだと間に割り込まれてCtrlの押下が途中で
                // 欠落し、Vだけが単独入力として扱われることがあった。
                //
                // 一方で4つすべてを1回で送ると押下から解放までの時間差が実質ゼロになり、
                // メッセージループでGetKeyStateを見て修飾キーを判定するアプリが
                // Ctrl+Vとして認識し損ねるため、押下と解放の間には間隔を空ける
                var downInputs = new[]
                {
                    KeyInput(VK_CONTROL, down: true),
                    KeyInput(VK_V, down: true),
                };

                var upInputs = new[]
                {
                    KeyInput(VK_V, down: false),
                    KeyInput(VK_CONTROL, down: false),
                };

                var sentDown = SendInput((uint)downInputs.Length, downInputs, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());
                await Task.Delay(KeyDownDurationMs);
                var sentUp = SendInput((uint)upInputs.Length, upInputs, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());

                Utils.PasteDiagnosticsLog.Write(
                    $"[Paster] Ctrl+V送信 押下={sentDown}/2 解放={sentUp}/2 " +
                    $"送信時の前面={Utils.PasteDiagnosticsLog.Describe(GetForegroundWindow())}");

                // キーはイベントを入力キューに積んだ時点で即座に返るため、対象アプリが
                // 処理し終える前にこちらの後片付けへ進まないよう、少しだけ待つ
                await Task.Delay(50);
            }
            finally
            {
                if (lockTimeoutSaved)
                    SystemParametersInfo(SPI_SETFOREGROUNDLOCKTIMEOUT, 0, ref previousLockTimeout, 0);
            }
        }

        /// <summary>
        /// 対象ウィンドウを前面化する。
        ///
        /// SetForegroundWindowは、直前の入力がキー操作でないと拒否されることがある
        /// (フォアグラウンドロック)。そのため対象スレッドの入力キューへ一時的にアタッチするが、
        /// アタッチ中は2つのスレッドが入力キューとキーボード状態を共有してしまうため、
        /// 前面化を終えたら即座に解除する。キー送信までアタッチしたままにすると、
        /// 貼り付け先が見る修飾キーの状態が通常と変わり、動作が不安定になる
        /// (Dittoも前面化の前後だけアタッチし、キー送信時には解除している)。
        /// </summary>
        private static void BringToForeground(IntPtr targetHwnd)
        {
            var targetThreadId = GetWindowThreadProcessId(targetHwnd, out _);
            var currentThreadId = GetCurrentThreadId();

            var attached = targetThreadId != 0
                && targetThreadId != currentThreadId
                && AttachThreadInput(targetThreadId, currentThreadId, true);

            try
            {
                // z順を上げてから前面化する方が確実に切り替わる
                BringWindowToTop(targetHwnd);
                SetForegroundWindow(targetHwnd);
            }
            finally
            {
                if (attached)
                    AttachThreadInput(targetThreadId, currentThreadId, false);
            }
        }

        /// <summary>
        /// 記憶しておいた子コントロールへフォーカスを戻す。
        /// 既にそこへ載っていれば何もしない(余計なSetFocusで対象アプリの状態を乱さない)。
        /// </summary>
        private static void RestoreFocus(IntPtr targetHwnd, IntPtr focusHwnd)
        {
            if (focusHwnd == IntPtr.Zero || focusHwnd == targetHwnd)
                return;

            var threadId = GetWindowThreadProcessId(targetHwnd, out _);
            if (threadId == 0)
                return;

            var info = new GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<GUITHREADINFO>() };
            if (GetGUIThreadInfo(threadId, ref info) && info.hwndFocus == focusHwnd)
            {
                Utils.PasteDiagnosticsLog.Write("[Paster] フォーカスは既に目的の位置にある");
                return;
            }

            SetFocus(focusHwnd);

            Utils.PasteDiagnosticsLog.Write(
                $"[Paster] フォーカスを戻した 現在={(GetGUIThreadInfo(threadId, ref info) ? Utils.PasteDiagnosticsLog.Describe(info.hwndFocus) : "取得不可")}");
        }

        /// <summary>
        /// 現在押下されているキーをすべて解放する。
        ///
        /// 修飾キーだけでなく通常キーも対象にするのが重要。ペーストはパレット上での
        /// Enter押下から始まるが、一連の処理は200ms程度で終わるため、Ctrl+Vを送る時点では
        /// まだEnterが物理的に押されたままのことが多い。押しっぱなしのキーが残っていると
        /// 貼り付け先がCtrl+Vを正しく認識しないこと(特にExcel)があるため、先に解放しておく
        /// (Dittoも同じ理由でAllKeysUpとして全キーを解放してから貼り付けを送っている)
        /// </summary>
        private static void ReleaseAllPressedKeys()
        {
            var upInputs = new System.Collections.Generic.List<INPUT>();

            // 0x07以下はマウスボタン(VK_LBUTTON等)なので対象外にする。
            // 一覧をダブルクリックしてペーストした場合はマウスのボタンが押下状態だが、
            // そこへキーボードのキーアップを送るのは意味がなく、入力状態を壊しかねない
            for (var vk = 0x08; vk < 256; vk++)
            {
                // 最上位ビットが立っていれば、そのキーは現在物理的に押下中
                if ((GetAsyncKeyState(vk) & 0x8000) != 0)
                    upInputs.Add(KeyInput((byte)vk, down: false));
            }

            if (upInputs.Count == 0)
                return;

            Utils.PasteDiagnosticsLog.Write($"[Paster] 押下中のキーを解放 数={upInputs.Count}");
            SendInput((uint)upInputs.Count, upInputs.ToArray(), System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());
        }

        private static INPUT KeyInput(byte virtualKey, bool down) => new()
        {
            type = INPUT_KEYBOARD,
            ki = new KEYBDINPUT
            {
                wVk = virtualKey,
                // スキャンコードが0のままだと、ハードウェア入力かどうかをスキャンコードで
                // 判定するアプリ(Chromium系など)にCtrlの押下が伝わらないことがあるため設定する
                wScan = (ushort)MapVirtualKey(virtualKey, MAPVK_VK_TO_VSC),
                dwFlags = down ? 0 : KEYEVENTF_KEYUP,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        };
    }
}
