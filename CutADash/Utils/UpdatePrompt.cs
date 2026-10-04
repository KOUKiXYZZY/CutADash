using Common.Models;
using UiLibrary;
using Microsoft.UI.Xaml;
using System.Threading.Tasks;

namespace CutADash.Utils
{
    /// <summary>
    /// アップデートがある時に、「更新しますか?」をOK/キャンセルで尋ねるダイアログ。
    ///
    /// WinUI 3のContentDialogは、表示中のWindow内の要素のXamlRootが必要で、Windowが無いと出せない。
    /// このアプリはタスクトレイ常駐で、起動時やメニュー操作の時点では、メインウィンドウが
    /// 非表示のことがほとんどなので、Windowを必要としない自作のMessageDialog(UiLibraryプロジェクト)を使う。
    /// </summary>
    internal static class UpdatePrompt
    {
        /// <summary>OKが押されたらtrue、キャンセルや×で閉じられたらfalse。</summary>
        public static async Task<bool> ConfirmUpdateAsync(string version)
        {
            // ボタンの文字列とテーマは、アプリ側の設定(言語・テーマ)に合わせる
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

            var message = string.Format(Common.Utils.AppStrings.Get("Update_ConfirmMessage"), version);

            var result = await MessageDialog.ShowAsync(
                message, "CutADash", MessageDialogButtons.OkCancel, MessageDialogIcon.Information);

            return result == MessageDialogResult.Ok;
        }
    }
}
