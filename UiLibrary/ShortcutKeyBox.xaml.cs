using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using Windows.System;
using Windows.UI.Core;

namespace UiLibrary
{
    /// <summary>
    /// ショートカットキーを入力する部品。押すとキー入力待ちになり、修飾キー(Ctrl/Shift/Alt/Win)を
    /// 押しながら別のキーを押すと、その組み合わせが<see cref="Shortcut"/>になる。
    /// Escで入力待ちをやめ、フォーカスが外れた時も入力待ちをやめる。
    /// </summary>
    public partial class ShortcutKeyBox : UserControl
    {
        public static readonly DependencyProperty ShortcutProperty = DependencyProperty.Register(
            nameof(Shortcut), typeof(ShortcutKey), typeof(ShortcutKeyBox),
            new PropertyMetadata(null, OnShortcutChanged));

        public static readonly DependencyProperty PlaceholderTextProperty = DependencyProperty.Register(
            nameof(PlaceholderText), typeof(string), typeof(ShortcutKeyBox),
            new PropertyMetadata("Not set", (d, _) => ((ShortcutKeyBox)d).UpdateText()));

        public static readonly DependencyProperty CapturingTextProperty = DependencyProperty.Register(
            nameof(CapturingText), typeof(string), typeof(ShortcutKeyBox),
            new PropertyMetadata("Press a shortcut...", (d, _) => ((ShortcutKeyBox)d).UpdateText()));

        public static readonly DependencyProperty RequireModifierProperty = DependencyProperty.Register(
            nameof(RequireModifier), typeof(bool), typeof(ShortcutKeyBox),
            new PropertyMetadata(true));

        public static readonly DependencyProperty IsClearableProperty = DependencyProperty.Register(
            nameof(IsClearable), typeof(bool), typeof(ShortcutKeyBox),
            new PropertyMetadata(true, (d, _) => ((ShortcutKeyBox)d).UpdateText()));

        public static readonly DependencyProperty ShowWindowsKeyOptionProperty = DependencyProperty.Register(
            nameof(ShowWindowsKeyOption), typeof(bool), typeof(ShortcutKeyBox),
            new PropertyMetadata(false, (d, _) => ((ShortcutKeyBox)d).UpdateText()));

        public static readonly DependencyProperty WindowsKeyTextProperty = DependencyProperty.Register(
            nameof(WindowsKeyText), typeof(string), typeof(ShortcutKeyBox),
            new PropertyMetadata("Win", (d, _) => ((ShortcutKeyBox)d).UpdateText()));

        private bool _isCapturing;

        // ショートカットが未設定の間に、Winのチェックだけ先に付けられている状態
        private bool _pendingWindows;

        /// <summary>設定されているショートカット。未設定ならnull。</summary>
        public ShortcutKey? Shortcut
        {
            get => (ShortcutKey?)GetValue(ShortcutProperty);
            set => SetValue(ShortcutProperty, value);
        }

        /// <summary>未設定の時に出す文字列。</summary>
        public string PlaceholderText
        {
            get => (string)GetValue(PlaceholderTextProperty);
            set => SetValue(PlaceholderTextProperty, value);
        }

        /// <summary>キー入力を待っている間に出す文字列。</summary>
        public string CapturingText
        {
            get => (string)GetValue(CapturingTextProperty);
            set => SetValue(CapturingTextProperty, value);
        }

        /// <summary>
        /// trueの時は、修飾キーを含まない組み合わせ(単独の"A"など)を受け付けない
        /// (他の操作と衝突しやすいため)。既定はtrue。
        /// </summary>
        public bool RequireModifier
        {
            get => (bool)GetValue(RequireModifierProperty);
            set => SetValue(RequireModifierProperty, value);
        }

        /// <summary>trueの時は、値がある間、右に解除ボタン(×)を出す。既定はtrue。</summary>
        public bool IsClearable
        {
            get => (bool)GetValue(IsClearableProperty);
            set => SetValue(IsClearableProperty, value);
        }

        /// <summary>
        /// trueの時は、入力欄の右に「Win」のチェックボックスを出す。Winキーは、押して入力するのではなく
        /// このチェックで指定する(Windowsが先に処理してしまい、アプリに届かない組み合わせが多いため)。
        /// 既定はfalse。
        /// </summary>
        public bool ShowWindowsKeyOption
        {
            get => (bool)GetValue(ShowWindowsKeyOptionProperty);
            set => SetValue(ShowWindowsKeyOptionProperty, value);
        }

        /// <summary>Winのチェックボックスの文字列。</summary>
        public string WindowsKeyText
        {
            get => (string)GetValue(WindowsKeyTextProperty);
            set => SetValue(WindowsKeyTextProperty, value);
        }

        /// <summary>ユーザーが入力・解除した時、または<see cref="Shortcut"/>が変わった時に発生する。</summary>
        public event EventHandler<ShortcutKey?>? ShortcutChanged;

