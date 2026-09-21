using Common.Models;
using Common.Utils;
using Preferences.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Preferences.Utils
{
    /// <summary>
    /// History設定(保持する履歴件数など)の読み込み・保存。
    /// %LOCALAPPDATA%\CutADash\history_settings.jsonへ保存する。
    /// 各設定の読み込み(GetXxx)・書き込み(SetXxx)・変更通知(Changedイベント)は
    /// すべてこのStoreが担う。PreferencesGatewayはこのStoreからの読み込み(GetXxx)だけを
    /// 中継する薄いファサードで、書き込みや通知の責務は持たない。
    /// </summary>
    internal static class HistorySettingsStore
    {
        private static readonly string FilePath = AppPaths.GetDataFilePath("history_settings.json");
        private const int DefaultMaxHistoryCount = 50;

        public static HistorySettings Load()
        {
            if (!File.Exists(FilePath))
                return new HistorySettings { MaxHistoryCount = DefaultMaxHistoryCount };

            try
            {
                var json = File.ReadAllText(FilePath);
                var data = JsonSerializer.Deserialize(json, HistorySettingsContext.Default.HistorySettings);
                return data ?? new HistorySettings { MaxHistoryCount = DefaultMaxHistoryCount };
            }
            catch
            {
                return new HistorySettings { MaxHistoryCount = DefaultMaxHistoryCount };
            }
        }

        public static void Save(HistorySettings settings)
        {
            var json = JsonSerializer.Serialize(settings, HistorySettingsContext.Default.HistorySettings);
            File.WriteAllText(FilePath, json);
        }

        /// <summary>MaxHistoryCountが変更された時に発火する。</summary>
        public static event Action<int>? MaxHistoryCountChanged;

        /// <summary>Historyとして保持しておける件数の上限を設定する。</summary>
        public static void SetMaxHistoryCount(int maxHistoryCount)
        {
            var settings = Load();
            settings.MaxHistoryCount = maxHistoryCount;
            Save(settings);

            MaxHistoryCountChanged?.Invoke(maxHistoryCount);
        }

        /// <summary>DisableImageHistoryが変更された時に発火する。</summary>
        public static event Action<bool>? ImageHistoryDisabledChanged;

        /// <summary>画像をHistoryに登録しない設定を切り替える。</summary>
        public static void SetImageHistoryDisabled(bool disabled)
        {
            var settings = Load();
            settings.DisableImageHistory = disabled;
            Save(settings);

            ImageHistoryDisabledChanged?.Invoke(disabled);
        }

        /// <summary>ThumbnailMaxDimensionが変更された時に発火する。</summary>
        public static event Action<int>? ThumbnailMaxDimensionChanged;

        /// <summary>一覧表示用サムネイルの最大辺の長さ(px)を設定する。</summary>
        public static void SetThumbnailMaxDimension(int dimension)
        {
            var settings = Load();
            settings.ThumbnailMaxDimension = dimension;
            Save(settings);

            ThumbnailMaxDimensionChanged?.Invoke(dimension);
        }

        /// <summary>DisableCaretPositioningが変更された時に発火する。</summary>
        public static event Action<bool>? CaretPositioningDisabledChanged;

        /// <summary>ウィンドウをキャレット位置へ移動する機能の無効/有効を切り替える。</summary>
        public static void SetCaretPositioningDisabled(bool disabled)
        {
            var settings = Load();
            settings.DisableCaretPositioning = disabled;
            Save(settings);

            CaretPositioningDisabledChanged?.Invoke(disabled);
        }

        /// <summary>DisableWindowsClipboardHistoryが変更された時に発火する。</summary>
        public static event Action<bool>? WindowsClipboardHistoryDisabledChanged;

        /// <summary>Windows標準のクリップボード履歴(Win+Vパネル)の無効/有効を切り替える。</summary>
        public static void SetWindowsClipboardHistoryDisabled(bool disabled)
        {
            ClipboardHistoryHelper.SetEnabled(disabled);

            var settings = Load();
            settings.DisableWindowsClipboardHistory = disabled;
            Save(settings);

            WindowsClipboardHistoryDisabledChanged?.Invoke(disabled);
        }

        /// <summary>SelectionToolbarEnabledが変更された時に発火する。</summary>
        public static event Action<bool>? SelectionToolbarEnabledChanged;

        /// <summary>テキスト選択ツールバー(試験的機能)の有効/無効を切り替える。</summary>
        public static void SetSelectionToolbarEnabled(bool enabled)
        {
            var settings = Load();
            settings.SelectionToolbarEnabled = enabled;
            Save(settings);

            SelectionToolbarEnabledChanged?.Invoke(enabled);
        }

        /// <summary>AlwaysPastePlainTextが変更された時に発火する。</summary>
        public static event Action<bool>? AlwaysPastePlainTextChanged;

        /// <summary>常にプレーンテキストとして貼り付けるかどうかを切り替える。</summary>
        public static void SetAlwaysPastePlainText(bool enabled)
        {
            var settings = Load();
            settings.AlwaysPastePlainText = enabled;
            Save(settings);

            AlwaysPastePlainTextChanged?.Invoke(enabled);
        }

        /// <summary>WindowBackdropが変更された時に発火する。</summary>
        public static event Action<WindowBackdropKind>? WindowBackdropChanged;

        /// <summary>メインウィンドウ(パレット)の背景素材(バックドロップ)を設定する。</summary>
        public static void SetWindowBackdrop(WindowBackdropKind backdrop)
        {
            var settings = Load();
            settings.WindowBackdrop = backdrop;
            Save(settings);

            WindowBackdropChanged?.Invoke(backdrop);
        }

        /// <summary>SelectionToolbarAutoHideSecondsが変更された時に発火する。</summary>
        public static event Action<double>? SelectionToolbarAutoHideSecondsChanged;

        /// <summary>
        /// テキスト選択ツールバーを自動的に隠すまでの秒数を設定する。0(無効)以外の値は
        /// バックアップ(HistorySettings.SelectionToolbarAutoHideSecondsBackup)としても覚えておく。
        /// </summary>
        public static void SetSelectionToolbarAutoHideSeconds(double seconds)
        {
            var settings = Load();
            settings.SelectionToolbarAutoHideSeconds = seconds;
            if (seconds > 0)
                settings.SelectionToolbarAutoHideSecondsBackup = seconds;
            Save(settings);

            SelectionToolbarAutoHideSecondsChanged?.Invoke(seconds);
        }

        /// <summary>ExcludedAppNamesが変更された時に発火する。</summary>
        public static event Action<List<string>>? ExcludedAppNamesChanged;

        /// <summary>コピーを履歴に登録しないアプリのプロセス名一覧を保存する。</summary>
        public static void SetExcludedAppNames(List<string> appNames)
        {
            var settings = Load();
            settings.ExcludedAppNames = appNames;
            Save(settings);

            ExcludedAppNamesChanged?.Invoke(appNames);
        }

        /// <summary>Languageが変更された時に発火する。</summary>
        public static event Action<string>? LanguageChanged;

        /// <summary>
        /// 表示言語のBCP-47タグ(例: "ja-JP"/"en-US"/"zh-CN"、空文字列はシステム既定)を設定する。
        /// </summary>
        public static void SetLanguage(string language)
        {
            var settings = Load();
            settings.Language = language;
            Save(settings);

            LanguageChanged?.Invoke(language);
        }
    }
}
