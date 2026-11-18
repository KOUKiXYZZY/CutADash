using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace CutADash.Views.ListFrame
{
    /// <summary>
    /// 履歴・お気に入り一覧の見た目(文字サイズ・行の余白・アイコンの大きさ)の設定値。
    /// DataTemplateの中からは、ページのコードビハインドの値を直接参照できず、StaticResourceの
    /// 値も実行中には書き換わらない。そのため、変更通知を持つこのオブジェクトをアプリの
    /// リソース("ListAppearance")に置き、テンプレートから
    /// {Binding Source={StaticResource ListAppearance}, Path=...}で参照する。
    /// 設定画面で変えると、表示中の一覧にもそのまま反映される。
    /// </summary>
    internal sealed partial class ListAppearance : ObservableObject
    {
        // 文字サイズ16のときの、各アイコン/行の元のサイズ。文字サイズに比例させて拡縮する
        private const double BaseFontSize = 16;

        private static ListAppearance? _instance;

        /// <summary>アプリ全体で共有する唯一のインスタンス。</summary>
        public static ListAppearance Instance => _instance ??= new ListAppearance();

        [ObservableProperty] private double fontSize = BaseFontSize;
        [ObservableProperty] private double badgeFontSize = 14;
        [ObservableProperty] private double fileIconSize = 18;
        [ObservableProperty] private double folderIconSize = 16;
        [ObservableProperty] private double catGlyphSmallSize = 13;
        [ObservableProperty] private double catGlyphMidSize = 15;
        [ObservableProperty] private double catCircleSize = 28;
        [ObservableProperty] private Thickness badgePadding = new(10, 4, 10, 4);
        [ObservableProperty] private CornerRadius badgeCornerRadius = new(12);
        [ObservableProperty] private Thickness rowPadding = new(8, 9, 8, 9);
        [ObservableProperty] private Thickness dividerMargin = new(-8, 0, -8, -9);
        [ObservableProperty] private Thickness catCardPadding = new(10, 10, 10, 10);

        private ListAppearance()
        {
            Update(Preferences.PreferencesGateway.GetListFontSize(), Preferences.PreferencesGateway.GetListRowPadding());

            // アプリと同じ寿命のため購読解除は不要。イベントは設定画面(UIスレッド)から飛ぶ
            Preferences.PreferencesGateway.ListFontSizeChanged +=
                size => Update(size, Preferences.PreferencesGateway.GetListRowPadding());
            Preferences.PreferencesGateway.ListRowPaddingChanged +=
                padding => Update(Preferences.PreferencesGateway.GetListFontSize(), padding);
        }

        /// <summary>
        /// 一覧のテンプレートが参照するリソースとして登録する。XAMLの読み込み
        /// (InitializeComponent)より前に呼ぶ必要がある。
        /// </summary>
        public static void EnsureResources()
        {
            Application.Current.Resources["ListAppearance"] = Instance;
        }

        private void Update(double size, double padding)
        {
            var scale = size / BaseFontSize;

            FontSize = size;
            BadgeFontSize = 14 * scale;
            FileIconSize = 18 * scale;
            FolderIconSize = size;
            CatGlyphSmallSize = 13 * scale;
            CatGlyphMidSize = 15 * scale;

            // Image/Shape等のバッジ(角丸の枠)。縦の余白は文字サイズに比例させつつ、
            // 行の余白が小さい時はそれ以上に枠が行を押し広げないよう、行の余白の半分までに抑える
            CatCircleSize = 28 * scale;
            var badgeVertical = Math.Min(4 * scale, padding / 2);
            BadgePadding = new Thickness(10 * scale, badgeVertical, 10 * scale, badgeVertical);
            BadgeCornerRadius = new CornerRadius(12 * scale);

            // 横の余白(8)は従来のまま。区切り線は行の下端まで届かせるため、下の余白の分だけはみ出させる
            RowPadding = new Thickness(8, padding, 8, padding);
            DividerMargin = new Thickness(-8, 0, -8, -padding);
            // 猫テーマのカードは通常より1px広めの余白(従来は10)にしている
            CatCardPadding = new Thickness(10, padding + 1, 10, padding + 1);
        }
    }
}
