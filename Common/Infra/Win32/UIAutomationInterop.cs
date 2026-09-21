using System;
using System.Runtime.InteropServices;

namespace Common.Infra.Win32
{
    /// <summary>
    /// UI Automation COM APIの自前バインディング(Interop.UIAutomationClientパッケージの代替)。
    ///
    /// 実際に呼び出すメンバーだけを最小限に絞っているが、COMのvtableは宣言順がそのまま
    /// スロット番号に対応するため、呼ばないメンバーも元のIDL通りの順序で"埋め"として
    /// 宣言しておく必要がある(IAccessibleTextまわりの既存コードと同じ考え方)。
    /// 埋めのメンバーは呼び出さないので、シグネチャは正しくなくても安全(objectで代用)。
    ///
    /// GUID・メンバー順序は、Interop.UIAutomationClient 10.19041.0のアセンブリを
    /// リフレクションで実際に読み取って確認した値(2026-09-19時点)。
    /// 元のIDL: UIAutomationClient.idl(Windows SDK)。
    /// </summary>
    public static class UIAutomationInterop
    {
        // CLSID_CUIAutomation
        private const string ClsidCUIAutomation = "FF48DBA4-60EF-4201-AA87-54103EEF594E";

        // 継承関係を宣言しない(sealedにしない)ことで、実行時のQueryInterfaceに委ねる
        // 明示的な参照変換としてコンパイルを通す。TLBIMPが生成するCoClassと同じ考え方
        [ComImport, Guid(ClsidCUIAutomation)]
        private class CUIAutomationCoClass
        {
        }

        /// <summary>CUIAutomationのインスタンスを作成する(new Interop.UIAutomationClient.CUIAutomation()の代替)。</summary>
        public static IUIAutomation CreateAutomation() => (IUIAutomation)(object)new CUIAutomationCoClass();
    }

    /// <summary>tagPOINT相当(x, y の2つのInt32。ElementFromPointの引数用)。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UiaPoint
    {
        public int x;
        public int y;
    }

    /// <summary>UI AutomationのTreeScope(AddAutomationEventHandlerの対象範囲指定用)。</summary>
    public enum TreeScope
    {
        Subtree = 0x1 | 0x2 | 0x4, // Element | Children | Descendants
    }

