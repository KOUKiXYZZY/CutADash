using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Globalization;

namespace UiLibrary
{
    /// <summary>
    /// 年月を選ぶピッカー。CalendarDatePickerの年月版にあたる。
    /// 選ばれている年月(未選択ならプレースホルダ)を枠付きの入力欄で表示し、押すと
    /// <see cref="YearMonthCalendar"/>が下に開く。月を選ぶと閉じる。
    /// </summary>
    public sealed partial class YearMonthPicker : UserControl
    {
        public static readonly DependencyProperty SelectedDateProperty = DependencyProperty.Register(
            nameof(SelectedDate), typeof(DateTime?), typeof(YearMonthPicker),
            new PropertyMetadata(null, OnSelectedDateChanged));

        public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
            nameof(Header), typeof(string), typeof(YearMonthPicker),
            new PropertyMetadata(string.Empty, (d, _) => ((YearMonthPicker)d).UpdateHeader()));

        public static readonly DependencyProperty PlaceholderTextProperty = DependencyProperty.Register(
            nameof(PlaceholderText), typeof(string), typeof(YearMonthPicker),
            new PropertyMetadata("Select year/month", (d, _) => ((YearMonthPicker)d).UpdateText()));

        public static readonly DependencyProperty DisplayFormatProperty = DependencyProperty.Register(
            nameof(DisplayFormat), typeof(string), typeof(YearMonthPicker),
            new PropertyMetadata("yyyy/MM", (d, _) => ((YearMonthPicker)d).UpdateText()));

        private CultureInfo? _culture;
        private bool _isSyncing;

        /// <summary>選ばれている年月の1日。未選択ならnull。1日以外を渡しても1日に丸める。</summary>
        public DateTime? SelectedDate
        {
            get => (DateTime?)GetValue(SelectedDateProperty);
            set => SetValue(SelectedDateProperty, value);
        }

        /// <summary>入力欄の上に出す見出し(CalendarDatePicker.Headerと同じ)。空なら出さない。</summary>
        public string Header
        {
            get => (string)GetValue(HeaderProperty);
            set => SetValue(HeaderProperty, value);
        }

        /// <summary>未選択の時に入力欄へ出す文字列。</summary>
        public string PlaceholderText
        {
            get => (string)GetValue(PlaceholderTextProperty);
            set => SetValue(PlaceholderTextProperty, value);
        }

        /// <summary>入力欄に出す選択結果の書式(DateTimeの書式文字列)。例: "yyyy/MM"、"yyyy年M月"。</summary>
        public string DisplayFormat
        {
            get => (string)GetValue(DisplayFormatProperty);
            set => SetValue(DisplayFormatProperty, value);
        }

        /// <summary>選べる最小の日付。</summary>
        public DateTime MinDate
        {
            get => Calendar.MinDate;
            set => Calendar.MinDate = value;
        }

        /// <summary>選べる最大の日付。</summary>
        public DateTime MaxDate
        {
            get => Calendar.MaxDate;
            set => Calendar.MaxDate = value;
        }

        /// <summary>フライアウト内の年の表示書式。</summary>
        public string YearFormat
        {
            get => Calendar.YearFormat;
            set => Calendar.YearFormat = value;
        }

        /// <summary>フライアウト内の月の表示書式。</summary>
        public string MonthFormat
        {
            get => Calendar.MonthFormat;
            set => Calendar.MonthFormat = value;
        }

        /// <summary>表示に使うカルチャ。未設定の時はOSの現在のカルチャを使う。</summary>
        public CultureInfo? Culture
        {
            get => _culture;
            set
            {
                _culture = value;
                Calendar.Culture = value;
                UpdateText();
            }
        }

        /// <summary>ユーザーが年月を選んだ時、または<see cref="SelectedDate"/>が変わった時に発生する。</summary>
        public event EventHandler<DateTime?>? SelectedDateChanged;

        public YearMonthPicker()
        {
            InitializeComponent();

            Calendar.SelectedDateChanged += (_, date) =>
            {
                if (_isSyncing)
                    return;

                SelectedDate = date;
                PickerFlyout.Hide();
            };

            UpdateHeader();
            UpdateText();
        }

        private static void OnSelectedDateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var picker = (YearMonthPicker)d;

            // 1日以外が渡された場合は、カレンダー側で1日に丸められるので、その結果に揃える
            picker._isSyncing = true;
            picker.Calendar.SelectedDate = (DateTime?)e.NewValue;
            picker._isSyncing = false;

            var normalized = picker.Calendar.SelectedDate;
            if (normalized != (DateTime?)e.NewValue)
            {
                picker.SelectedDate = normalized;
                return;
            }

            picker.UpdateText();
            picker.SelectedDateChanged?.Invoke(picker, normalized);
        }

        private void UpdateHeader()
        {
            HeaderTextBlock.Text = Header ?? string.Empty;
            HeaderTextBlock.Visibility = string.IsNullOrEmpty(Header) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void UpdateText()
        {
            if (SelectedDate is { } date)
            {
                ValueTextBlock.Text = date.ToString(DisplayFormat, _culture ?? CultureInfo.CurrentCulture);
                ValueTextBlock.Visibility = Visibility.Visible;
                PlaceholderTextBlock.Visibility = Visibility.Collapsed;
            }
            else
            {
                PlaceholderTextBlock.Text = PlaceholderText;
                PlaceholderTextBlock.Visibility = Visibility.Visible;
                ValueTextBlock.Visibility = Visibility.Collapsed;
            }
        }
    }
}