        public ShortcutKeyBox()
        {
            InitializeComponent();
            UpdateText();
        }

        private static void OnShortcutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var box = (ShortcutKeyBox)d;
            box.UpdateText();
            box.ShortcutChanged?.Invoke(box, box.Shortcut);
        }

        private void UpdateText()
        {
            if (_isCapturing)
            {
                ValueTextBlock.Text = CapturingText;
                ValueTextBlock.Opacity = 0.6;
            }
            else if (Shortcut is { } shortcut)
            {
                ValueTextBlock.Text = shortcut.ToString();
                ValueTextBlock.Opacity = 1.0;
            }
            else
            {
                ValueTextBlock.Text = PlaceholderText;
                ValueTextBlock.Opacity = 0.6;
            }

            WinCheckBox.Content = WindowsKeyText;
            WinCheckBox.Visibility = ShowWindowsKeyOption ? Visibility.Visible : Visibility.Collapsed;
            WinCheckBox.IsChecked = Shortcut is { } current
                ? current.Modifiers.HasFlag(ShortcutModifiers.Windows)
                : _pendingWindows;

            ClearButton.Visibility = IsClearable && Shortcut is not null && !_isCapturing
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void Box_Click(object sender, RoutedEventArgs e)
        {
            if (_isCapturing)
                return;

            _isCapturing = true;
            UpdateText();
            Box.Focus(FocusState.Programmatic);
        }

        private void Box_LostFocus(object sender, RoutedEventArgs e) => EndCapture();

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            _pendingWindows = false;
            Shortcut = null;
        }

        // Winのチェックを切り替えた時。ショートカットが設定済みなら、Winの有無だけを入れ替える
        private void WinCheckBox_Click(object sender, RoutedEventArgs e)
        {
            var isChecked = WinCheckBox.IsChecked == true;

            if (Shortcut is { } current)
            {
                var modifiers = isChecked
                    ? current.Modifiers | ShortcutModifiers.Windows
                    : current.Modifiers & ~ShortcutModifiers.Windows;

                Shortcut = new ShortcutKey(modifiers, current.VirtualKey);
                return;
            }

            _pendingWindows = isChecked;
        }

        private void EndCapture()
        {
            if (!_isCapturing)
                return;

            _isCapturing = false;
            UpdateText();
        }

        // 入力待ちの間は、押したキーをこの部品が全て受け取る(ボタンのEnter/Spaceによるクリックや、
        // Tabによるフォーカス移動などが起きないようにする)
        private void Box_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (!_isCapturing)
                return;

            e.Handled = true;

            switch (e.Key)
            {
                case VirtualKey.Escape:
                    EndCapture();
                    return;

                case VirtualKey.Control:
                case VirtualKey.LeftControl:
                case VirtualKey.RightControl:
                case VirtualKey.Shift:
                case VirtualKey.LeftShift:
                case VirtualKey.RightShift:
                case VirtualKey.Menu:
                case VirtualKey.LeftMenu:
                case VirtualKey.RightMenu:
                case VirtualKey.LeftWindows:
                case VirtualKey.RightWindows:
                    // 修飾キーだけが押されている間は、途中経過を見せる
                    var held = GetHeldModifiers();
                    ValueTextBlock.Text = held == ShortcutModifiers.None
                        ? CapturingText
                        : ShortcutKey.FormatModifiers(held) + " + ...";
                    return;
            }

            var modifiers = GetHeldModifiers();

            // Winのチェックが付いていれば、押したキーにWinを加える
            if (ShowWindowsKeyOption && WinCheckBox.IsChecked == true)
                modifiers |= ShortcutModifiers.Windows;

            if (RequireModifier && modifiers == ShortcutModifiers.None)
                return;

            _pendingWindows = false;
            Shortcut = new ShortcutKey(modifiers, (uint)e.Key);
            EndCapture();
        }

        // Enter/Spaceはキーを離した時にボタンのクリックになるため、入力待ちの間は離した時も受け取る
        private void Box_PreviewKeyUp(object sender, KeyRoutedEventArgs e)
        {
            if (_isCapturing)
                e.Handled = true;
        }

        // 「今押されている修飾キー」は、キーを押した時点の状態から求める。押したキーの履歴を
        // 積み上げる方式だと、CtrlをいったんCtrlを離したあとの組み合わせにCtrlが残ってしまう
        private static ShortcutModifiers GetHeldModifiers()
        {
            var modifiers = ShortcutModifiers.None;

            if (IsDown(VirtualKey.Control))
                modifiers |= ShortcutModifiers.Control;
            if (IsDown(VirtualKey.Shift))
                modifiers |= ShortcutModifiers.Shift;
            if (IsDown(VirtualKey.Menu))
                modifiers |= ShortcutModifiers.Alt;
            if (IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows))
                modifiers |= ShortcutModifiers.Windows;

            return modifiers;
        }

        private static bool IsDown(VirtualKey key)
            => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
    }
}
