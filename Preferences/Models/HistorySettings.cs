using Common.Models;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Preferences.Models
{
    /// <summary>
    /// Historyタブで保持しておく履歴件数などの設定。
    /// </summary>
    internal class HistorySettings
    {
        public int MaxHistoryCount { get; set; } = 50;
        public bool DisableImageHistory { get; set; } = false;
        public int ThumbnailMaxDimension { get; set; } = 200;

        /// <summary>
        /// ホットキーで開いた際、ウィンドウをキャレット位置へ移動する機能を無効にするかどうか。
        /// </summary>
        public bool DisableCaretPositioning { get; set; } = false;

        /// <summary>
        /// Windows標準のクリップボード履歴(Win+Vパネル)を無効にするかどうか。
        /// HKCU\Software\Policies\Microsoft\Windows\System\AllowClipboardHistory(DWORD)を
        /// 0にすることで無効化する(グループポリシー相当)。
        /// </summary>
        public bool DisableWindowsClipboardHistory { get; set; } = false;

        /// <summary>
        /// コピーを履歴に登録しないアプリのプロセス名(拡張子なし。例: "chrome")の一覧。
        /// 大文字/小文字は区別しない。
        /// </summary>
        public List<string> ExcludedAppNames { get; set; } = new();

        /// <summary>
        /// テキスト選択ツールバー(選択直後に近くへ小さなツールバーを出す試験的機能)を
        /// 有効にするかどうか。既定は無効(オプトイン)。
        /// </summary>
        public bool SelectionToolbarEnabled { get; set; } = false;

        /// <summary>メインウィンドウ(パレット)の背景素材(バックドロップ)の種類。</summary>
        public WindowBackdropKind WindowBackdrop { get; set; } = WindowBackdropKind.Acrylic;

        /// <summary>
        /// テキスト選択ツールバーを、操作されないまま自動的に隠すまでの秒数。
        /// SelectionToolbarEnabledがfalseの間は0(無効)にする。
        /// </summary>
        public double SelectionToolbarAutoHideSeconds { get; set; } = 4.0;

        /// <summary>
        /// SelectionToolbarAutoHideSecondsが直前に0より大きかった時の値を覚えておく。
        /// チェックを外すとSelectionToolbarAutoHideSecondsは0になるが、設定画面を
        /// 再度開いた時にスライダーへ0ではなくこちらを表示するために使う。
        /// </summary>
        public double SelectionToolbarAutoHideSecondsBackup { get; set; } = 4.0;

        /// <summary>
        /// 表示言語のBCP-47タグ(例: "ja-JP"/"en-US"/"zh-CN")。空文字列はシステム既定に従う
        /// (Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverrideへ渡す値と同じ)。
        /// </summary>
        public string Language { get; set; } = "";

        /// <summary>
        /// trueの場合、リッチテキスト(Rtf/Html)を保持している項目でも常にプレーンテキストとして
        /// 貼り付ける。タスクトレイメニューのチェックで切り替える。
        /// </summary>
        public bool AlwaysPastePlainText { get; set; } = false;
    }

    [JsonSerializable(typeof(HistorySettings))]
    internal partial class HistorySettingsContext : JsonSerializerContext { }
}
