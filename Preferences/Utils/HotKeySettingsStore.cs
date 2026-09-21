using Common.Infra.Win32;
using Common.Utils;
using Preferences.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Preferences.Utils
{
    /// <summary>
    /// ショートカットキー設定の読み込み・保存。複数のショートカットを名前(key)ごとに
    /// %LOCALAPPDATA%\CutADash\hotkey_settings.json へまとめて保存する。
    /// </summary>
    internal static class HotKeySettingsStore
    {
        private static readonly string FilePath = AppPaths.GetDataFilePath("hotkey_settings.json");

        /// <summary>
        /// 指定したキーのショートカットが変更/削除された時に発火する
        /// (削除時はdefinition=null)。Win32への実際の登録変更(HotKeyService.Update/Remove)は
        /// このStoreの責務ではないため、購読側(App.xaml.cs、PreferencesGateway経由)が
        /// この通知を受けて反映する。
        /// </summary>
        public static event Action<string, HotKeyDefinition?>? HotKeyChanged;

        /// <summary>
        /// 指定したキーの設定を読み込む。保存されていなければdefaultDefinitionを返す
        /// (defaultDefinitionがnullなら「未設定」を表すnullを返す)。
        /// </summary>
        public static HotKeyDefinition? LoadOrDefault(string key, HotKeyDefinition? defaultDefinition)
        {
            var all = LoadAll();
            if (all.TryGetValue(key, out var data))
                return new HotKeyDefinition(data.Modifiers, data.VirtualKey);

            return defaultDefinition;
        }


        /// <summary>
        /// 指定したキーの設定を保存する。すでに保存されている場合は上書きする。
        /// </summary>
        /// <param name="key"></param>
        /// <param name="definition"></param>
        public static void Save(string key, HotKeyDefinition definition)
        {
            var all = LoadAll();
            all[key] = new HotKeySettings
            {
                Modifiers = definition.Modifiers,
                VirtualKey = definition.VirtualKey
            };

            SaveAll(all);

            HotKeyChanged?.Invoke(key, definition);
        }

        /// <summary>
        /// 指定したキーのショートカット設定を削除する(未設定状態に戻す)。
        /// </summary>
        public static void Remove(string key)
        {
            var all = LoadAll();
            if (all.Remove(key))
            {
                SaveAll(all);
            }

            HotKeyChanged?.Invoke(key, null);
        }

        /// <summary>
        /// すべてのショートカット設定を読み込む。保存されていなければ空の辞書を返す。
        /// </summary>
        /// <returns></returns>
        private static Dictionary<string, HotKeySettings> LoadAll()
        {
            if (!File.Exists(FilePath))
                return new();

            try
            {
                var json = File.ReadAllText(FilePath);
                var data = JsonSerializer.Deserialize(json, HotKeySettingsContext.Default.DictionaryStringHotKeySettings);
                return data ?? new();
            }
            catch
            {
                return new();
            }
        }

        /// <summary>
        /// すべてのショートカット設定を保存する。
        /// </summary>
        /// <param name="all"></param>
        private static void SaveAll(Dictionary<string, HotKeySettings> all)
        {
            var json = JsonSerializer.Serialize(all, HotKeySettingsContext.Default.DictionaryStringHotKeySettings);
            File.WriteAllText(FilePath, json);
        }
    }
}
