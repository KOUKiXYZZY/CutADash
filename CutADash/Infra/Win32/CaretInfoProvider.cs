using System;
using System.Runtime.InteropServices;
using Accessibility;
using Common.Infra.Win32;
using WinAPI;
using Windows.Foundation;
using static WinAPI.Structs;
using static WinAPI.WinUser;

namespace CutADash.Infra.Win32
{
    public sealed class CaretInfo
    {
        public required Rect Rect { get; init; }
        public required IntPtr Hwnd { get; init; }
        public required bool IsVisible { get; init; }
    }

    internal static class CaretInfoProvider
    {
        // UI AutomationのTextPatternのパターンID(UIA_TextPatternId)。
        // Interop.UIAutomationClientの型を直接使わず、ドキュメント化された定数値を使う
        private const int UIA_TextPatternId = 10014;

        // CUIAutomationの生成コストは軽くないため、1度だけ作って使い回す。
        // staticフィールド初期化子(readonly = new())にすると、初回アクセス時に
        // 一度でも失敗すると型初期化子自体が「壊れた」状態になり、プロセスが
        // 生きている間ずっとTypeInitializationExceptionを返し続けてしまう。
        // そのため遅延生成にし、失敗しても次回また作り直せるようにする
        private static IUIAutomation? _automation;

        private static IUIAutomation? GetAutomation()
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

        /// <summary>
        /// 現在フォーカスされているテキスト入力位置(キャレット)を取得する。
        ///
        /// メモ帳・メールなど、Windows 11で刷新された標準アプリの多くはWinUI/UWP製の
        /// カスタム描画テキストコントロールを使っており、古典的なWin32キャレットAPI
        /// (CreateCaret/ShowCaret)を呼ばない。そのため、次の順で取得を試みる。
        /// 1. UI AutomationのTextPattern(対応の広い正攻法)
        /// 2. IAccessible2のIAccessibleText(TextPattern未対応だがIA2は対応するアプリ向け)
        /// 3. GetGUIThreadInfo(素のWin32 Editコントロールを使う古いアプリ向け)
        /// </summary>
        public static CaretInfo? GetCaretInfo(IntPtr hWnd)
        {
            return TryGetCaretInfoViaUiAutomation()
                ?? TryGetCaretInfoViaAccessible2(hWnd)
                ?? GetCaretInfoViaGuiThreadInfo(hWnd);
        }

        private static CaretInfo? TryGetCaretInfoViaUiAutomation()
        {
            try
            {
                var automation = GetAutomation();
                if (automation is null)
                    return null;

                var focused = automation.GetFocusedElement();
                if (focused is null)
                    return null;

                if (focused.GetCurrentPattern(UIA_TextPatternId) is not IUIAutomationTextPattern textPattern)
                    return null;

                var selection = textPattern.GetSelection();
                if (selection is null || selection.Length == 0)
                    return null;

                var range = selection.GetElement(0);
                if (range.GetBoundingRectangles() is not double[] rects || rects.Length < 4)
                    return null;

                // GetBoundingRectanglesは[x, y, width, height]の繰り返し(画面座標、物理ピクセル)。
                // 折り返しなどで複数矩形になることがあるが、キャレット位置には先頭の矩形で十分
                var x = rects[0];
                var y = rects[1];
                var width = rects[2];
                var height = rects[3];

                return new CaretInfo
                {
                    Rect = new Rect(x, y, width, height),
                    Hwnd = focused.get_CurrentNativeWindowHandle(),
                    IsVisible = true
                };
            }
            catch
            {
                // フォーカス要素がTextPatternを実装していない、選択範囲が取得できない等は
                // すべて「UI Automationでは取れなかった」として扱い、フォールバックに任せる
                return null;
            }
        }

        // IAccessible(OBJID_CLIENT)を取得するためのWin32定数・P/Invoke
        private const uint OBJID_CLIENT = unchecked((uint)-4);

        [DllImport("oleacc.dll")]
        private static extern int AccessibleObjectFromWindow(
            IntPtr hwnd, uint dwId, ref Guid riid,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppvObject);

        private static readonly Guid IID_IAccessible = new("618736E0-3C3D-11CF-810C-00AA00389B71");
        private static readonly Guid IID_IAccessible2 = new("E89F726E-C4F4-4c19-BB19-B647D7FA8478");

