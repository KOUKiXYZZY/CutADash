using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Collections.Generic;
using Common.Models;
using WinAPI;

namespace Theming
{
    /// <summary>
    /// 猫テーマ固有の見た目のルールを1か所へまとめたもの。MainWindow/ListFrame/
    /// SequentialPasteWindowなど、猫テーマを扱う各ウィンドウ・ページのコード側は
    /// ここを呼ぶだけにして、テーマの実装(色・角丸・ListViewの選択表現)を
    /// 個別のファイルへ埋め込まないようにする。
    ///
    /// テーマを増やす場合は、このファイルと同じ形で別クラス(例: SweetTheme)を
    /// 追加し、呼び出し側は「今どのテーマか」を判定して該当クラスを呼ぶだけにする。
    /// </summary>
    public static class CatTheme
    {
        public static bool IsActive(WindowBackdropKind kind) => kind == WindowBackdropKind.Cat;

        /// <summary>
        /// ウィンドウのルート要素に重ねる、暖色のパステル(クリーム/ピーチ)なティント。
        /// 猫テーマでなければnull(=重ねない)を返す。
        ///
        /// 明/暗テーマで色を変えず同じ固定色にしているが、Mica自体の地の色はOSのテーマに
        /// 応じて変わる(WinUIの仕様上ここでは打ち消せない)ため、完全な統一はできない。
        /// alphaを上げてMicaの地をできるだけ覆い隠し、差が目立たないようにしている。
        /// </summary>
        public static Brush? GetBackdropTint(WindowBackdropKind kind)
            => IsActive(kind)
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(190, 255, 213, 179))
                : null;

        /// <summary>
        /// ウィンドウの角丸設定。猫テーマの間は、設定(AppWindowCornerPreference)に
        /// 関わらず一番丸いDWMWCP_ROUNDを返す。それ以外はfallbackをそのまま返す。
        /// </summary>
        public static DwmAPI.DWM_WINDOW_CORNER_PREFERENCE ResolveCornerPreference(
            WindowBackdropKind kind, DwmAPI.DWM_WINDOW_CORNER_PREFERENCE fallback)
            => IsActive(kind) ? DwmAPI.DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND : fallback;

        // ListViewItemの既定背景(選択/ホバー/押下)を差し替えるための、
        // 内部コントロールテンプレートが参照するテーマリソースキー
        private static readonly string[] ListViewItemBackgroundResourceKeys =
        {
            "ListViewItemBackground",
            "ListViewItemBackgroundPointerOver",
            "ListViewItemBackgroundPressed",
            "ListViewItemBackgroundSelected",
            "ListViewItemBackgroundSelectedPointerOver",
            "ListViewItemBackgroundSelectedPressed",
            "ListViewItemBackgroundDisabled",
        };

        /// <summary>
        /// 猫テーマの間、ListViewの既定の選択/ホバー/押下ハイライト(白やアクセント色)が
        /// 出ないよう、対象のListView.Resourcesへ透明なブラシを差し込む。
        /// カード自体の見た目はItemTemplate側が持つため、選択の視覚的な表現は
        /// 何も出さない(それがこのテーマでの狙い)。猫テーマでなくなったら元に戻す。
        /// </summary>
        public static void ApplyListViewSelectionStyle(ListView listView, WindowBackdropKind kind)
        {
            if (IsActive(kind))
            {
                var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                foreach (var key in ListViewItemBackgroundResourceKeys)
                    listView.Resources[key] = transparent;
            }
            else
            {
                foreach (var key in ListViewItemBackgroundResourceKeys)
                    listView.Resources.Remove(key);
            }
        }
    }
}
