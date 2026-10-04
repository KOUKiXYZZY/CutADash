using Common.Extension;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using WinUIEx;
using Windows.ApplicationModel.DataTransfer;

namespace SelectionToolbar
{
    /// <summary>
    /// テキスト選択のすぐそばに出す、コピー/検索(/URLを開く)の小さなツールバー。
    /// MainWindow(パレット)と同じく、フォーカスを奪わずに表示し(WS_EX_NOACTIVATE)、
    /// 外側をクリックしたら自動的に閉じる。
    /// </summary>
    public sealed partial class SelectionToolbarWindow : WindowEx
    {
        // スキーム無しの「URLの一部」(www.example.com、example.com/path等)を緩く判定する。
        // ドット区切りのドメインらしき形をしているかだけを見て、実際に開く時にhttps://を補う
        private static readonly Regex UrlLikeRegex = new(
            @"^[\w-]+(\.[\w-]+)+(/\S*)?$",
            RegexOptions.Compiled);

        // Copy/Search/Closeの3ボタン、かつ英語("Search"など)でも文字が
        // 切れない余裕を持たせた幅(DIP)。URLを開くボタンが出る時だけ、
        // 3ボタン目ぶんの幅を追加する(WidthWithUrlButton)
        private const int WidthDip = 150;
        private const int WidthDipWithUrlButton = 230;

        private readonly ToolbarWindowHelper _helper;
        private string _text = string.Empty;
        private string? _urlToOpen;

        public SelectionToolbarWindow()
        {
            InitializeComponent();

            LocalizeUi();

            // MainWindowと同じ明/暗の表示テーマ(Light/Dark)に揃える。OSのテーマには追従しない
            this.ApplyColorTheme(Preferences.PreferencesGateway.GetColorTheme());
            Preferences.PreferencesGateway.ColorThemeChanged += theme =>
                this.DispatcherQueue.TryEnqueue(() => this.ApplyColorTheme(theme));

            // 選択したテキストの上に指が被らないよう、選択位置の上に表示する
            _helper = new ToolbarWindowHelper(this, RootBorder, widthDip: WidthDip, heightDip: 32);
        }

        private void LocalizeUi()
        {
            CopyButtonText.Text = Utils.ToolbarStrings.Get("Toolbar_Copy");
            SearchButtonText.Text = Utils.ToolbarStrings.Get("Toolbar_Search");
            OpenUrlButtonText.Text = Utils.ToolbarStrings.Get("Toolbar_OpenUrl");

            ToolTipService.SetToolTip(CopyButton, Utils.ToolbarStrings.Get("Toolbar_Copy"));
            ToolTipService.SetToolTip(SearchButton, Utils.ToolbarStrings.Get("Toolbar_Search"));
            ToolTipService.SetToolTip(OpenUrlButton, Utils.ToolbarStrings.Get("Toolbar_OpenUrl"));
            ToolTipService.SetToolTip(CloseButton, Utils.ToolbarStrings.Get("Toolbar_Close"));
        }

        /// <summary>指定した画面座標の近くへ、コピー/検索(/URLを開く)ボタンを、フォーカスを奪わずに表示する。</summary>
        public void ShowNear(int screenX, int screenY, string text)
        {
            _text = text;

            var hasUrl = TryGetUrlToOpen(text, out _urlToOpen);
            OpenUrlButton.Visibility = hasUrl ? Visibility.Visible : Visibility.Collapsed;
            OpenUrlColumn.Width = hasUrl ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

            _helper.ShowAt(screenX, screenY, hasUrl ? WidthDipWithUrlButton : WidthDip);
        }

        /// <summary>
        /// 選択文字列がURL、またはスキーム無しの「URLの一部」(www.example.com等)と
        /// 判定できるかを調べる。判定できた場合、実際に開くべきURL(スキーム無しなら
        /// https://を補ったもの)をurlに返す。
        /// </summary>
        private static bool TryGetUrlToOpen(string text, out string? url)
        {
            url = null;
            var trimmed = text.Trim();

            // URL単体の選択のみを対象とする(文中の一部にURLが混ざっているだけの
            // 長い選択テキストは対象外。空白や改行を含む時点でURL単体ではない)
            if (trimmed.Length == 0 || trimmed.Contains(' ') || trimmed.Contains('\n'))
                return false;

            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute)
                && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
            {
                url = trimmed;
                return true;
            }

            if (UrlLikeRegex.IsMatch(trimmed))
            {
                url = "https://" + trimmed;
                return true;
            }

            return false;
        }

        public void HideNoActivate() => _helper.HideNoActivate();

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            var package = new DataPackage();
            package.SetText(_text);
            Clipboard.SetContent(package);

            HideNoActivate();
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var url = "https://www.google.com/search?q=" + Uri.EscapeDataString(_text);
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SelectionToolbarWindow] 検索の起動に失敗しました: {ex}");
            }

            HideNoActivate();
        }

        private void OpenUrlButton_Click(object sender, RoutedEventArgs e)
        {
            if (_urlToOpen is not null)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(_urlToOpen) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SelectionToolbarWindow] URLを開くのに失敗しました: {ex}");
                }
            }

            HideNoActivate();
        }

        // 何もせずポップアップを閉じるだけのボタン
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            HideNoActivate();
        }
    }
}
