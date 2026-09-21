using Common.Infra.Win32;
using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using WinAPI;
using static WinAPI.Structs;
using static WinAPI.WinUser;

namespace SelectionToolbar
{
    /// <summary>選択範囲が見つかった時に、選択されているテキストと画面上のアンカー位置(マウス位置)を渡す。</summary>
    public sealed class SelectionFoundEventArgs : EventArgs
    {
        public required string Text { get; init; }
        public required int ScreenX { get; init; }
        public required int ScreenY { get; init; }
    }

    /// <summary>
    /// システム全体でマウスの左ボタン離し(ドラッグ選択の完了)を監視し、その時点で
    /// フォーカスされている要素にUI Automation経由でテキスト選択が無いか確認する。
    /// PopClipのような「テキストを選択したら近くに小さなツールバーを出す」機能の入力部分。
    ///
    /// UI Automationにしか対応していないため、対応の広い正攻法ではあるが、
    /// TextPatternを実装しないアプリ(一部の独自描画アプリ等)では検出できない。
    /// </summary>
    public sealed class SelectionWatcher : IDisposable
    {
        // UI AutomationのパターンID。CaretInfoProviderと同じく、
        // ドキュメント化された定数値を直接使う
        private const int UIA_TextPatternId = 10014;

        // 選択テキスト取得時、クリック位置の要素から祖先を辿ってTextPattern実装元を
        // 探す際の上限階層数
        private const int MaxAncestorWalk = 4;

        // ドラッグして選択したのか、ただの1クリックなのかを見分けるための、
        // ボタン押下位置からの許容誤差(px)
        private const int ClickDragThreshold = 2;

        private readonly MouseHook.LowLevelMouseProc _proc;
        private IntPtr _hookHandle;
        private IUIAutomation? _automation;
        private int _downX;
        private int _downY;
        private Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue;

        // マウスドラッグの検知(ポーリング)を補う、UI Automationのイベント購読(プッシュ通知)。
        // ダブルクリックでの単語選択・トリプルクリックでの行選択・Shift+矢印キーでの選択など、
        // マウスドラッグを伴わない選択操作はWM_LBUTTONUPベースの検知では拾えないため、
        // TextSelectionChangedEventをデスクトップ全体(TreeScope_Subtree)で購読して補う。
        // 対応していないアプリでは従来通りマウスドラッグ検知側だけが効く
        private TextSelectionEventHandler? _textSelectionEventHandler;

        /// <summary>ドラッグ選択の完了直後、非空のテキスト選択が見つかった時に発火する。</summary>
        public event EventHandler<SelectionFoundEventArgs>? SelectionFound;

        public SelectionWatcher()
        {
            _proc = OnMouseEvent;
        }

        public bool IsRunning => _hookHandle != IntPtr.Zero;

        public void Start()
        {
            if (IsRunning)
                return;

            // フックコールバックはStart()を呼んだスレッド上で実行される。
            // 後でCheckSelectionAsyncの結果(UI Automation呼び出し完了後)を
            // このスレッド(UIスレッド)へ戻すために保持しておく
            _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

            var moduleHandle = Kernel32.GetModuleHandle(null);
            _hookHandle = MouseHook.SetWindowsHookEx(MouseHook.WH_MOUSE_LL, _proc, moduleHandle, 0);

            if (_hookHandle == IntPtr.Zero)
                Debug.WriteLine("[SelectionWatcher] SetWindowsHookExに失敗しました");

            StartTextSelectionEventHandler();
        }

        public void Stop()
        {
            StopTextSelectionEventHandler();

            if (!IsRunning)
                return;

            MouseHook.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }

