using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using WinUIEx;
using Windows.ApplicationModel.DataTransfer;

namespace SelectionToolbar
{
    /// <summary>
    /// テキスト選択のすぐそばに出す、コピー/検索だけの小さなツールバー。
    /// MainWindow(パレット)と同じく、フォーカスを奪わずに表示し(WS_EX_NOACTIVATE)、
    /// 外側をクリックしたら自動的に閉じる。
    /// </summary>
    public sealed partial class SelectionToolbarWindow : WindowEx
    {
        private readonly ToolbarWindowHelper _helper;
        private string _text = string.Empty;

        public SelectionToolbarWindow()
        {
            InitializeComponent();

            LocalizeUi();

            // Copy/Search/Closeの3ボタン、かつ英語("Search"など)でも文字が
            // 切れない余裕を持たせた固定サイズ(DIP)。選択したテキストの上に指が
            // 被らないよう、選択位置の上に表示する
            _helper = new ToolbarWindowHelper(this, RootBorder, widthDip: 140, heightDip: 32, showAbove: true);
        }

        private void LocalizeUi()
        {
            CopyButtonText.Text = Utils.ToolbarStrings.Get("Toolbar_Copy");
            SearchButtonText.Text = Utils.ToolbarStrings.Get("Toolbar_Search");

            ToolTipService.SetToolTip(CopyButton, Utils.ToolbarStrings.Get("Toolbar_Copy"));
            ToolTipService.SetToolTip(SearchButton, Utils.ToolbarStrings.Get("Toolbar_Search"));
            ToolTipService.SetToolTip(CloseButton, Utils.ToolbarStrings.Get("Toolbar_Close"));
        }

        /// <summary>指定した画面座標の近くへ、コピー/検索ボタンを、フォーカスを奪わずに表示する。</summary>
        public void ShowNear(int screenX, int screenY, string text)
        {
            _text = text;
            _helper.ShowAt(screenX, screenY);
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

        // 何もせずポップアップを閉じるだけのボタン
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            HideNoActivate();
        }
    }
}
