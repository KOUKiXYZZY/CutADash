using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using Windows.System;

namespace UiLibrary
{
    /// <summary>
    /// 複数の文字列を、チップ(タグ)の並びとして入力する部品。下の入力欄に文字を入れて
    /// Enter(または区切り文字)で追加し、チップの×で削除する。入力欄が空の時にBackspaceを押すと、
    /// 最後のチップを消す。区切り文字を含む文字列を貼り付けると、まとめて追加される。
    /// </summary>
    public sealed partial class ChipInput : UserControl
    {
        public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
            nameof(Header), typeof(string), typeof(ChipInput),
            new PropertyMetadata(string.Empty, (d, _) => ((ChipInput)d).UpdateHeader()));

        public static readonly DependencyProperty PlaceholderTextProperty = DependencyProperty.Register(
            nameof(PlaceholderText), typeof(string), typeof(ChipInput),
            new PropertyMetadata(string.Empty, (d, e) => ((ChipInput)d).InputBox.PlaceholderText = (string)e.NewValue));

        /// <summary>追加済みの項目。この一覧を直接操作しても、チップの表示に反映される。</summary>
        public ObservableCollection<string> Items { get; } = new();

        /// <summary>
        /// 同じ文字列を重ねて追加できるか。既定はfalse(大文字と小文字は区別しない)。
        /// </summary>
        public bool AllowDuplicates { get; set; }

        /// <summary>
        /// 追加してよい文字列かを判定する関数。falseを返した文字列は追加されず、
        /// <see cref="ItemRejected"/>が発生する。未設定の時は、空でなければ全て追加できる。
        /// </summary>
        public Func<string, bool>? Validator { get; set; }

        /// <summary>入力を区切る文字(貼り付けた文字列の分割にも使う)。Enterは常に区切りになる。</summary>
        public char[] Separators { get; set; } = { ',', ';', '\n', '\r', '\t' };

        /// <summary>入力欄の上に出す見出し。空なら出さない。</summary>
        public string Header
        {
            get => (string)GetValue(HeaderProperty);
            set => SetValue(HeaderProperty, value);
        }

        /// <summary>入力欄が空の時に出す薄い文字。</summary>
        public string PlaceholderText
        {
            get => (string)GetValue(PlaceholderTextProperty);
            set => SetValue(PlaceholderTextProperty, value);
        }

        /// <summary>入力から項目が追加された時に発生する。</summary>
        public event EventHandler<string>? ItemAdded;

        /// <summary>項目が削除された時に発生する。</summary>
        public event EventHandler<string>? ItemRemoved;

        /// <summary>重複や、<see cref="Validator"/>に断られて、追加されなかった時に発生する。</summary>
        public event EventHandler<string>? ItemRejected;

        public ChipInput()
        {
            InitializeComponent();
            ChipsControl.ItemsSource = Items;
            UpdateHeader();
            UpdateChipsVisibility();

            Items.CollectionChanged += (_, _) => UpdateChipsVisibility();
        }

        private void UpdateHeader()
        {
            HeaderTextBlock.Text = Header ?? string.Empty;
            HeaderTextBlock.Visibility = string.IsNullOrEmpty(Header) ? Visibility.Collapsed : Visibility.Visible;
        }

        // チップが1つも無い間は、一覧の分の余白を詰める
        private void UpdateChipsVisibility()
            => ChipsControl.Visibility = Items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>
        /// 項目を追加する。空・重複・<see cref="Validator"/>に断られた場合は追加せずfalseを返す。
        /// </summary>
        public bool TryAdd(string value)
        {
            var text = (value ?? string.Empty).Trim();
            if (text.Length == 0)
                return false;

            var isDuplicate = !AllowDuplicates
                && Items.Any(x => string.Equals(x, text, StringComparison.OrdinalIgnoreCase));

            if (isDuplicate || (Validator is { } validate && !validate(text)))
            {
                ItemRejected?.Invoke(this, text);
                return false;
            }

            Items.Add(text);
            ItemAdded?.Invoke(this, text);
            return true;
        }

        /// <summary>項目を削除する。無ければfalseを返す。</summary>
        public bool Remove(string value)
        {
            if (!Items.Remove(value))
                return false;

            ItemRemoved?.Invoke(this, value);
            return true;
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            if (((Button)sender).Tag is string value)
                Remove(value);
        }

        private void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case VirtualKey.Enter:
                    AddFromInput(InputBox.Text);
                    e.Handled = true;
                    break;

                case VirtualKey.Back when InputBox.Text.Length == 0 && Items.Count > 0:
                    Remove(Items[^1]);
                    e.Handled = true;
                    break;
            }
        }

        // 区切り文字が入力(貼り付けを含む)された時は、その手前までを項目として追加する
        private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var text = InputBox.Text;
            if (text.IndexOfAny(Separators) < 0)
                return;

            var parts = text.Split(Separators);

            // 最後の区切りより後ろは、まだ入力途中なので入力欄に残す(Enterまたは次の区切りで追加)
            var rest = parts[^1];
            foreach (var part in parts[..^1])
                TryAdd(part);

            InputBox.Text = rest;
            InputBox.SelectionStart = rest.Length;
        }

        private void AddFromInput(string text)
        {
            // 追加に成功した時だけ入力欄を空にする(断られた時は、直して再入力できるよう残す)
            if (TryAdd(text) || string.IsNullOrWhiteSpace(text))
                InputBox.Text = string.Empty;
        }
    }
}
