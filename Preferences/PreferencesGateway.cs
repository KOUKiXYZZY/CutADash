using Common.Infra.Win32;
using Common.Models;
using Preferences.Utils;
using System;
using System.Collections.Generic;

namespace Preferences
{
    /// <summary>
    /// 他プロジェクト(CutADash/SequentialPaste/SelectionToolbar)が設定関連の情報を
    /// 読み込む/書き込む/変更通知を購読するための唯一の窓口。実際の永続化
    /// (HistorySettingsStore/HotKeySettingsStoreなど)はPreferencesプロジェクト内にすべて
    /// internalとして閉じており、他プロジェクトからは参照できない。これらのStoreへ
    /// 直接アクセスできるのはPreferencesプロジェクト内(PreferenceViewModel等)だけで、
    /// 他プロジェクトは必ずこのGatewayを経由する。
    ///
    /// 書き込み(SetXxx)は基本的にStore自身の役割のままだが、他プロジェクトから
    /// 書き込みたいものだけ、このGateway側にも薄いラッパーを用意する
    /// (例: SetAlwaysPastePlainText。タスクトレイのメニューから呼ばれる)。
    ///
    /// 変更通知(Changedイベント)は、以前はStoreのイベントを他プロジェクトが直接購読していたが、
    /// それだと内部実装(Store)を素通りして知ってしまい、このGatewayが「唯一の窓口」として
    /// 機能しなくなる。そこでこのGatewayが自分でStore側のイベントを購読(Observer)し、
    /// 同じ形で自分のイベントとして
    /// 中継する(Observable)ことで、CutADash側は常にこのGatewayだけを見ればよいようにする。
    /// </summary>
    public static class PreferencesGateway
    {
        // 各Changedイベントを、対応するStoreのイベントへそのまま中継する。
        // 静的コンストラクタでの一括購読(StoreEvent += (v) => GatewayEvent?.Invoke(v))は
        // 型引数ごとにヘルパーを書く必要があり冗長なため、各イベントの隣に直接書く方式にする
        static PreferencesGateway()
        {
            HistorySettingsStore.MaxHistoryCountChanged += v => MaxHistoryCountChanged?.Invoke(v);
            HistorySettingsStore.ImageHistoryDisabledChanged += v => ImageHistoryDisabledChanged?.Invoke(v);
            HistorySettingsStore.ThumbnailMaxDimensionChanged += v => ThumbnailMaxDimensionChanged?.Invoke(v);
            HistorySettingsStore.CaretPositioningDisabledChanged += v => CaretPositioningDisabledChanged?.Invoke(v);
            HistorySettingsStore.WindowsClipboardHistoryDisabledChanged += v => WindowsClipboardHistoryDisabledChanged?.Invoke(v);
            HistorySettingsStore.SelectionToolbarEnabledChanged += v => SelectionToolbarEnabledChanged?.Invoke(v);
            HistorySettingsStore.AlwaysPastePlainTextChanged += v => AlwaysPastePlainTextChanged?.Invoke(v);
            HistorySettingsStore.WindowBackdropChanged += v => WindowBackdropChanged?.Invoke(v);
            HistorySettingsStore.SelectionToolbarAutoHideSecondsChanged += v => SelectionToolbarAutoHideSecondsChanged?.Invoke(v);
            HistorySettingsStore.ExcludedAppNamesChanged += v => ExcludedAppNamesChanged?.Invoke(v);
            HistorySettingsStore.LanguageChanged += v => LanguageChanged?.Invoke(v);
            HotKeySettingsStore.HotKeyChanged += (key, definition) => HotKeyChanged?.Invoke(key, definition);
        }

        public static event Action<int>? MaxHistoryCountChanged;
        public static event Action<bool>? ImageHistoryDisabledChanged;
        public static event Action<int>? ThumbnailMaxDimensionChanged;
        public static event Action<bool>? CaretPositioningDisabledChanged;
        public static event Action<bool>? WindowsClipboardHistoryDisabledChanged;
        public static event Action<bool>? SelectionToolbarEnabledChanged;
        public static event Action<bool>? AlwaysPastePlainTextChanged;
        public static event Action<WindowBackdropKind>? WindowBackdropChanged;
        public static event Action<double>? SelectionToolbarAutoHideSecondsChanged;
        public static event Action<List<string>>? ExcludedAppNamesChanged;
        public static event Action<string>? LanguageChanged;

        /// <summary>
        /// 指定したキーのショートカットが変更/削除された時に発火する(削除時はdefinition=null)。
        /// HotKeyService(Common)自体は永続化もこの通知の発行も知らないため、実際のWin32登録の
        /// 更新はこれを購読した側(App.xaml.cs)がHotKeyServiceを操作して行う。
        /// </summary>
        public static event Action<string, HotKeyDefinition?>? HotKeyChanged;