    // IUIAutomation: ElementFromPoint/GetFocusedElement/RawViewWalker/GetRootElement/
    // AddAutomationEventHandler/RemoveAutomationEventHandlerを使う。実際のIID
    // (30CBE57D-D9D0-452A-AB13-7AC5AC4825EE)通りの順序で、そこまでのメンバーを埋めとして宣言する
    [ComImport, Guid("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IUIAutomation
    {
        object CompareElements_Unused();
        object CompareRuntimeIds_Unused();
        IUIAutomationElement GetRootElement();
        object ElementFromHandle_Unused();
        IUIAutomationElement ElementFromPoint(UiaPoint pt);
        IUIAutomationElement GetFocusedElement();
        object GetRootElementBuildCache_Unused();
        object ElementFromHandleBuildCache_Unused();
        object ElementFromPointBuildCache_Unused();
        object GetFocusedElementBuildCache_Unused();
        object CreateTreeWalker_Unused();
        object get_ControlViewWalker_Unused();
        object get_ContentViewWalker_Unused();
        IUIAutomationTreeWalker get_RawViewWalker();
        object get_RawViewCondition_Unused();
        object get_ControlViewCondition_Unused();
        object get_ContentViewCondition_Unused();
        object CreateCacheRequest_Unused();
        object CreateTrueCondition_Unused();
        object CreateFalseCondition_Unused();
        object CreatePropertyCondition_Unused();
        object CreatePropertyConditionEx_Unused();
        object CreateAndCondition_Unused();
        object CreateAndConditionFromArray_Unused();
        object CreateAndConditionFromNativeArray_Unused();
        object CreateOrCondition_Unused();
        object CreateOrConditionFromArray_Unused();
        object CreateOrConditionFromNativeArray_Unused();
        object CreateNotCondition_Unused();

        void AddAutomationEventHandler(
            int eventId,
            IUIAutomationElement element,
            TreeScope scope,
            IntPtr cacheRequest,
            IUIAutomationEventHandler handler);

        void RemoveAutomationEventHandler(
            int eventId,
            IUIAutomationElement element,
            IUIAutomationEventHandler handler);
    }

    /// <summary>
    /// UI Automationのイベント通知コールバック。実装はこちら(SelectionWatcher)側が行い、
    /// AddAutomationEventHandlerへ渡す。COMからのコールバックを受けるため、
    /// 呼び出し元スレッド(UIAのイベントスレッド)で呼ばれることに注意する。
    /// </summary>
    [ComImport, Guid("146C3C17-F12E-4E22-8C27-F894B9B79C69"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IUIAutomationEventHandler
    {
        void HandleAutomationEvent(IUIAutomationElement sender, int eventId);
    }

    /// <summary>よく使うUI Automationのイベント定数。</summary>
    public static class UiaEventIds
    {
        public const int UIA_Text_TextSelectionChangedEventId = 20014;
    }

    // IUIAutomationElement: GetCurrentPattern(スロット13)とCurrentNativeWindowHandle(スロット33)だけを使う
    [ComImport, Guid("D22108AA-8AC5-49A5-837B-37BBB3D7591E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IUIAutomationElement
    {
        object SetFocus_Unused();
        object GetRuntimeId_Unused();
        object FindFirst_Unused();
        object FindAll_Unused();
        object FindFirstBuildCache_Unused();
        object FindAllBuildCache_Unused();
        object BuildUpdatedCache_Unused();
        object GetCurrentPropertyValue_Unused();
        object GetCurrentPropertyValueEx_Unused();
        object GetCachedPropertyValue_Unused();
        object GetCachedPropertyValueEx_Unused();
        object GetCurrentPatternAs_Unused();
        object GetCachedPatternAs_Unused();
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object GetCurrentPattern(int patternId);
        object GetCachedPattern_Unused();
        object GetCachedParent_Unused();
        object GetCachedChildren_Unused();
        object get_CurrentProcessId_Unused();
        object get_CurrentControlType_Unused();
        object get_CurrentLocalizedControlType_Unused();
        object get_CurrentName_Unused();
        object get_CurrentAcceleratorKey_Unused();
        object get_CurrentAccessKey_Unused();
        object get_CurrentHasKeyboardFocus_Unused();
        object get_CurrentIsKeyboardFocusable_Unused();
        object get_CurrentIsEnabled_Unused();
        object get_CurrentAutomationId_Unused();
        object get_CurrentClassName_Unused();
        object get_CurrentHelpText_Unused();
        object get_CurrentCulture_Unused();
        object get_CurrentIsControlElement_Unused();
        object get_CurrentIsContentElement_Unused();
        object get_CurrentIsPassword_Unused();
        IntPtr get_CurrentNativeWindowHandle();
    }

    // IUIAutomationTreeWalker: GetParentElementが先頭(スロット0)なので埋めは不要
    [ComImport, Guid("4042C624-389C-4AFC-A630-9DF854A541FC"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IUIAutomationTreeWalker
    {
        IUIAutomationElement GetParentElement(IUIAutomationElement element);
    }

    // IUIAutomationValuePattern: get_CurrentIsReadOnly(スロット2)だけを使う。
    // クリック先が書き込み可能なテキスト欄かどうかの判定(貼り付けポップアップ用)に使う
    [ComImport, Guid("A94CD8B1-0844-4CD6-9D2D-640537AB39E9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IUIAutomationValuePattern
    {
        object SetValue_Unused();
        object get_CurrentValue_Unused();
        [return: MarshalAs(UnmanagedType.Bool)]
        bool get_CurrentIsReadOnly();
    }

    // IUIAutomationTextPattern: GetSelection(スロット2)だけを使う
    [ComImport, Guid("32EBA289-3583-42C9-9C59-3B6D9A1E9B6A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IUIAutomationTextPattern
    {
        object RangeFromPoint_Unused();
        object RangeFromChild_Unused();
        IUIAutomationTextRangeArray GetSelection();
    }

    // IUIAutomationTextRange: GetBoundingRectangles(スロット7)とGetText(スロット9)を使う
    [ComImport, Guid("A543CC6A-F4AE-494B-8239-C814481187A8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IUIAutomationTextRange
    {
        object Clone_Unused();
        object Compare_Unused();
        object CompareEndpoints_Unused();
        object ExpandToEnclosingUnit_Unused();
        object FindAttribute_Unused();
        object FindText_Unused();
        object GetAttributeValue_Unused();
        [return: MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_R8)]
        double[] GetBoundingRectangles();
        object GetEnclosingElement_Unused();
        [return: MarshalAs(UnmanagedType.BStr)]
        string GetText(int maxLength);
    }

    // IUIAutomationTextRangeArray: Length/GetElementとも先頭2つなので埋めは不要
    [ComImport, Guid("CE4AE76A-E717-4C98-81EA-47371D028EB6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IUIAutomationTextRangeArray
    {
        int Length { get; }
        IUIAutomationTextRange GetElement(int index);
    }
}
