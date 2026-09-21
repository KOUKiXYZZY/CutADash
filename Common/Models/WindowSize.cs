using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Common.Models
{
    // ウィンドウ名をキーに1つのファイル(window_settings.json)へまとめて保存する。
    // 値の型はウィンドウごとに異なりうる(MainWindowだけMainWindowSize)ため、
    // 辞書自体はDictionary<string, JsonElement>として読み書きし、各キーの中身は
    // 呼び出し側が要求した型(WindowSize/MainWindowSize)へその場でデシリアライズする
    // (WindowExtensionsHelpers.SaveWindowSize/RestoreWindowSize参照)
    [JsonSerializable(typeof(WindowSize))]
    [JsonSerializable(typeof(MainWindowSize))]
    [JsonSerializable(typeof(Dictionary<string, JsonElement>))]
    internal partial class WindowSizeContext : JsonSerializerContext { }

    public class WindowSize
    {
        public int Width { get; set; }
        public int Height { get; set; }

        /// <summary>
        /// ウィンドウ位置(物理ピクセル、AppWindow.Positionと同じ座標系)。
        /// キャレット位置に合わせて毎回動かすMainWindowなどでは保存しないためnullになりうる。
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? X { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Y { get; set; }
    }

    /// <summary>
    /// MainWindow専用。ContentSizer(セパレータ)でドラッグして決めたListFrameの幅(DIP)を
    /// 追加で持つ。他のウィンドウ(PreferenceWindow/SequentialPasteWindow等)はこの値を
    /// 使わないため、基底のWindowSizeのまま保存/復元する。
    /// </summary>
    public class MainWindowSize : WindowSize
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? ListFrameWidth { get; set; }
    }
}
