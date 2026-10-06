using UiLibrary;
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
            var message = string.Format(Common.Utils.AppStrings.Get("Update_ConfirmMessage"), version);

            // ボタンの文字列とテーマは、アプリ側の設定(言語・テーマ)に合わせる(ConfirmPrompt)
            return await ConfirmPrompt.ConfirmAsync(message, MessageDialogIcon.Information);
        }
    }
}
