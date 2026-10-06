using CutADash.Models;
using Common.Extension;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using Windows.Graphics;
using WinAPI;
using WinRT.Interop;
using WinUIEx;
using static WinAPI.WinUser;
using ShowWindowCommands = WinAPI.ShowWindowCommands;

namespace CutADash.Views.Contents
{
    /// <summary>
    /// ウィンドウを縮めたコンパクト表示(一覧だけで、Contentsを置く場所が無い状態)で、
    /// マウスオーバーした項目のContentsを、メインウィンドウの横に出す小さなポップアップ。
    ///
    /// WS_EX_NOACTIVATE(MainWindowと同じ仕組み)を付けているため、出てもフォーカスや
    /// 貼り付け先(直前のフォアグラウンドウィンドウ)は変わらない。
    /// </summary>
    public sealed class ContentsPopupWindow : WindowEx
    {
        // ポップアップの大きさ(DIP)と、メインウィンドウとの間隔
        private const int WidthDip = 420;
        private const int HeightDip = 360;
        private const int GapDip = 4;

        private readonly MainWindow _owner;
        private readonly IntPtr _hWnd;
        private readonly Frame _frame = new();
        private readonly Grid _root = new();

        public ContentsPopupWindow(MainWindow owner)
        {
            _owner = owner;
            _hWnd = WindowNative.GetWindowHandle(this);

            var exStyle = GetWindowLong(_hWnd, GWL_EXSTYLE);
            SetWindowLong(_hWnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);

            // タイトルバー・枠を取り除く(SelectionToolbarと同じ)
            var style = GetWindowLong(_hWnd, GWL_STYLE);
            style &= ~(int)(WindowStyles.WS_CAPTION | WindowStyles.WS_THICKFRAME
                | WindowStyles.WS_BORDER | WindowStyles.WS_DLGFRAME);
            SetWindowLong(_hWnd, GWL_STYLE, style);

            this.RemoveTitleBar();
            this.DwmTransitions(true);
            this.SetWindowCornerPreference(DwmAPI.DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND);

            Title = "Contents";
            _root.Children.Add(_frame);
            Content = _root;

            // メインウィンドウと同じテーマ(明/暗)・背景素材にする
            this.ApplyColorTheme(Preferences.PreferencesGateway.GetColorTheme());
            Preferences.PreferencesGateway.ColorThemeChanged += theme =>
                DispatcherQueue.TryEnqueue(() => this.ApplyColorTheme(theme));

            ApplyBackdropWithTint(Preferences.PreferencesGateway.GetWindowBackdrop());
            Preferences.PreferencesGateway.WindowBackdropChanged += kind =>
                DispatcherQueue.TryEnqueue(() => ApplyBackdropWithTint(kind));
            _root.ActualThemeChanged += (_, _) =>
                ApplyBackdropWithTint(Preferences.PreferencesGateway.GetWindowBackdrop());

            _frame.Navigate(typeof(Contents), new ContentsNavigationParameter { MainWindow = owner },
                new SuppressNavigationTransitionInfo());

            // ポップアップは内容の表示部分だけにする(操作用のボタン列は出さない)
            if (_frame.Content is Contents contents)
                contents.IsPreviewOnly = true;
        }

        // 背景素材を適用し、ルートに半透明のティントを重ねる。Blurは生のぼかしだけで色味が無く、
        // 背後の色によって文字が読みにくくなるため(MainWindowと同じ理由)、MainWindowと
        // 同じ濃さ(アルファ80=約31%)にする
        private void ApplyBackdropWithTint(Common.Models.WindowBackdropKind kind)
        {
            this.ApplyBackdrop(kind, alwaysActive: true);

            var isDark = _root.ActualTheme == ElementTheme.Dark;
            _root.Background = kind switch
            {
                Common.Models.WindowBackdropKind.Blur => isDark
                    ? new SolidColorBrush(Windows.UI.Color.FromArgb(80, 32, 32, 32))
                    : new SolidColorBrush(Windows.UI.Color.FromArgb(80, 243, 243, 243)),

                _ when Theming.CatTheme.IsActive(kind) => Theming.CatTheme.GetBackdropTint(kind),

                _ => null
            };
        }

        /// <summary>指定した項目のContentsを、メインウィンドウの横に表示する(フォーカスは奪わない)。</summary>
        public void ShowFor(ClipboardItem item)
        {
            if (_frame.Content is Contents contents)
                contents.ForceSetContext(item, null);

            Reposition();

            ShowWindow(_hWnd, ShowWindowCommands.SW_SHOWNOACTIVATE);
            SetWindowPos(_hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        public void HidePopup()
        {
            // AppWindow経由のHideではなく、Win32で直接隠す。HidePaletteは別アプリのアクティブ化
            // (WM_ACTIVATEAPP)の最中にも呼ばれるため、ここで例外を出してアプリを落とさない
            try
            {
                ShowWindow(_hWnd, ShowWindowCommands.SW_HIDE);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ContentsPopupWindow] HidePopupで例外を無視しました: {ex.Message}");
            }
        }

        // メインウィンドウの右隣に、上端をそろえて置く。画面の右端に収まらなければ左隣にする
        private void Reposition()
        {
            var scale = Dpi.GetDpiForWindow(_owner.GetWindowHandle()) / 96.0;
            var width = (int)(WidthDip * scale);
            var height = (int)(HeightDip * scale);
            var gap = (int)(GapDip * scale);

            var ownerPos = _owner.AppWindow.Position;
            var ownerSize = _owner.AppWindow.Size;
            var work = DisplayArea.GetFromWindowId(_owner.AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;

            var x = ownerPos.X + ownerSize.Width + gap;
            if (x + width > work.X + work.Width)
                x = ownerPos.X - width - gap;
            x = Math.Max(work.X, Math.Min(x, work.X + work.Width - width));

            var y = Math.Max(work.Y, Math.Min(ownerPos.Y, work.Y + work.Height - height));

            AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
        }
    }
}