        /// <summary>Historyとして保持しておける件数の上限。</summary>
        public static int GetMaxHistoryCount()
        {
            return HistorySettingsStore.Load().MaxHistoryCount;
        }

        /// <summary>画像をHistoryに登録しない設定が有効かどうか。</summary>
        public static bool IsImageHistoryDisabled()
        {
            return HistorySettingsStore.Load().DisableImageHistory;
        }

        /// <summary>一覧表示用サムネイルの最大辺の長さ(px)。</summary>
        public static int GetThumbnailMaxDimension()
        {
            return HistorySettingsStore.Load().ThumbnailMaxDimension;
        }

        /// <summary>ウィンドウをキャレット位置へ移動する機能が無効化されているかどうか。</summary>
        public static bool IsCaretPositioningDisabled()
        {
            return HistorySettingsStore.Load().DisableCaretPositioning;
        }

        /// <summary>Windows標準のクリップボード履歴(Win+Vパネル)が無効化されているかどうか。</summary>
        public static bool IsWindowsClipboardHistoryDisabled()
        {
            return HistorySettingsStore.Load().DisableWindowsClipboardHistory;
        }

        /// <summary>テキスト選択ツールバー(試験的機能)が有効かどうか。</summary>
        public static bool IsSelectionToolbarEnabled()
        {
            return HistorySettingsStore.Load().SelectionToolbarEnabled;
        }

        /// <summary>常にプレーンテキストとして貼り付けるかどうか。</summary>
        public static bool IsAlwaysPastePlainText()
        {
            return HistorySettingsStore.Load().AlwaysPastePlainText;
        }

        /// <summary>
        /// 常にプレーンテキストとして貼り付けるかどうかを切り替える。
        /// タスクトレイのメニュー等、Preferences以外のプロジェクトから書き込みたい場合はこちらを使う
        /// (Storeは内部実装のためinternalであり、外から直接は呼べない)。
        /// </summary>
        public static void SetAlwaysPastePlainText(bool enabled)
        {
            HistorySettingsStore.SetAlwaysPastePlainText(enabled);
        }

        /// <summary>メインウィンドウ(パレット)の背景素材(バックドロップ)の種類。</summary>
        public static WindowBackdropKind GetWindowBackdrop()
        {
            return HistorySettingsStore.Load().WindowBackdrop;
        }

        /// <summary>テキスト選択ツールバーを自動的に隠すまでの秒数。</summary>
        public static double GetSelectionToolbarAutoHideSeconds()
        {
            return HistorySettingsStore.Load().SelectionToolbarAutoHideSeconds;
        }

        /// <summary>
        /// テキスト選択ツールバーを自動的に隠すまでの秒数として、直前に0より大きかった値。
        /// チェックを外した後は実際の秒数(GetSelectionToolbarAutoHideSeconds)が0になるが、
        /// 設定画面のスライダーには0ではなくこちらを表示して、再度チェックを入れた時に
        /// 元の値へ戻せるようにする。
        /// </summary>
        public static double GetSelectionToolbarAutoHideSecondsBackup()
        {
            return HistorySettingsStore.Load().SelectionToolbarAutoHideSecondsBackup;
        }

        /// <summary>コピーを履歴に登録しないアプリのプロセス名一覧を読み込む。</summary>
        public static List<string> GetExcludedAppNames()
        {
            return HistorySettingsStore.Load().ExcludedAppNames;
        }

        /// <summary>
        /// 表示言語のBCP-47タグ(例: "ja-JP"/"en-US"/"zh-CN")。空文字列はシステム既定に従う。
        /// </summary>
        public static string GetLanguage()
        {
            // 設定ファイルに"Language":nullや空白だけの文字列が保存されてしまっていた場合の
            // 後方互換(PreferenceViewModel.OnSelectedLanguageTagChanged参照)。
            // null/空白はシステム既定(空文字列)として扱う
            var language = HistorySettingsStore.Load().Language;
            return string.IsNullOrWhiteSpace(language) ? "" : language;
        }

        /// <summary>
        /// 指定したプロセス名のアプリが、コピーの除外対象に含まれているかどうか。
        /// 大文字/小文字は区別しない。
        /// </summary>
        public static bool IsAppExcluded(string? processName)
        {
            if (string.IsNullOrEmpty(processName))
                return false;

            var excluded = GetExcludedAppNames();
            foreach (var name in excluded)
            {
                if (string.Equals(name, processName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 指定したキーのショートカット設定を読み込む。保存されていなければdefaultDefinitionを返す。
        /// </summary>
        public static HotKeyDefinition? LoadHotKeyOrDefault(string key, HotKeyDefinition? defaultDefinition)
        {
            return HotKeySettingsStore.LoadOrDefault(key, defaultDefinition);
        }
    }
}
