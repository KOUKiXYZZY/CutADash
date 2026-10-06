using Common.Models;
using Microsoft.UI.Xaml;
using System.Threading.Tasks;
using UiLibrary;

namespace CutADash.Utils
{
    /// <summary>
    /// OK/キャンセルで確認するダイアログ。UpdatePromptと同じく、Windowを必要としない
    /// 自作のMessageDialog(UiLibraryプロジェクト)を使い、ボタンの文字列とテーマは
    /// アプリ側の設定(言語・テーマ)に合わせる。
    /// </summary>
    internal static class ConfirmPrompt
    {
        /// <summary>OKが押されたらtrue、キャンセルや×で閉じられたらfalse。</summary>
        public static async Task<bool> ConfirmAsync(string message, MessageDialogIcon icon = MessageDialogIcon.Question)
        {
            Prepare();

            var result = await MessageDialog.ShowAsync(
                message, "CutADash", MessageDialogButtons.OkCancel, icon);

            return result == MessageDialogResult.Ok;
        }

        /// <summary>OKだけのダイアログで、結果を知らせる。</summary>
        public static async Task NotifyAsync(string message, MessageDialogIcon icon = MessageDialogIcon.Information)
        {
            Prepare();

            await MessageDialog.ShowAsync(message, "CutADash", MessageDialogButtons.Ok, icon);
        }

        // ボタンの文字列とテーマは、アプリ側の設定(言語・テーマ)に合わせる
        private static void Prepare()
        {
            MessageDialog.ButtonLabelProvider = button => button switch
            {
                MessageDialogResult.Ok => Common.Utils.AppStrings.Get("Dialog_Ok"),
                MessageDialogResult.Cancel => Common.Utils.AppStrings.Get("Dialog_Cancel"),
                _ => null,
            };

            MessageDialog.Theme = Preferences.PreferencesGateway.GetColorTheme() switch
            {
                AppColorTheme.Dark => ElementTheme.Dark,
                AppColorTheme.Light => ElementTheme.Light,
                _ => ElementTheme.Default,
            };
        }
    }
}