        // IA2CoordinateType.IA2_COORDTYPE_SCREEN_RELATIVE
        private const int IA2_COORDTYPE_SCREEN_RELATIVE = 0;

        /// <summary>
        /// UI Automationのフォーカス要素にはTextPatternが無いが、IAccessible2の
        /// IAccessibleTextは実装しているアプリ向けのフォールバック。
        /// MSAA(IAccessible)→IServiceProviderの橋渡し→IAccessible2→IAccessibleTextと辿る、
        /// IAccessible2仕様で公式に案内されている手順を踏む。
        /// </summary>
        private static CaretInfo? TryGetCaretInfoViaAccessible2(IntPtr hWnd)
        {
            object? clientObj = null;
            try
            {
                var riid = IID_IAccessible;
                if (AccessibleObjectFromWindow(hWnd, OBJID_CLIENT, ref riid, out clientObj) != 0)
                    return null;

                if (clientObj is not Accessibility.IAccessible client)
                    return null;

                // フォーカスされている子を辿る。単なるCHILDID(int)が返る場合や
                // 何も無い場合は、クライアント自身(=対象コントロール)を使う
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

                if (accessible2Obj is not IAccessibleText text)
                    return null;

                if (text.get_caretOffset(out var offset) != 0)
                    return null;

                if (text.get_characterExtents(offset, IA2_COORDTYPE_SCREEN_RELATIVE,
                        out var x, out var y, out var width, out var height) != 0)
                {
                    return null;
                }

                return new CaretInfo
                {
                    Rect = new Rect(x, y, width, height),
                    Hwnd = hWnd,
                    IsVisible = true
                };
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

        // 素のWin32 Editコントロールを使う古典的なアプリ向けのフォールバック。
        // rcCaretはクライアント座標なので、hwndCaretのクライアント座標系でClientToScreenする
        private static CaretInfo? GetCaretInfoViaGuiThreadInfo(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return null;
            uint threadId = GetWindowThreadProcessId(hWnd, out _);

            var gui = new GUITHREADINFO();
            gui.cbSize = Marshal.SizeOf<GUITHREADINFO>();
            if (!GetGUIThreadInfo(threadId, ref gui)) return null;

            if (gui.hwndCaret == IntPtr.Zero)
                return new CaretInfo { Rect = new Rect(0, 0, 0, 0), Hwnd = IntPtr.Zero, IsVisible = false };

            var leftTop = new POINT { X = gui.rcCaret.Left, Y = gui.rcCaret.Top };
            var rightBottom = new POINT { X = gui.rcCaret.Right, Y = gui.rcCaret.Bottom };

            if (!WinUser.ClientToScreen(gui.hwndCaret, ref leftTop)) return null;
            if (!WinUser.ClientToScreen(gui.hwndCaret, ref rightBottom)) return null;

            return new CaretInfo
            {
                Rect = new Rect(
                    leftTop.X,
                    leftTop.Y,
                    Math.Max(0, rightBottom.X - leftTop.X),
                    Math.Max(0, rightBottom.Y - leftTop.Y)),

                Hwnd = gui.hwndCaret,

                IsVisible = true
            };
        }
    }

    // --- IAccessible2まわりの最小限のCOM宣言 ---
    // IServiceProvider: 古典的なOLEインターフェース(GUID/シグネチャとも長年不変で安定)。
    // IAccessible(MSAA)からIAccessible2へ橋渡しするための標準手順で使う
    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid guidService, ref Guid riid, out IntPtr ppvObject);
    }

    /// <summary>
    /// IAccessibleText(IAccessible2仕様)。COMのvtableは宣言順に対応するため、
    /// 実際に呼び出すget_caretOffset/get_characterExtentsより前のメンバー
    /// (addSelection/get_attributes)も、たとえ呼ばなくても正しい順序で宣言する必要がある。
    /// 順序はIAccessible2公式IDL(ia2_api_all.idl)のIAccessibleText定義に基づく。
    /// </summary>
    [ComImport, Guid("24FD2FFB-3AAD-4a08-8335-A3AD89C0FB4B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAccessibleText
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
    }
}