        /// <summary>
        /// UI AutomationのTextSelectionChangedEventをデスクトップ全体で購読する。
        /// マウスドラッグ検知では拾えないダブルクリック単語選択・キーボード選択を補うため。
        /// </summary>
        private void StartTextSelectionEventHandler()
        {
            var automation = GetAutomation();
            if (automation is null || _textSelectionEventHandler is not null)
                return;

            try
            {
                var root = automation.GetRootElement();
                if (root is null)
                    return;

                _textSelectionEventHandler = new TextSelectionEventHandler(OnTextSelectionChanged);
                automation.AddAutomationEventHandler(
                    UiaEventIds.UIA_Text_TextSelectionChangedEventId,
                    root,
                    TreeScope.Subtree,
                    IntPtr.Zero,
                    _textSelectionEventHandler);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SelectionWatcher] TextSelectionChangedEventの購読に失敗: {ex}");
                _textSelectionEventHandler = null;
            }
        }

        private void StopTextSelectionEventHandler()
        {
            if (_textSelectionEventHandler is null)
                return;

            // RemoveAutomationEventHandlerは同期COM呼び出しで、デスクトップ全体
            // (TreeScope.Subtree)を対象に購読解除するため、相手プロセスの応答が
            // 遅い/固まっていると長時間ブロックしうる。Stop()はアプリ終了(トレイの「終了」)
            // からも呼ばれ、ここで固まるとEnvironment.Exit(0)まで辿り着けずプロセスが
            // 終了しなくなってしまうため、バックグラウンドで実行して待たない
            var automation = _automation;
            var handler = _textSelectionEventHandler;
            _textSelectionEventHandler = null;

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var root = automation?.GetRootElement();
                    if (automation is not null && root is not null)
                    {
                        automation.RemoveAutomationEventHandler(
                            UiaEventIds.UIA_Text_TextSelectionChangedEventId,
                            root,
                            handler);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SelectionWatcher] TextSelectionChangedEventの購読解除に失敗: {ex}");
                }
            });
        }

        // TextSelectionChangedEventは、ドラッグ中に選択が1文字伸びるたびなど非常に頻繁に
        // 発火する。左ボタンを押している間(ドラッグ中)はポップアップを出さず、押している間に
        // 見えた選択内容だけを覚えておいて、ボタンを離した時点でまとめて1回だけ出す
        // (マウスドラッグ検知側のWM_LBUTTONUPと同じタイミングに揃える)。
        // キーボードでの選択(Shift+矢印等、ボタンを押していない)は即座に出す
        private bool _isLeftMouseDown;
        private string? _lastShownSelectionText;
        private string? _pendingSelectionTextDuringDrag;

        /// <summary>
        /// TextSelectionChangedEventのコールバック(UIAのイベントスレッドから呼ばれる)。
        /// senderのTextPatternから選択文字列を取り出し、UIスレッドへ戻してSelectionFoundを発火する。
        /// </summary>
        private void OnTextSelectionChanged(IUIAutomationElement sender)
        {
            try
            {
                // 自分自身(SelectionToolbar)からのイベントは無視する
                var hwnd = sender.get_CurrentNativeWindowHandle();
                if (hwnd != IntPtr.Zero && IsOwnWindow(hwnd))
                    return;

                if (sender.GetCurrentPattern(UIA_TextPatternId) is not IUIAutomationTextPattern pattern)
                    return;

                var text = ExtractSelectedText(pattern);
                if (string.IsNullOrWhiteSpace(text) || text.Trim().Length <= 1)
                {
                    _pendingSelectionTextDuringDrag = null;
                    return;
                }

                if (_isLeftMouseDown)
                {
                    // ドラッグ中は出さず、最新の選択内容だけ覚えておく
                    // (ボタンを離した時にWM_LBUTTONUP側から拾いに行く)
                    _pendingSelectionTextDuringDrag = text;
                    return;
                }

                if (text == _lastShownSelectionText)
                    return;

                ShowSelectionFound(text);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SelectionWatcher] OnTextSelectionChanged error: {ex}");
            }
        }

        private void ShowSelectionFound(string text)
        {
            _lastShownSelectionText = text;

            // イベントは選択位置の座標を教えてくれないため、現在のマウスカーソル位置を
            // ポップアップのアンカーとして使う(キーボード選択の場合は多少ずれるが、
            // マウスドラッグ検知側の座標付きの結果と役割は同じなので許容する)
            GetCursorPos(out var cursor);

            _dispatcherQueue?.TryEnqueue(() =>
                SelectionFound?.Invoke(this, new SelectionFoundEventArgs { Text = text, ScreenX = cursor.X, ScreenY = cursor.Y }));
        }

        private IntPtr OnMouseEvent(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                if ((int)wParam == MouseHook.WM_LBUTTONDOWN)
                {
                    _downX = Marshal.ReadInt32(lParam, 0);
                    _downY = Marshal.ReadInt32(lParam, 4);
                    _isLeftMouseDown = true;
                    _pendingSelectionTextDuringDrag = null;
                }
                else if ((int)wParam == MouseHook.WM_LBUTTONUP)
                {
                    // MSLLHOOKSTRUCTの先頭フィールドがPOINT pt(x, y)
                    var x = Marshal.ReadInt32(lParam, 0);
                    var y = Marshal.ReadInt32(lParam, 4);

                    _isLeftMouseDown = false;

                    // ドラッグ中にTextSelectionChangedEvent側で拾っておいた選択内容があれば、
                    // ボタンを離した今のタイミングでまとめて1回だけ出す
                    if (_pendingSelectionTextDuringDrag is { } pendingText)
                    {
                        _pendingSelectionTextDuringDrag = null;
                        if (pendingText != _lastShownSelectionText)
                            ShowSelectionFound(pendingText);
                    }

                    // ドラッグしたのか、ただの1クリックなのかを、押下位置からの移動量で見分ける。
                    // ドラッグ(=選択操作)の時だけ選択テキストの有無を確認する
                    var isDrag = Math.Abs(x - _downX) > ClickDragThreshold
                        || Math.Abs(y - _downY) > ClickDragThreshold;

                    if (isDrag)
                    {
                        // ラムダが後から実行されるため、フィールド(_downX/_downY)を
                        // 直接参照せず、この時点の値をローカルへ写してから渡す
                        var downX = _downX;
                        var downY = _downY;

                        // フック内で重い処理(UI Automation呼び出し)をすると入力全体が
                        // 詰まりかねないため、Task.Runでフック呼び出しスレッドから完全に
                        // 切り離す(awaitなしで直接呼ぶだけでは同じスレッド上で同期的に
                        // 実行されてしまい、フックが詰まってマウス入力全体が重くなっていた)
                        _ = System.Threading.Tasks.Task.Run(() => CheckSelectionAsync(downX, downY, x, y));
                    }
                }
            }

            // 横取りはせず、必ずクリック先へ渡す
            return MouseHook.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        /// <param name="downX">ドラッグを開始した位置(WM_LBUTTONDOWN)。要素の探索に使う。</param>
        /// <param name="downY">同上。</param>
        /// <param name="upX">ドラッグを終えた位置(WM_LBUTTONUP)。ツールバーを出す位置に使う。</param>
        /// <param name="upY">同上。</param>
        private System.Threading.Tasks.Task CheckSelectionAsync(int downX, int downY, int upX, int upY)
        {
            try
            {
                // UI Automationで取れなければ、IAccessible2までしか対応していない
                // アプリ向けにフォールバックする(CaretInfoProviderと同じ考え方)
                var text = TryGetSelectedTextViaUiAutomation(downX, downY, upX, upY)
                    ?? TryGetSelectedTextViaAccessible2(downX, downY)
                    ?? TryGetSelectedTextViaAccessible2(upX, upY)
                    ?? TryGetSelectedTextViaWin32EditControl(downX, downY)
                    ?? TryGetSelectedTextViaWin32EditControl(upX, upY)
                    ?? TryGetSelectedTextViaScintilla(downX, downY)
                    ?? TryGetSelectedTextViaScintilla(upX, upY);

                // 1文字だけの選択ではポップアップを出さない。PopClipと同じく、
                // 複数文字を選んだ時だけ意味のある選択とみなす。TextSelectionChangedEvent側で
                // 既に同じ内容を出し終えている場合は、二重に出さないようスキップする
                if (!string.IsNullOrWhiteSpace(text) && text.Trim().Length > 1 && text != _lastShownSelectionText)
                {
                    _lastShownSelectionText = text;

                    // ツールバー自体は、カーソルがある位置(ドラッグ終了位置)の近くに出す
                    _dispatcherQueue?.TryEnqueue(() =>
                        SelectionFound?.Invoke(this, new SelectionFoundEventArgs { Text = text, ScreenX = upX, ScreenY = upY }));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SelectionWatcher] {ex}");
            }

            return System.Threading.Tasks.Task.CompletedTask;
        }

        /// <summary>クリックされたウィンドウが、自分自身(このプロセス)のものかどうか。</summary>
        private static bool IsOwnWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
                return false;

            GetWindowThreadProcessId(hWnd, out var processId);
            return processId == _currentProcessId;
        }

        private static readonly uint _currentProcessId = (uint)Environment.ProcessId;

        private string? TryGetSelectedTextViaUiAutomation(int downX, int downY, int upX, int upY)
        {
            var automation = GetAutomation();
            if (automation is null)
                return null;

            // まず座標上の要素から祖先を辿って探す。Claudeデスクトップアプリの
            // 応答表示欄のような読み取り専用のテキスト領域はキーボードフォーカスを
            // 受け取らないことが多く、GetFocusedElement()では選択中の要素を取得できない。
            //
            // 見るのはドラッグの「開始位置」を優先する。終了位置は、最終行より先まで
            // 引っ張る・右端を越える・スクロールバーや別コントロールの上で離すなど、
            // テキスト領域の外へはみ出していることが珍しくなく、その場合
            // ElementFromPointは選択とは無関係な要素を返してしまうため。
            // ただし本文脇の余白からドラッグを始める操作もあるので、開始位置で
            // 取れなければ終了位置でも試す
            var text = TryGetSelectedTextFromPoint(automation, downX, downY)
                ?? TryGetSelectedTextFromPoint(automation, upX, upY);

            if (text is not null)
                return text;

            // 見つからなければ、通常の入力欄向けにフォーカス中の要素でも試す
            var focused = automation.GetFocusedElement();
            if (focused?.GetCurrentPattern(UIA_TextPatternId) is IUIAutomationTextPattern focusedPattern)
                return ExtractSelectedText(focusedPattern);

            return null;
        }

        /// <summary>
        /// 指定した画面座標にある要素から、TextPatternを実装している祖先を
        /// MaxAncestorWalk階層まで辿り、選択中のテキストを取り出す。
        /// </summary>
        private static string? TryGetSelectedTextFromPoint(IUIAutomation automation, int x, int y)
        {
            var point = new UiaPoint { x = x, y = y };
            var element = automation.ElementFromPoint(point);
            var walker = automation.get_RawViewWalker();

            for (var depth = 0; element is not null && depth < MaxAncestorWalk; depth++)
            {
                if (element.GetCurrentPattern(UIA_TextPatternId) is IUIAutomationTextPattern pattern)
                {
                    var text = ExtractSelectedText(pattern);
                    if (text is not null)
                        return text;
                }

                element = walker.GetParentElement(element);
            }

            return null;
        }

        private static string? ExtractSelectedText(IUIAutomationTextPattern textPattern)
        {
            var selection = textPattern.GetSelection();
            if (selection is null || selection.Length == 0)
                return null;

            var range = selection.GetElement(0);
            var text = range.GetText(-1);
            return string.IsNullOrEmpty(text) ? null : text;
        }

        // IAccessible2(OBJID_CLIENT)を取得するためのWin32定数
        private const uint OBJID_CLIENT = unchecked((uint)-4);

        private static readonly Guid IID_IAccessible = new("618736E0-3C3D-11CF-810C-00AA00389B71");
        private static readonly Guid IID_IAccessible2 = new("E89F726E-C4F4-4c19-BB19-B647D7FA8478");

        [DllImport("oleacc.dll")]
        private static extern int AccessibleObjectFromWindow(
            IntPtr hwnd, uint dwId, ref Guid riid,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppvObject);

        /// <summary>
        /// UI AutomationのTextPatternに対応していないが、IAccessible2の
        /// IAccessibleTextは実装しているアプリ向けのフォールバック
        /// (CaretInfoProvider.TryGetCaretInfoViaAccessible2と同じ手順)。
        /// クリック位置からウィンドウを特定し、そのクライアント領域のフォーカス要素を辿る。
        /// </summary>
        private string? TryGetSelectedTextViaAccessible2(int x, int y)
        {
            var hWnd = WindowFromPoint(new POINT { X = x, Y = y });
            if (hWnd == IntPtr.Zero)
                return null;

            object? clientObj = null;
            try
            {
                var riid = IID_IAccessible;
                if (AccessibleObjectFromWindow(hWnd, OBJID_CLIENT, ref riid, out clientObj) != 0)
                    return null;

                if (clientObj is not Accessibility.IAccessible client)
                    return null;

                object? focusResult = null;
                try { focusResult = client.accFocus; } catch { /* 未対応なら無視 */ }

                var target = focusResult as Accessibility.IAccessible ?? client;

                if (target is not IServiceProvider serviceProvider)
                    return null;

                var iid2 = IID_IAccessible2;
                if (serviceProvider.QueryService(ref iid2, ref iid2, out var accessible2Ptr) != 0
                    || accessible2Ptr == IntPtr.Zero)
                {
                    return null;
                }

                object accessible2Obj;
                try
                {
                    accessible2Obj = Marshal.GetObjectForIUnknown(accessible2Ptr);
                }
                finally
                {
                    Marshal.Release(accessible2Ptr);
                }

                if (accessible2Obj is not IAccessibleText2 text)
                    return null;

                if (text.get_nSelections(out var selectionCount) != 0 || selectionCount <= 0)
                    return null;

                if (text.get_selection(0, out var startOffset, out var endOffset) != 0)
                    return null;

                if (startOffset == endOffset)
                    return null;

                if (startOffset > endOffset)
                    (startOffset, endOffset) = (endOffset, startOffset);

                if (text.get_text(startOffset, endOffset, out var selectedText) != 0)
                    return null;

                return string.IsNullOrEmpty(selectedText) ? null : selectedText;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (clientObj is not null && Marshal.IsComObject(clientObj))
                    Marshal.ReleaseComObject(clientObj);
            }
        }

        // クラシックなWin32エディットコントロール(Edit/RichEdit)向けの最終フォールバック。
        // UI AutomationのTextPatternにもIAccessible2のIAccessibleTextにも対応していない
        // 古いアプリ(一部の独自ダイアログ等)でも、コントロール自体はEdit/RichEditの
        // 標準メッセージ(EM_GETSEL/WM_GETTEXT)に応答することが多いため、直接SendMessageで問い合わせる
        private const int EM_GETSEL = 0x00B0;
        private const int WM_GETTEXT = 0x000D;
        private const int WM_GETTEXTLENGTH = 0x000E;

        private static string? TryGetSelectedTextViaWin32EditControl(int x, int y)
        {
            var hWnd = WindowFromPoint(new POINT { X = x, Y = y });
            if (hWnd == IntPtr.Zero)
                return null;

            var classNameBuffer = new System.Text.StringBuilder(256);
            if (GetClassName(hWnd, classNameBuffer, classNameBuffer.Capacity) == 0)
                return null;

            var className = classNameBuffer.ToString();
            var isEditControl = className.Equals("Edit", StringComparison.OrdinalIgnoreCase)
                || className.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase);

            if (!isEditControl)
                return null;

            // EM_GETSELは戻り値のLOWORD/HIWORDへ開始/終了位置を詰めて返す(16bit幅のため
            // 65535文字を超える長いテキストでは信頼できないが、選択範囲の検出用途としては十分)
            var selection = SendMessage(hWnd, EM_GETSEL, IntPtr.Zero, IntPtr.Zero).ToInt64();
            var start = (int)(selection & 0xFFFF);
            var end = (int)((selection >> 16) & 0xFFFF);

            if (start >= end)
                return null;

            var length = (int)SendMessage(hWnd, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero);
            if (length <= 0 || end > length)
                return null;

            var textBuffer = new System.Text.StringBuilder(length + 1);
            SendMessage(hWnd, WM_GETTEXT, new IntPtr(textBuffer.Capacity), textBuffer);

            var fullText = textBuffer.ToString();
            if (start >= fullText.Length)
                return null;

            end = Math.Min(end, fullText.Length);
            var selectedText = fullText.Substring(start, end - start);
            return string.IsNullOrEmpty(selectedText) ? null : selectedText;
        }

        // Scintilla(Notepad++等が使う独自描画のテキスト編集コンポーネント)向けのフォールバック。
        // UI AutomationのTextPatternにもIAccessible2にも対応していないが、WM_USERベースの
        // 独自メッセージ(SCI_*)で選択範囲・テキストを取得できる。ただしEM_GETSEL/WM_GETTEXTと
        // 異なりOSが自動でプロセス間マーシャリングしてくれないため、対象プロセス内に
        // VirtualAllocExでバッファを確保し、SendMessageで書き込ませてからReadProcessMemoryで
        // 読み出す(他のScintilla系ツールでも使われる定番の手順)
        private const int SCI_GETSELECTIONSTART = 2143;
        private const int SCI_GETSELECTIONEND = 2145;
        private const int SCI_GETSELTEXT = 2161;
        private const int SCI_GETCODEPAGE = 2137;
        private const int SC_CP_UTF8 = 65001;

        private static string? TryGetSelectedTextViaScintilla(int x, int y)
        {
            var hWnd = WindowFromPoint(new POINT { X = x, Y = y });
            if (hWnd == IntPtr.Zero)
                return null;

            var classNameBuffer = new System.Text.StringBuilder(256);
            if (GetClassName(hWnd, classNameBuffer, classNameBuffer.Capacity) == 0)
                return null;

            if (!classNameBuffer.ToString().Equals("Scintilla", StringComparison.OrdinalIgnoreCase))
                return null;

            var start = (int)SendMessage(hWnd, SCI_GETSELECTIONSTART, IntPtr.Zero, IntPtr.Zero);
            var end = (int)SendMessage(hWnd, SCI_GETSELECTIONEND, IntPtr.Zero, IntPtr.Zero);
            if (start >= end)
                return null;

            var length = end - start;
            var isUtf8 = (int)SendMessage(hWnd, SCI_GETCODEPAGE, IntPtr.Zero, IntPtr.Zero) == SC_CP_UTF8;

            GetWindowThreadProcessId(hWnd, out var processId);
            var hProcess = Kernel32.OpenProcess(
                Kernel32.PROCESS_VM_OPERATION | Kernel32.PROCESS_VM_READ | Kernel32.PROCESS_VM_WRITE | Kernel32.PROCESS_QUERY_INFORMATION,
                false, processId);
            if (hProcess == IntPtr.Zero)
                return null;

            var remoteBuffer = IntPtr.Zero;
            try
            {
                // SCI_GETSELTEXTはヌル終端込みでlength+1バイト書き込むため、その分を確保する
                remoteBuffer = Kernel32.VirtualAllocEx(
                    hProcess, IntPtr.Zero, (UIntPtr)(length + 1), Kernel32.MEM_COMMIT, Kernel32.PAGE_READWRITE);
                if (remoteBuffer == IntPtr.Zero)
                    return null;

                SendMessage(hWnd, SCI_GETSELTEXT, IntPtr.Zero, remoteBuffer);

                var localBuffer = new byte[length + 1];
                if (!Kernel32.ReadProcessMemory(hProcess, remoteBuffer, localBuffer, localBuffer.Length, out _))
                    return null;

                // ヌル終端(バイト0)までを実際の文字列長とする
                var actualLength = Array.IndexOf(localBuffer, (byte)0);
                if (actualLength < 0)
                    actualLength = localBuffer.Length;

                var encoding = isUtf8 ? System.Text.Encoding.UTF8 : System.Text.Encoding.Default;
                var text = encoding.GetString(localBuffer, 0, actualLength);
                return string.IsNullOrEmpty(text) ? null : text;
            }
            finally
            {
                if (remoteBuffer != IntPtr.Zero)
                    Kernel32.VirtualFreeEx(hProcess, remoteBuffer, UIntPtr.Zero, Kernel32.MEM_RELEASE);

                Kernel32.CloseHandle(hProcess);
            }
        }

        private IUIAutomation? GetAutomation()
        {
            if (_automation is not null)
                return _automation;

            try
            {
                _automation = UIAutomationInterop.CreateAutomation();
                return _automation;
            }
            catch
            {
                return null;
            }
        }

        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }

        ~SelectionWatcher()
        {
            Stop();
        }

        /// <summary>
        /// IUIAutomationEventHandlerの実装。UIAはCOMのコールバックとしてこれを呼ぶため、
        /// 実処理はデリゲート経由でSelectionWatcher側(OnTextSelectionChanged)へ委譲するだけにする。
        /// </summary>
        private sealed class TextSelectionEventHandler : IUIAutomationEventHandler
        {
            private readonly Action<IUIAutomationElement> _onEvent;

            public TextSelectionEventHandler(Action<IUIAutomationElement> onEvent)
            {
                _onEvent = onEvent;
            }

            public void HandleAutomationEvent(IUIAutomationElement sender, int eventId)
            {
                if (eventId == UiaEventIds.UIA_Text_TextSelectionChangedEventId && sender is not null)
                    _onEvent(sender);
            }
        }
    }

    // --- IAccessible2まわりの最小限のCOM宣言(CaretInfoProviderと同じ考え方) ---
    // IServiceProvider: IAccessible(MSAA)からIAccessible2へ橋渡しするための標準手順で使う
    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid guidService, ref Guid riid, out IntPtr ppvObject);
    }

    /// <summary>
    /// IAccessibleText(IAccessible2仕様)。COMのvtableは宣言順に対応するため、
    /// 実際に呼び出すget_nSelections/get_selection/get_textより前のメンバーも、
    /// たとえ呼ばなくても正しい順序で宣言する必要がある。
    /// 順序はIAccessible2公式IDL(ia2_api_all.idl)のIAccessibleText定義に基づく。
    /// </summary>
    [ComImport, Guid("24FD2FFB-3AAD-4a08-8335-A3AD89C0FB4B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAccessibleText2
    {
        [PreserveSig]
        int addSelection(int startOffset, int endOffset);

        [PreserveSig]
        int get_attributes(int offset, out int startOffset, out int endOffset,
            [MarshalAs(UnmanagedType.BStr)] out string textAttributes);

        [PreserveSig]
        int get_caretOffset(out int offset);

        [PreserveSig]
        int get_characterExtents(int offset, int coordType,
            out int x, out int y, out int width, out int height);

        [PreserveSig]
        int get_nSelections(out int nSelections);

        [PreserveSig]
        int get_offsetAtPoint(int x, int y, int coordType, out int offset);

        [PreserveSig]
        int get_selection(int selectionIndex, out int startOffset, out int endOffset);

        [PreserveSig]
        int get_text(int startOffset, int endOffset,
            [MarshalAs(UnmanagedType.BStr)] out string text);
    }
}
