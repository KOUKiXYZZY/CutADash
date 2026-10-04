using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Graphics;
using WinRT.Interop;

namespace UiLibrary
{
    /// <summary>MessageDialogの実体となる、専用の小さなウィンドウ。</summary>
    internal sealed partial class MessageDialogWindow : Window
    {
        // 本文の折り返しの基準になる、ウィンドウ内側の幅(DIP)
        private const double ClientWidthDip = 440;

        private readonly TaskCompletionSource<MessageDialogResult> _completion = new();
        private readonly IReadOnlyList<MessageDialogResult> _buttons;
        private readonly MessageDialogIcon _icon;
        private readonly MessageDialogDefaultButton _defaultButton;
        private readonly IntPtr _hWnd;

        // ×ボタン/Escで閉じた時の結果。MessageBoxと同じく、キャンセルのボタンがあればそれ、
        // OKだけならOK。それ以外(はい/いいえ等)は、閉じられないようにする
        private readonly MessageDialogResult? _closeResult;

        // モーダルにするために、一時的に無効にした他のウィンドウ
        private readonly List<IntPtr> _disabledWindows = new();

        private readonly List<Button> _buttonControls = new();

        private bool _isCompleted;

        public Task<MessageDialogResult> Result => _completion.Task;

        public MessageDialogWindow(
            string text, string caption, MessageDialogButtons buttons, MessageDialogIcon icon,
            MessageDialogDefaultButton defaultButton)
        {
            InitializeComponent();

            _hWnd = WindowNative.GetWindowHandle(this);
            _buttons = GetButtons(buttons);
            _icon = icon;
            _defaultButton = defaultButton;
            _closeResult = GetCloseResult(_buttons);

            Title = caption;
            MessageText.Text = text;

            ConfigureIcon();
            CreateButtons();

            RootGrid.RequestedTheme = MessageDialog.Theme;
            RootGrid.KeyDown += OnKeyDown;

            // 最小化・最大化・リサイズができない、ダイアログ用の枠にする
            var presenter = OverlappedPresenter.CreateForDialog();
            presenter.IsAlwaysOnTop = true;
            AppWindow.SetPresenter(presenter);

            AppWindow.Closing += OnClosing;
            Closed += (_, _) => RestoreOtherWindows();
        }

        /// <summary>
        /// 位置・大きさを決めて表示する。マウスカーソルのあるモニタの中央に出し、
        /// 同じスレッドの他のウィンドウを無効にして、モーダルにする。
        /// </summary>
        public void Present()
        {
            // 表示先モニタのDPIは、実際にそこへ置いてからでないと取れないため、
            // 先にそのモニタの作業領域の左上へ移してから、サイズを確定する
            GetCursorPos(out var cursor);
            var area = DisplayArea.GetFromPoint(new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Primary);
            var work = area.WorkArea;
            AppWindow.Move(new PointInt32(work.X, work.Y));

            var scale = GetDpiForWindow(_hWnd) / 96.0;

            RootGrid.Measure(new Windows.Foundation.Size(ClientWidthDip, double.PositiveInfinity));
            var clientWidth = (int)Math.Ceiling(ClientWidthDip * scale);
            var clientHeight = (int)Math.Ceiling(RootGrid.DesiredSize.Height * scale);
            AppWindow.ResizeClient(new SizeInt32(clientWidth, clientHeight));

            var size = AppWindow.Size;
            AppWindow.Move(new PointInt32(
                work.X + (work.Width - size.Width) / 2,
                work.Y + (work.Height - size.Height) / 2));

            PlayBeep();
            DisableOtherWindows();

            Activate();
            SetForegroundWindow(_hWnd);
            FocusDefaultButton();
        }

        private static IReadOnlyList<MessageDialogResult> GetButtons(MessageDialogButtons buttons) => buttons switch
        {
            MessageDialogButtons.OkCancel => new[] { MessageDialogResult.Ok, MessageDialogResult.Cancel },
            MessageDialogButtons.AbortRetryIgnore => new[] { MessageDialogResult.Abort, MessageDialogResult.Retry, MessageDialogResult.Ignore },
            MessageDialogButtons.YesNoCancel => new[] { MessageDialogResult.Yes, MessageDialogResult.No, MessageDialogResult.Cancel },
            MessageDialogButtons.YesNo => new[] { MessageDialogResult.Yes, MessageDialogResult.No },
            MessageDialogButtons.RetryCancel => new[] { MessageDialogResult.Retry, MessageDialogResult.Cancel },
            MessageDialogButtons.CancelTryContinue => new[] { MessageDialogResult.Cancel, MessageDialogResult.TryAgain, MessageDialogResult.Continue },
            _ => new[] { MessageDialogResult.Ok },
        };

