using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace UiLibrary
{
    /// <summary>
    /// 年と月を選ぶカレンダー部品。月の一覧(4x3)から月を選び、見出しの年を押すと
    /// 年の一覧(12年ぶん)に切り替わって年を選べる。
    /// 選択結果は、その月の1日(<see cref="SelectedDate"/>)として保持する。
    /// </summary>
    public sealed partial class YearMonthCalendar : UserControl
    {
        private enum ViewMode
        {
            Months,
            Years
        }

        public static readonly DependencyProperty SelectedDateProperty = DependencyProperty.Register(
            nameof(SelectedDate), typeof(DateTime?), typeof(YearMonthCalendar),
            new PropertyMetadata(null, OnSelectedDateChanged));

        public static readonly DependencyProperty MinDateProperty = DependencyProperty.Register(
            nameof(MinDate), typeof(DateTime), typeof(YearMonthCalendar),
            new PropertyMetadata(new DateTime(1900, 1, 1), OnRangeChanged));

        public static readonly DependencyProperty MaxDateProperty = DependencyProperty.Register(
            nameof(MaxDate), typeof(DateTime), typeof(YearMonthCalendar),
            new PropertyMetadata(new DateTime(2200, 12, 31), OnRangeChanged));

        public static readonly DependencyProperty YearFormatProperty = DependencyProperty.Register(
            nameof(YearFormat), typeof(string), typeof(YearMonthCalendar),
            new PropertyMetadata("yyyy", OnFormatChanged));

        public static readonly DependencyProperty MonthFormatProperty = DependencyProperty.Register(
            nameof(MonthFormat), typeof(string), typeof(YearMonthCalendar),
            new PropertyMetadata("MMM", OnFormatChanged));

        private readonly List<Button> _cells = new();
        private ViewMode _mode = ViewMode.Months;

        // 一覧に出している年(月の一覧の時)、または年の一覧が含む年(年の一覧の時)
        private int _displayYear;

        private CultureInfo? _culture;

        /// <summary>選ばれている年月の1日。未選択ならnull。1日以外を渡しても1日に丸める。</summary>
        public DateTime? SelectedDate
        {
            get => (DateTime?)GetValue(SelectedDateProperty);
            set => SetValue(SelectedDateProperty, value);
        }

        /// <summary>選べる最小の日付(この日付を含む月から選べる)。</summary>
        public DateTime MinDate
        {
            get => (DateTime)GetValue(MinDateProperty);
            set => SetValue(MinDateProperty, value);
        }

        /// <summary>選べる最大の日付(この日付を含む月まで選べる)。</summary>
        public DateTime MaxDate
        {
            get => (DateTime)GetValue(MaxDateProperty);
            set => SetValue(MaxDateProperty, value);
        }

        /// <summary>年の表示書式(DateTimeの書式文字列)。例: "yyyy"、"yyyy年"。</summary>
        public string YearFormat
        {
            get => (string)GetValue(YearFormatProperty);
            set => SetValue(YearFormatProperty, value);
        }

        /// <summary>月の表示書式(DateTimeの書式文字列)。例: "MMM"(1月/Jan)、"MMMM"(1月/January)。</summary>
        public string MonthFormat
        {
            get => (string)GetValue(MonthFormatProperty);
            set => SetValue(MonthFormatProperty, value);
        }

        /// <summary>
        /// 月名などの表示に使うカルチャ。未設定の時はOSの現在のカルチャを使う
        /// (アプリが独自の言語設定を持つ場合は、アプリ側が設定する)。
        /// </summary>
        public CultureInfo? Culture
        {
            get => _culture;
            set
            {
                _culture = value;
                Refresh();
            }
        }

        /// <summary>ユーザーが年月を選んだ時、または<see cref="SelectedDate"/>が変わった時に発生する。</summary>
        public event EventHandler<DateTime?>? SelectedDateChanged;

        public YearMonthCalendar()
        {
            InitializeComponent();
            CreateCells();

            _displayYear = ClampYear((SelectedDate ?? DateTime.Today).Year);
            Refresh();
        }

        private static void OnSelectedDateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (YearMonthCalendar)d;

            // 1日以外が渡された場合は、その月の1日に揃える(揃えた値で再度この処理が呼ばれる)
            if (e.NewValue is DateTime value && value.Day != 1)
            {
                control.SelectedDate = new DateTime(value.Year, value.Month, 1);
                return;
            }

            if (control.SelectedDate is { } selected)
                control._displayYear = control.ClampYear(selected.Year);

            control.Refresh();
            control.SelectedDateChanged?.Invoke(control, control.SelectedDate);
        }

        private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (YearMonthCalendar)d;
            control._displayYear = control.ClampYear(control._displayYear);
            control.Refresh();
        }

        private static void OnFormatChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((YearMonthCalendar)d).Refresh();

        private void CreateCells()
        {
            for (var i = 0; i < 12; i++)
            {
                var cell = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Height = 44,
                    Tag = i,
                };
                cell.Click += Cell_Click;

                Grid.SetRow(cell, i / 4);
                Grid.SetColumn(cell, i % 4);

                _cells.Add(cell);
                CellsGrid.Children.Add(cell);
            }
        }

        private int ClampYear(int year) => Math.Clamp(year, MinDate.Year, MaxDate.Year);

        private bool IsMonthInRange(int year, int month)
        {
            var first = new DateTime(year, month, 1);
            var min = new DateTime(MinDate.Year, MinDate.Month, 1);
            var max = new DateTime(MaxDate.Year, MaxDate.Month, 1);
            return first >= min && first <= max;
        }

        private int YearPageStart => _displayYear / 12 * 12;

        private void Refresh()
        {
            // コンストラクタのInitializeComponentより前に呼ばれることがある(DPの初期値の設定時)
            if (_cells.Count == 0)
                return;

            var culture = _culture ?? CultureInfo.CurrentCulture;
            var today = DateTime.Today;
            var selected = SelectedDate;

            if (_mode == ViewMode.Months)
            {
                TitleButton.Content = SafeFormat(_displayYear, 1, YearFormat, culture);
                PreviousButton.IsEnabled = _displayYear > MinDate.Year;
                NextButton.IsEnabled = _displayYear < MaxDate.Year;

                for (var month = 1; month <= 12; month++)
                {
                    var cell = _cells[month - 1];
                    cell.Content = SafeFormat(2000, month, MonthFormat, culture);
                    cell.IsEnabled = IsMonthInRange(_displayYear, month);

                    ApplyCellStyle(
                        cell,
                        isSelected: selected is { } s && s.Year == _displayYear && s.Month == month,
                        isCurrent: today.Year == _displayYear && today.Month == month);
                }
            }
            else
            {
                var start = YearPageStart;
                TitleButton.Content = $"{SafeFormat(start, 1, YearFormat, culture)} - {SafeFormat(start + 11, 1, YearFormat, culture)}";
                PreviousButton.IsEnabled = start > MinDate.Year;
                NextButton.IsEnabled = start + 11 < MaxDate.Year;

                for (var i = 0; i < 12; i++)
                {
                    var year = start + i;
                    var cell = _cells[i];
                    cell.Content = SafeFormat(year, 1, YearFormat, culture);
                    cell.IsEnabled = year >= MinDate.Year && year <= MaxDate.Year;

                    ApplyCellStyle(
                        cell,
                        isSelected: selected is { } s && s.Year == year,
                        isCurrent: today.Year == year);
                }
            }
        }

        // DateTimeの範囲(1〜9999年)の外や、不正な書式でも例外にしない
        private static string SafeFormat(int year, int month, string format, CultureInfo culture)
        {
            if (year < 1 || year > 9999)
                return string.Empty;

            try
            {
                return new DateTime(year, month, 1).ToString(format, culture);
            }
            catch (FormatException)
            {
                return new DateTime(year, month, 1).ToString("d", culture);
            }
        }

        private static void ApplyCellStyle(Button cell, bool isSelected, bool isCurrent)
        {
            if (isSelected)
            {
                // 選択中はアクセントカラーの塗り(ローカル値が優先されるため、背景・枠を消してスタイルに任せる)
                cell.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
                cell.ClearValue(Control.BackgroundProperty);
                cell.ClearValue(Control.BorderThicknessProperty);
                cell.ClearValue(Control.BorderBrushProperty);
                cell.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                return;
            }

            cell.Style = null;
            cell.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

            // 今日を含む月/年は、アクセントカラーの細い枠で示す
            if (isCurrent)
            {
                cell.BorderThickness = new Thickness(1);
                cell.BorderBrush = new SolidColorBrush((Windows.UI.Color)Application.Current.Resources["SystemAccentColor"]);
                cell.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            }
            else
            {
                cell.BorderThickness = new Thickness(0);
                cell.ClearValue(Control.BorderBrushProperty);
                cell.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
            }
        }

        private void Cell_Click(object sender, RoutedEventArgs e)
        {
            var index = (int)((Button)sender).Tag;

            if (_mode == ViewMode.Months)
            {
                SelectedDate = new DateTime(_displayYear, index + 1, 1);
                return;
            }

            // 年を選んだら、その年の月の一覧に戻る(月はまだ選ばない)
            _displayYear = YearPageStart + index;
            _mode = ViewMode.Months;
            Refresh();
        }

        private void TitleButton_Click(object sender, RoutedEventArgs e)
        {
            _mode = _mode == ViewMode.Months ? ViewMode.Years : ViewMode.Months;
            Refresh();
        }

        private void PreviousButton_Click(object sender, RoutedEventArgs e) => Move(-1);

        private void NextButton_Click(object sender, RoutedEventArgs e) => Move(1);

        // 月の一覧では1年、年の一覧では12年ぶん動く
        private void Move(int direction)
        {
            var step = _mode == ViewMode.Months ? 1 : 12;
            _displayYear = ClampYear(_displayYear + direction * step);
            Refresh();
        }
    }
}
