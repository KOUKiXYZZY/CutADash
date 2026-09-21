using CutADash.Infra.Win32;
using CutADash.Models;
using CutADash.Views;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using WinUIEx;

namespace CutADash.Utils
{
    /// <summary>
    /// 履歴のEnter/ダブルクリック、Contentsのエンコード/デコード結果など、
    /// 「OSクリップボードへ書き戻して直前のフォアグラウンドウィンドウへペーストする」
    /// という同じ流れを複数箇所で使うための共通処理。
    /// </summary>
    public static class ForegroundPasteHelper
    {
        /// <param name="addToHistory">
        /// trueの場合、書き戻しをクリップボード監視に検知させ、新規コピーとして
        /// 履歴に追加させる(Encode/Decode結果など、新しく生まれた内容を残したい場合に使う)。
        /// falseの場合は従来通り検知を抑止し、履歴への再登録を防ぐ
        /// (既存の履歴項目の再ペーストで、重複登録を避けたい場合に使う)。
        /// </param>
        /// <param name="paste">
        /// trueの場合、直前のフォアグラウンドウィンドウへ実際にCtrl+Vで貼り付ける。
        /// falseの場合はOSクリップボードへ書き戻すだけで、貼り付けは行わない
        /// (Encode/Decodeのように、結果をクリップボードと履歴に残すだけで良い場合に使う)。
        /// </param>
        /// <param name="plainTextOnly">
        /// trueの場合、リッチテキスト(Rtf/Html)を保持していてもプレーンテキストとしてのみ
        /// 貼り付ける(Contents画面の「プレーンテキストとして貼り付け」用)。
        /// </param>
        public static async Task PasteToPreviousWindowAsync(
            MainWindow? mainWindow, ClipboardItem item, bool addToHistory = false, bool paste = true,
            bool plainTextOnly = false)
        {
            PasteDiagnosticsLog.Write(
                $"[Helper] 呼び出し paste={paste} addToHistory={addToHistory} plainTextOnly={plainTextOnly} " +
                $"mainWindowあり={mainWindow is not null} " +
                $"貼り付け先={PasteDiagnosticsLog.Describe(mainWindow?.PreviousForegroundWindow ?? IntPtr.Zero)}");

            // これから書き戻すクリップボード変更を、新規コピーとして履歴に再登録して
            // しまわないようにする。1回の書き込みで変更通知が複数回来るため、
            // 回数ではなくクリップボードの通し番号で「自分由来」を判定させる
            var monitor = addToHistory
                ? null
                : mainWindow?.Provider?.GetService<ClipboardMonitor>();

            if (addToHistory)
            {
                // 履歴に追加された直後、その項目を自動的に選択状態にする
                mainWindow?.Provider?.GetService<ViewModels.ClipboardListViewModel>()?.SelectNextEnqueuedItem();
            }

            // タスクトレイの「常にテキストでペースト」がオンなら、呼び出し元の指定に関わらず
            // プレーンテキスト貼り付けを強制する
            var forcePlainText = plainTextOnly || Preferences.PreferencesGateway.IsAlwaysPastePlainText();

            monitor?.BeginOwnWrite();
            try
            {
                await ClipboardContentWriter.SetClipboardContentAsync(item, forcePlainText);
            }
            finally
            {
                monitor?.EndOwnWrite();
            }

            if (paste)
            {
                var targetHwnd = mainWindow?.PreviousForegroundWindow ?? IntPtr.Zero;

                if (targetHwnd != IntPtr.Zero)
                {
                    if (mainWindow is not null)
                        mainWindow.IsPasting = true;

                    try
                    {
                        var focusHwnd = mainWindow?.PreviousFocusWindow ?? IntPtr.Zero;
                        await ForegroundPaster.ActivateAndPasteAsync(targetHwnd, focusHwnd);
                    }
                    catch (Exception ex)
                    {
                        // 呼び出し元がasync voidのイベントハンドラのため、ここで例外を
                        // 記録しておかないと無言で握りつぶされる。実際、P/Invoke宣言の
                        // 追加漏れによるMissingMethodExceptionがこの経路で見えなくなり、
                        // 「クリップボードには入るのに貼り付かない」状態の原因特定に時間を要した
                        PasteDiagnosticsLog.Write($"[Helper] 例外: {ex}");
                        throw;
                    }
                    finally
                    {
                        if (mainWindow is not null)
                            mainWindow.IsPasting = false;
                    }
                }
                else
                {
                    // ここに来ると、クリップボードへの書き戻しは成功しているのにキーは
                    // 一切送られない(コピーはされるが貼り付かない)状態になる
                    PasteDiagnosticsLog.Write("[Helper] 貼り付け先が未記憶(PreviousForegroundWindow=0)のため、キー送信を行わなかった");
                }

                // ペーストが完了してからウィンドウを隠す(すぐに消えないようにする)。
                // HidePaletteはキー入力/マウス/フォアグラウンドの監視停止も兼ねる。
                // 貼り付けを行わない場合(Encode/Decodeなど)は、結果をその場で
                // 確認できるようパレットは開いたままにする
                mainWindow?.HidePalette();

                // 溜めておいた診断ログは、ペーストへ影響しないここで書き出す
                PasteDiagnosticsLog.Flush();
            }
        }
    }
}
