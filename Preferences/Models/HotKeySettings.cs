using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Preferences.Models
{
    /// <summary>
    /// クリップボード履歴などを開くショートカットキーの設定。
    /// Modifiers/VirtualKeyはWin32のRegisterHotKeyに渡す値と同じ形式。
    /// </summary>
    public class HotKeySettings
    {
        public uint Modifiers { get; set; }
        public uint VirtualKey { get; set; }
    }

    [JsonSerializable(typeof(HotKeySettings))]
    [JsonSerializable(typeof(Dictionary<string, HotKeySettings>))]
    internal partial class HotKeySettingsContext : JsonSerializerContext { }
}