        private static MessageDialogResult? GetCloseResult(IReadOnlyList<MessageDialogResult> buttons)
        {
            if (buttons.Contains(MessageDialogResult.Cancel))
                return MessageDialogResult.Cancel;

            if (buttons.Count == 1 && buttons[0] == MessageDialogResult.Ok)
                return MessageDialogResult.Ok;

            return null;
        }

        private void ConfigureIcon()
        {
            // Segoe Fluent Icons / Segoe MDL2 Assetsのグリフ。テーマで色が切り替わる
            // システムのブラシはコードから直接引けないため、明暗どちらでも見える固定色にしている
            var (glyph, color) = _icon switch
            {
                MessageDialogIcon.Information => ("", Windows.UI.Color.FromArgb(255, 0x0F, 0x6C, 0xBD)),
                MessageDialogIcon.Warning => ("", Windows.UI.Color.FromArgb(255, 0xD9, 0x82, 0x00)),
                MessageDialogIcon.Error => ("", Windows.UI.Color.FromArgb(255, 0xC4, 0x2B, 0x1C)),
                MessageDialogIcon.Question => ("", Windows.UI.Color.FromArgb(255, 0x0F, 0x6C, 0xBD)),
                _ => (string.Empty, default(Windows.UI.Color)),
            };

            if (glyph.Length == 0)
                return;

            IconGlyph.Glyph = glyph;
            IconGlyph.Foreground = new SolidColorBrush(color);
            IconGlyph.Visibility = Visibility.Visible;
        }

        private void CreateButtons()
        {
            var defaultIndex = Math.Min((int)_defaultButton, _buttons.Count - 1);

            for (var i = 0; i < _buttons.Count; i++)
            {
                var result = _buttons[i];
                var button = new Button
                {
                    Content = MessageDialog.GetLabel(result),
                    MinWidth = 88,
                };

                if (i == defaultIndex)
                    button.Style = (Style)Application.Current.Resources["AccentButtonStyle"];

                button.Click += (_, _) => Complete(result);

                _buttonControls.Add(button);
                ButtonsPanel.Children.Add(button);
            }
        }

        private void FocusDefaultButton()
        {
            var defaultIndex = Math.Min((int)_defaultButton, _buttonControls.Count - 1);
            _buttonControls[defaultIndex].Focus(FocusState.Programmatic);
        }

        private void OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape && _closeResult is { } result)
            {
                e.Handled = true;
                Complete(result);
            }
        }

        // ×ボタン/Alt+F4。閉じてよい結果がある時だけ閉じる(MessageBoxと同じ)
        private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            if (_isCompleted)
                return;

            if (_closeResult is { } result)
            {
                Complete(result);
                return;
            }

            args.Cancel = true;
        }

        private void Complete(MessageDialogResult result)
        {
            if (_isCompleted)
                return;

            _isCompleted = true;

            // 閉じる前に他のウィンドウを有効に戻す。閉じた後だと、有効化の前に別のアプリへ
            // 前面が移ってしまうことがある
            RestoreOtherWindows();

            _completion.TrySetResult(result);
            Close();
        }

        // モーダルにするため、同じスレッドの表示中の他のウィンドウを、閉じるまで無効にする
        // (MessageBoxのMB_TASKMODALと同じ動き)
        private void DisableOtherWindows()
        {
            EnumThreadWindows(GetCurrentThreadId(), (hWnd, _) =>
            {
                if (hWnd != _hWnd && IsWindowVisible(hWnd) && IsWindowEnabled(hWnd))
                {
                    EnableWindow(hWnd, false);
                    _disabledWindows.Add(hWnd);
                }

                return true;
            }, IntPtr.Zero);
        }

        private void RestoreOtherWindows()
        {
            foreach (var hWnd in _disabledWindows)
                EnableWindow(hWnd, true);

            _disabledWindows.Clear();
        }

        private void PlayBeep()
        {
            if (_icon == MessageDialogIcon.None)
                return;

            var type = _icon switch
            {
                MessageDialogIcon.Information => 0x00000040u,
                MessageDialogIcon.Warning => 0x00000030u,
                MessageDialogIcon.Error => 0x00000010u,
                _ => 0x00000020u,
            };

            MessageBeep(type);
        }

        private delegate bool EnumThreadWndProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool EnumThreadWindows(uint threadId, EnumThreadWndProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowEnabled(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool EnableWindow(IntPtr hWnd, bool enable);

        [DllImport("user32.dll")]
        private static extern bool MessageBeep(uint type);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
    }
}
