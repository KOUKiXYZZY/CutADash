using CutADash.Infra.Win32;
using System;

namespace CutADash.Utils
{
    /// <summary>
    /// ペースト先(パレットを出す直前にフォアグラウンドだった外部アプリ)の記憶と、
    /// ペースト実行中かどうかの状態を保持する。
    /// </summary>
    public sealed class PasteTargetTracker
    {
        /// <summary>
        /// このウィンドウをアクティブ化する直前にフォアグラウンドだったウィンドウ。
        /// History一覧でEnterを押したときのペースト先として使う。
        /// </summary>
        public IntPtr ForegroundWindow { get; private set; }

        /// <summary>
        /// ForegroundWindow内で実際にキーボードフォーカスを持っていた子コントロール
        /// (MDI/タブ構成のアプリではフレームウィンドウ自体とは別)。サクラエディタ等、
        /// フレームをフォアグラウンド化しただけでは編集領域にフォーカスが戻らないアプリのために、
        /// ペースト時に明示的にSetFocusし直す(Dittoのm_focusWndと同じ発想)
        /// </summary>
        public IntPtr FocusWindow { get; private set; }

        /// <summary>
        /// ペースト実行中(ForegroundPasteHelper.PasteToPreviousWindowAsync)はtrue。
        /// サクラエディタ等のMDIアプリでは、フォアグラウンド復帰時に想定先とは別の
        /// ウィンドウハンドルでEVENT_SYSTEM_FOREGROUNDが発火することがあり、
        /// OnForegroundChangedが誤ってHidePalette()を呼んでCtrl+V送信を
        /// 中断させてしまう。ペースト中はこの監視を無視する
        /// </summary>
        public bool IsPasting { get; set; }

        /// <summary>
        /// アクティブ化する時点のフォアグラウンドウィンドウを、ペースト先として記憶する。
        ///
        /// 自分自身のウィンドウは記憶しない。ホットキー連打やタブ切り替えでの再呼び出し時は
        /// フォアグラウンドが既に自分自身になっていることがあり、それを記憶すると
        /// 貼り付け先が自分自身になってEnterを押しても何も起きなくなるため。
        ///
        /// 以前はこれを「非表示→表示の遷移の時だけ記憶する」という表示状態での判定で
        /// 回避していたが、表示済みの状態から呼ばれ続けると一度も記憶されないまま
        /// ForegroundWindowがIntPtr.Zeroで固定され、クリップボードへの書き戻しは
        /// 成功するのにキー送信が一切行われない(=コピーはされるが貼り付かない)状態に
        /// 陥っていた。表示状態ではなく「自分のプロセスのウィンドウか」で判定することで、
        /// 自己記憶を防ぎつつ、外部アプリが前面にいる限り常に最新の相手を記憶できる
        /// (Dittoも同様に、自分のウィンドウがフォアグラウンドの間は追跡を更新しない)
        /// </summary>
        public void Remember(IntPtr foregroundWindow)
        {
            if (foregroundWindow == IntPtr.Zero || IsOwnWindow(foregroundWindow))
                return;

            ForegroundWindow = foregroundWindow;
            FocusWindow = ForegroundPaster.GetFocusedChildWindow(foregroundWindow);
        }

        /// <summary>指定したウィンドウが自分のプロセスのものかどうか。</summary>
        private static bool IsOwnWindow(IntPtr hWnd)
        {
            WinAPI.WinUser.GetWindowThreadProcessId(hWnd, out var processId);
            return processId == (uint)Environment.ProcessId;
        }
    }
}
