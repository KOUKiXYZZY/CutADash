using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.Foundation;
using Windows.Graphics;

namespace UiLibrary
{
    /// <summary>
    /// システムのタイトルバーを消したウィンドウ用の、細いタイトルバー。
    /// タイトル文字(アクセントカラー)・閉じるボタン・下端の区切り線を持ち、タイトル文字の領域を掴んで
    /// ウィンドウを移動できる。<see cref="AttachTo"/>を呼ぶと、システムのタイトルバーを消し、
    /// ドラッグ領域の登録と、サイズ変更時の更新まで行う。
    /// </summary>
    public sealed partial class SlimTitleBar : UserControl
    {
        public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
            nameof(Title), typeof(string), typeof(SlimTitleBar),
            new PropertyMetadata(string.Empty, (d, e) => ((SlimTitleBar)d).TitleTextBlock.Text = (string?)e.NewValue ?? string.Empty));

        public static readonly DependencyProperty TitleForegroundProperty = DependencyProperty.Register(
            nameof(TitleForeground), typeof(Brush), typeof(SlimTitleBar),
            new PropertyMetadata(null, (d, e) =>
            {
                var text = ((SlimTitleBar)d).TitleTextBlock;
                if (e.NewValue is Brush brush) text.Foreground = brush; else text.ClearValue(TextBlock.ForegroundProperty);
            }));

        public static readonly DependencyProperty DividerBrushProperty = DependencyProperty.Register(
            nameof(DividerBrush), typeof(Brush), typeof(SlimTitleBar),
            new PropertyMetadata(null, (d, e) =>
            {
                var border = ((SlimTitleBar)d).DividerBorder;
                if (e.NewValue is Brush brush) border.Background = brush; else border.ClearValue(Border.BackgroundProperty);
            }));

        public static readonly DependencyProperty ShowCloseButtonProperty = DependencyProperty.Register(
            nameof(ShowCloseButton), typeof(bool), typeof(SlimTitleBar),
            new PropertyMetadata(true, (d, e) =>
                ((SlimTitleBar)d).CloseButton.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed));

        public static readonly DependencyProperty ShowDividerProperty = DependencyProperty.Register(
            nameof(ShowDivider), typeof(bool), typeof(SlimTitleBar),
            new PropertyMetadata(true, (d, e) =>
                ((SlimTitleBar)d).DividerBorder.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed));

        public static readonly DependencyProperty LeftContentProperty = DependencyProperty.Register(
            nameof(LeftContent), typeof(object), typeof(SlimTitleBar),
            new PropertyMetadata(null, (d, e) => ((SlimTitleBar)d).LeftPresenter.Content = e.NewValue));

        public static readonly DependencyProperty RightContentProperty = DependencyProperty.Register(
            nameof(RightContent), typeof(object), typeof(SlimTitleBar),
            new PropertyMetadata(null, (d, e) => ((SlimTitleBar)d).RightPresenter.Content = e.NewValue));

        private Window? _window;

        public SlimTitleBar()
        {
            InitializeComponent();
            DragArea.Loaded += (_, _) => UpdateDragRegion();
            DragArea.SizeChanged += (_, _) => UpdateDragRegion();
        }

        /// <summary>閉じるボタンが押された。購読していなければ、アタッチしたウィンドウを閉じる。</summary>
        public event TypedEventHandler<SlimTitleBar, EventArgs>? CloseRequested;

        /// <summary>中央に表示するタイトル文字。</summary>
        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        /// <summary>タイトル文字の色。未指定ならアクセントカラー。</summary>
        public Brush? TitleForeground
        {
            get => (Brush?)GetValue(TitleForegroundProperty);
            set => SetValue(TitleForegroundProperty, value);
        }

        /// <summary>下端の区切り線の色。未指定ならテーマの区切り線の色。</summary>
        public Brush? DividerBrush
        {
            get => (Brush?)GetValue(DividerBrushProperty);
            set => SetValue(DividerBrushProperty, value);
        }

        public bool ShowCloseButton
        {
            get => (bool)GetValue(ShowCloseButtonProperty);
            set => SetValue(ShowCloseButtonProperty, value);
        }

        public bool ShowDivider
        {
            get => (bool)GetValue(ShowDividerProperty);
            set => SetValue(ShowDividerProperty, value);
        }

        /// <summary>タイトル文字の左に置く要素(タブなど)。この領域はドラッグでの移動の対象外になる。</summary>
        public object? LeftContent
        {
            get => GetValue(LeftContentProperty);
            set => SetValue(LeftContentProperty, value);
        }

        /// <summary>閉じるボタンの左に置く要素(ボタンなど)。この領域はドラッグでの移動の対象外になる。</summary>
        public object? RightContent
        {
            get => GetValue(RightContentProperty);
            set => SetValue(RightContentProperty, value);
        }

        /// <summary>
        /// このタイトルバーをウィンドウに結び付ける。システムのタイトルバーを消し、タイトル文字の領域を
        /// ドラッグ領域として登録し、ウィンドウのサイズ変更に追従させる。ウィンドウのコンストラクタで、
        /// InitializeComponentのあとに1度だけ呼ぶ。
        /// </summary>
        public void AttachTo(Window window)
        {
            if (_window is not null)
                return;
            _window = window;

            var appWindow = window.AppWindow;
            if (AppWindowTitleBar.IsCustomizationSupported())
            {
                appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
                appWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;
            }

            appWindow.Changed += OnAppWindowChanged;
            window.Closed += (_, _) => appWindow.Changed -= OnAppWindowChanged;
            UpdateDragRegion();
        }

        /// <summary>
        /// ドラッグ領域を計算し直す。サイズ変更では自動で呼ばれるが、タイトルバーの配置(行・列)を
        /// コードで切り替えたときは、呼び直す。
        /// </summary>
        public void UpdateDragRegion()
        {
            if (_window?.Content is not UIElement root || DragArea.XamlRoot is null)
                return;
            if (DragArea.ActualWidth <= 0 || DragArea.ActualHeight <= 0)
                return;

            var scale = DragArea.XamlRoot.RasterizationScale;
            var origin = DragArea.TransformToVisual(root).TransformPoint(new Point(0, 0));

            var rect = new RectInt32(
                (int)(origin.X * scale),
                (int)(origin.Y * scale),
                (int)(DragArea.ActualWidth * scale),
                (int)(DragArea.ActualHeight * scale));

            InputNonClientPointerSource.GetForWindowId(_window.AppWindow.Id)
                .SetRegionRects(NonClientRegionKind.Caption, new[] { rect });
        }

        private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
        {
            if (args.DidSizeChange)
                UpdateDragRegion();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (CloseRequested is null)
                _window?.Close();
            else
                CloseRequested.Invoke(this, EventArgs.Empty);
        }
    }
}
