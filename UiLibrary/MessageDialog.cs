using Microsoft.UI.Xaml;
using System;
using System.Threading.Tasks;

namespace UiLibrary
{
    /// <summary>MessageBoxのMB_OK等に対応する、ボタンの組み合わせ。</summary>
    public enum MessageDialogButtons
    {
        Ok,
        OkCancel,
        AbortRetryIgnore,
        YesNoCancel,
        YesNo,
        RetryCancel,
        CancelTryContinue
    }

    /// <summary>MessageBoxのMB_ICON*に対応する、アイコンの種類。</summary>
    public enum MessageDialogIcon
    {
        None,
        Information,
        Warning,
        Error,
        Question
    }

    /// <summary>MessageBoxのMB_DEFBUTTON1〜3に対応する、最初に選ばれているボタン。</summary>
    public enum MessageDialogDefaultButton
    {
        First,
        Second,
        Third
    }

    /// <summary>押されたボタン。値はMessageBoxの戻り値(IDOK等)と同じ。</summary>
    public enum MessageDialogResult
    {
        Ok = 1,
        Cancel = 2,
        Abort = 3,
        Retry = 4,
        Ignore = 5,
        Yes = 6,
        No = 7,
        TryAgain = 10,
        Continue = 11
    }

    /// <summary>
    /// MessageBoxW相当の機能を持つ、WinUI 3製のメッセージダイアログ。
    ///
    /// ContentDialogと違い、呼び出し元にWindow(XamlRoot)が無くても出せる(専用の小さな
    /// ウィンドウを作る)ので、タスクトレイ常駐のアプリからでも使える。
    /// マウスカーソルのあるモニタの中央に、最前面で表示する。
    /// UIスレッド(XAMLを扱うスレッド)から呼ぶこと。
    /// </summary>
    public static class MessageDialog
    {
        /// <summary>
        /// ダイアログの明/暗のテーマ。Defaultの時はOS(アプリ)の設定に従う。
        /// アプリ側のテーマ設定が変わった時に、呼び出し元が更新する。
        /// </summary>
        public static ElementTheme Theme { get; set; } = ElementTheme.Default;

        /// <summary>
        /// ボタンの表示文字列を返す関数。未設定の時は英語("OK"/"Cancel"等)を使う。
        /// 多言語化はこのライブラリでは持たず、アプリ側が起動時に設定する。
        /// </summary>
        public static Func<MessageDialogResult, string?>? ButtonLabelProvider { get; set; }

        /// <summary>
        /// メッセージダイアログを表示し、押されたボタンを返す。
        /// </summary>
        /// <param name="text">本文。</param>
        /// <param name="caption">ウィンドウのタイトル。</param>
        /// <param name="buttons">ボタンの組み合わせ。</param>
        /// <param name="icon">アイコン。</param>
        /// <param name="defaultButton">最初に選ばれている(Enterで押される)ボタン。</param>
        public static Task<MessageDialogResult> ShowAsync(
            string text,
            string caption = "",
            MessageDialogButtons buttons = MessageDialogButtons.Ok,
            MessageDialogIcon icon = MessageDialogIcon.None,
            MessageDialogDefaultButton defaultButton = MessageDialogDefaultButton.First)
        {
            var window = new MessageDialogWindow(text, caption, buttons, icon, defaultButton);
            window.Present();
            return window.Result;
        }

        internal static string GetLabel(MessageDialogResult result)
        {
            var custom = ButtonLabelProvider?.Invoke(result);
            if (!string.IsNullOrEmpty(custom))
                return custom;

            return result switch
            {
                MessageDialogResult.Ok => "OK",
                MessageDialogResult.Cancel => "Cancel",
                MessageDialogResult.Abort => "Abort",
                MessageDialogResult.Retry => "Retry",
                MessageDialogResult.Ignore => "Ignore",
                MessageDialogResult.Yes => "Yes",
                MessageDialogResult.No => "No",
                MessageDialogResult.TryAgain => "Try Again",
                MessageDialogResult.Continue => "Continue",
                _ => result.ToString(),
            };
        }
    }
}
