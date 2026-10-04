using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace UiLibrary
{
    /// <summary>
    /// UiLibraryの部品を一覧で見て、実際に操作して試せるギャラリー画面(WinUI 3 Galleryのようなもの)。
    /// 左のメニューから部品を選ぶと、説明・動くサンプル・設定を変えるコントロール・確認項目が出る。
    /// 部品を作ったり直したりした時の動作確認と、使い方の見本を兼ねる。
    /// </summary>
    public sealed class GalleryWindow : Window
    {
        private sealed record GalleryPage(string Title, string Glyph, Func<FrameworkElement> Build);

        private readonly NavigationView _navigation = new();
        private readonly ScrollViewer _host = new();
        private readonly Dictionary<string, FrameworkElement> _built = new();
        private readonly List<GalleryPage> _pages;

        public GalleryWindow()
        {
            Title = "UiLibrary Gallery";

            _pages = new List<GalleryPage>
            {
                new("MessageDialog", "", BuildMessageDialogPage),
                new("YearMonthCalendar", "", BuildCalendarPage),
                new("YearMonthPicker", "", BuildPickerPage),
                new("ShortcutKeyBox", "", BuildShortcutPage),
                new("ChipInput", "", BuildChipPage),
            };

            _navigation.IsSettingsVisible = false;
            _navigation.IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed;
            _navigation.PaneDisplayMode = NavigationViewPaneDisplayMode.Left;
            _navigation.OpenPaneLength = 220;

            foreach (var page in _pages)
            {
                _navigation.MenuItems.Add(new NavigationViewItem
                {
                    Content = page.Title,
                    Tag = page.Title,
                    Icon = new FontIcon
                    {
                        Glyph = page.Glyph,
                        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                    },
                });
            }

            _navigation.PaneFooter = CreateThemeSelector();
            _navigation.Content = _host;
            _navigation.SelectionChanged += (_, args) =>
            {
                if (args.SelectedItem is NavigationViewItem { Tag: string title })
                    Show(title);
            };

            Content = _navigation;
            _navigation.SelectedItem = _navigation.MenuItems[0];

            AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 800));
        }

        private void Show(string title)
        {
            if (!_built.TryGetValue(title, out var content))
            {
                content = _pages.First(p => p.Title == title).Build();
                _built[title] = content;
            }

            _host.Content = content;
        }

        // 左下のテーマ選択。この画面と、ダイアログ(MessageDialog)の明暗を切り替える
        private FrameworkElement CreateThemeSelector()
        {
            var combo = CreateEnumComboBox(ElementTheme.Default);
            combo.Margin = new Thickness(12, 0, 12, 12);
            combo.HorizontalAlignment = HorizontalAlignment.Stretch;
            combo.SelectionChanged += (_, _) =>
            {
                var theme = (ElementTheme)combo.SelectedItem;
                MessageDialog.Theme = theme;
                _navigation.RequestedTheme = theme;
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = "テーマ", Margin = new Thickness(16, 0, 12, 4), Opacity = 0.7, FontSize = 12 });
            stack.Children.Add(combo);
            return stack;
        }

        // ---- ページの共通部品 ----

        private static StackPanel CreatePage(string title, string description, string checklist)
        {
            var panel = new StackPanel { Padding = new Thickness(32, 24, 32, 32), Spacing = 12, MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };

            panel.Children.Add(new TextBlock { Text = title, FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            panel.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 });
            panel.Children.Add(new TextBlock
            {
                Text = "確認項目: " + checklist,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Opacity = 0.6,
            });

            return panel;
        }

        private static TextBlock SectionTitle(string text) => new()
        {
            Text = text,
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 12, 0, 0),
        };

        private static FrameworkElement Labeled(string label, FrameworkElement control)
        {
            var stack = new StackPanel { Spacing = 4 };
            stack.Children.Add(new TextBlock { Text = label, FontSize = 12, Opacity = 0.7 });
            stack.Children.Add(control);
            return stack;
        }

        private static StackPanel Row(params FrameworkElement[] children)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            foreach (var child in children)
                row.Children.Add(child);
            return row;
        }

        // 実際の部品を載せる枠(背景と枠線付き)
        private static Border Sample(FrameworkElement content) => new()
        {
            Child = content,
            Padding = new Thickness(20),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(60, 128, 128, 128)),
            MinHeight = 80,
        };

        // 「どう書くか」のソースの見本。等幅フォントで、選択もコピーボタンでのコピーもできる
        private static FrameworkElement CodeBlock(string title, string code)
        {
            var text = new TextBlock
            {
                Text = code,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas"),
                FontSize = 12,
                IsTextSelectionEnabled = true,
            };

            var copy = new Button
            {
                Content = new FontIcon
                {
                    Glyph = "",
                    FontSize = 12,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                },
                Padding = new Thickness(8, 4, 8, 4),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
            };
            ToolTipService.SetToolTip(copy, "コピー");
            copy.Click += (_, _) =>
            {
                var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
                data.SetText(code);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
            };

            var grid = new Grid { ColumnSpacing = 8 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(new ScrollViewer
            {
                Content = text,
                HorizontalScrollMode = ScrollMode.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollMode = ScrollMode.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            });
            Grid.SetColumn(copy, 1);
            grid.Children.Add(copy);

            var stack = new StackPanel { Spacing = 4 };
            stack.Children.Add(SectionTitle(title));
            stack.Children.Add(new Border
            {
                Child = grid,
                Padding = new Thickness(16),
                CornerRadius = new CornerRadius(8),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(24, 128, 128, 128)),
            });
            return stack;
        }

        private static ComboBox CreateEnumComboBox<T>(T selected) where T : struct, Enum
        {
            var values = Enum.GetValues<T>().Cast<object>().ToList();
            return new ComboBox
            {
                ItemsSource = values,
                SelectedItem = values.First(v => v.Equals(selected)),
                MinWidth = 240,
            };
        }

        // ---- MessageDialog ----

        private FrameworkElement BuildMessageDialogPage()
        {
            var panel = CreatePage(
                "MessageDialog",
                "MessageBoxW相当の機能を持つメッセージダイアログ。ContentDialogと違い、呼び出し元にWindowが無くても出せる。"
                + "カーソルのあるモニタの中央に、最前面で、モーダルで表示する。",
                "(1)押したボタンの名前が返る (2)表示中、このギャラリーなど他のウィンドウが操作できず、閉じると戻る "
                + "(3)Escまたは×で、キャンセルのボタンがあればキャンセル、OKだけならOK(はい/いいえだけの時は閉じられない) "
                + "(4)既定ボタンが強調されEnterで押せる (5)モニタの中央に出る (6)テーマの切り替えが反映される");

            var buttons = CreateEnumComboBox(MessageDialogButtons.OkCancel);
            var icon = CreateEnumComboBox(MessageDialogIcon.Information);
            var defaultButton = CreateEnumComboBox(MessageDialogDefaultButton.First);
            var message = new TextBox
            {
                Text = "テスト用のメッセージです。\nThis is a test message.\n長い行は折り返されるはずです。長い行は折り返されるはずです。長い行は折り返されるはずです。",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 90,
            };
            var result = new TextBlock { Text = "(結果はここに出ます)" };

            async Task Show()
            {
                var dialogResult = await MessageDialog.ShowAsync(
                    message.Text, "UiLibrary Gallery",
                    (MessageDialogButtons)buttons.SelectedItem,
                    (MessageDialogIcon)icon.SelectedItem,
                    (MessageDialogDefaultButton)defaultButton.SelectedItem);

                result.Text = $"結果: {dialogResult} (値 {(int)dialogResult})";
            }

            var showButton = new Button { Content = "表示" };
            showButton.Click += async (_, _) => await Show();

            var delayedButton = new Button { Content = "3秒後に表示(他のウィンドウへ切り替えて試す)" };
            delayedButton.Click += async (_, _) =>
            {
                await Task.Delay(3000);
                await Show();
            };

            var sample = new StackPanel { Spacing = 12 };
            sample.Children.Add(Labeled("ボタンの組み合わせ", buttons));
            sample.Children.Add(Labeled("アイコン", icon));
            sample.Children.Add(Labeled("既定ボタン", defaultButton));
            sample.Children.Add(Labeled("本文", message));
            sample.Children.Add(Row(showButton, delayedButton));
            sample.Children.Add(result);

            panel.Children.Add(SectionTitle("サンプル"));
            panel.Children.Add(Sample(sample));

            panel.Children.Add(CodeBlock("ソース(C#)", """
// 表示して、押されたボタンを受け取る(UIスレッドから呼ぶ)
var result = await MessageDialog.ShowAsync(
    "ファイルを削除しますか?",           // 本文
    "確認",                               // タイトル
    MessageDialogButtons.YesNo,           // ボタンの組み合わせ
    MessageDialogIcon.Question,           // アイコン
    MessageDialogDefaultButton.Second);   // 最初に選ばれているボタン

if (result == MessageDialogResult.Yes)
{
    // ...
}

// アプリ全体の設定(起動時に1回)
MessageDialog.Theme = ElementTheme.Dark;      // 明/暗(既定: OSに従う)
MessageDialog.ButtonLabelProvider = button => button switch
{
    MessageDialogResult.Ok => "OK",
    MessageDialogResult.Cancel => "キャンセル",
    _ => null,                                // nullなら英語の既定の文字
};
"""));
            return panel;
        }

        // ---- YearMonthCalendar ----

        private FrameworkElement BuildCalendarPage()
        {
            var panel = CreatePage(
                "YearMonthCalendar",
                "年と月を選ぶカレンダー。月の一覧(4x3)から月を選び、見出しの年を押すと年の一覧(12年ぶん)に切り替わる。"
                + "選択結果は、その月の1日(SelectedDate)として保持する。",
                "(1)年の見出しを押すと年の一覧になり、年を選ぶと月の一覧に戻る (2)< > で年(年の一覧では12年)が動き、範囲の端で押せなくなる "
                + "(3)範囲外(2020年3月より前/2027年10月より後)の月が押せない (4)選択中の月が塗られ、今月が枠で示される "
                + "(5)矢印キー・Tab・Enterで操作できる (6)カルチャ/書式の切り替えが反映される");

            var calendar = new YearMonthCalendar
            {
                MinDate = new DateTime(2020, 3, 1),
                MaxDate = new DateTime(2027, 10, 1),
                SelectedDate = new DateTime(2026, 5, 1),
                Culture = CultureInfo.GetCultureInfo("ja-JP"),
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            var result = new TextBlock { Text = $"SelectedDate: {calendar.SelectedDate:yyyy-MM-dd}" };
            calendar.SelectedDateChanged += (_, date) =>
                result.Text = $"SelectedDate: {(date is { } d ? d.ToString("yyyy-MM-dd") : "(未選択)")}";

            var culture = new ComboBox { ItemsSource = new[] { "ja-JP", "en-US", "zh-CN" }, SelectedIndex = 0 };
            culture.SelectionChanged += (_, _) => calendar.Culture = CultureInfo.GetCultureInfo((string)culture.SelectedItem);

            var monthFormat = new ComboBox { ItemsSource = new[] { "MMM", "MMMM", "M月" }, SelectedIndex = 0 };
            monthFormat.SelectionChanged += (_, _) => calendar.MonthFormat = (string)monthFormat.SelectedItem;

            var yearFormat = new ComboBox { ItemsSource = new[] { "yyyy", "yyyy年", "yy" }, SelectedIndex = 0 };
            yearFormat.SelectionChanged += (_, _) => calendar.YearFormat = (string)yearFormat.SelectedItem;

            var clear = new Button { Content = "選択を解除(SelectedDate = null)" };
            clear.Click += (_, _) => calendar.SelectedDate = null;

            var set = new Button { Content = "2024-12-15を設定(1日に丸められる)" };
            set.Click += (_, _) => calendar.SelectedDate = new DateTime(2024, 12, 15);

            var sample = new StackPanel { Spacing = 12 };
            sample.Children.Add(calendar);
            sample.Children.Add(result);

            var settings = new StackPanel { Spacing = 12 };
            settings.Children.Add(Labeled("カルチャ", culture));
            settings.Children.Add(Labeled("月の書式", monthFormat));
            settings.Children.Add(Labeled("年の書式", yearFormat));
            settings.Children.Add(Row(clear, set));

            panel.Children.Add(SectionTitle("サンプル(範囲: 2020年3月〜2027年10月)"));
            panel.Children.Add(Sample(sample));
            panel.Children.Add(SectionTitle("設定"));
            panel.Children.Add(Sample(settings));

            panel.Children.Add(CodeBlock("ソース(XAML)", """
<ui:YearMonthCalendar x:Name="Calendar"
                      SelectedDate="2026-05-01"
                      MinDate="2020-03-01"
                      MaxDate="2027-10-01"
                      MonthFormat="MMM"
                      YearFormat="yyyy年" />

(xmlns:ui="using:UiLibrary")
"""));
            panel.Children.Add(CodeBlock("ソース(C#)", """
var calendar = new YearMonthCalendar
{
    MinDate = new DateTime(2020, 3, 1),
    MaxDate = new DateTime(2027, 10, 1),
    Culture = CultureInfo.GetCultureInfo("ja-JP"),   // 月名の言語(省略するとOSの設定)
};

// 選ばれた年月(その月の1日)。未選択はnull
calendar.SelectedDate = new DateTime(2026, 5, 1);
calendar.SelectedDateChanged += (sender, date) =>
{
    // date?.Year, date?.Month
};
"""));
            return panel;
        }

        // ---- YearMonthPicker ----

        private FrameworkElement BuildPickerPage()
        {
            var panel = CreatePage(
                "YearMonthPicker",
                "年月を選ぶピッカー(CalendarDatePickerの年月版)。選択結果またはプレースホルダを枠付きの入力欄で表示し、"
                + "押すとYearMonthCalendarが下に開いて、月を選ぶと閉じる。",
                "(1)押すとカレンダーが開き、月を選ぶと閉じて表示が変わる (2)未選択の時は薄いプレースホルダが出る "
                + "(3)見出し(Header)が入力欄の上に出る (4)ホバー・押下で入力欄の色が変わる (5)ライト/ダークで見た目が切り替わる");

            var picker = new YearMonthPicker
            {
                Header = "対象の年月",
                DisplayFormat = "yyyy年M月",
                PlaceholderText = "年月を選択",
                MinDate = new DateTime(2020, 3, 1),
                MaxDate = new DateTime(2027, 10, 1),
            };
            var result = new TextBlock { Text = "SelectedDate: (未選択)" };
            picker.SelectedDateChanged += (_, date) =>
                result.Text = $"SelectedDate: {(date is { } d ? d.ToString("yyyy-MM-dd") : "(未選択)")}";

            var set = new Button { Content = "2025-08-20を設定(1日に丸められる)" };
            set.Click += (_, _) => picker.SelectedDate = new DateTime(2025, 8, 20);

            var clear = new Button { Content = "選択を解除" };
            clear.Click += (_, _) => picker.SelectedDate = null;

            var sample = new StackPanel { Spacing = 12 };
            sample.Children.Add(picker);
            sample.Children.Add(result);
            sample.Children.Add(Row(set, clear));

            panel.Children.Add(SectionTitle("サンプル"));
            panel.Children.Add(Sample(sample));

            panel.Children.Add(CodeBlock("ソース(XAML)", """
<ui:YearMonthPicker Header="対象の年月"
                    PlaceholderText="年月を選択"
                    DisplayFormat="yyyy年M月"
                    MinDate="2020-03-01"
                    MaxDate="2027-10-01" />
"""));
            panel.Children.Add(CodeBlock("ソース(C#)", """
var picker = new YearMonthPicker
{
    Header = "対象の年月",
    DisplayFormat = "yyyy年M月",    // 入力欄に出す書式
    PlaceholderText = "年月を選択",  // 未選択の時の文字
};

picker.SelectedDateChanged += (sender, date) =>
{
    // 月を選んだ時、または SelectedDate を変えた時に届く
};

picker.SelectedDate = new DateTime(2025, 8, 1);   // 設定(日は1日に丸められる)
"""));
            return panel;
        }

        // ---- ShortcutKeyBox ----

        private FrameworkElement BuildShortcutPage()
        {
            var panel = CreatePage(
                "ShortcutKeyBox",
                "ショートカットキーを入力する部品。押すとキー入力待ちになり、修飾キーを押しながら別のキーを押すと確定する。"
                + "Winキーのチェックボックス付き(ShortcutKeyBoxWithWin)と、無し(ShortcutKeyBox)の2種類がある。"
                + "Winは、Windowsが先に処理してしまうため、押して入力せずチェックで指定する。",
                "(1)押すと入力待ちになる (2)Ctrlなどを押している間は「Ctrl + ...」と途中経過が出る (3)Ctrl+Shift+Aなどで確定する "
                + "(4)Escやフォーカスが外れると入力待ちをやめる (5)修飾キー必須の時、Aだけでは確定しない (6)×で解除できる "
                + "(7)Ctrlを押して離してからShift+Aを押しても、Ctrlが残らない (8)WinにチェックしてEを押すと、Win+Eになる");

            var withWin = new ShortcutKeyBoxWithWin
            {
                PlaceholderText = "未設定",
                CapturingText = "キーを押してください...",
                Shortcut = new ShortcutKey(ShortcutModifiers.Control | ShortcutModifiers.Shift, 0x56),
            };
            var withWinResult = new TextBlock { Text = "Shortcut: Ctrl + Shift + V" };
            withWin.ShortcutChanged += (_, key) => withWinResult.Text = Describe(key);

            var requireModifier = new ToggleSwitch { Header = "修飾キーを必須にする", IsOn = true };
            requireModifier.Toggled += (_, _) => withWin.RequireModifier = requireModifier.IsOn;

            var set = new Button { Content = "Ctrl + Alt + Fを設定" };
            set.Click += (_, _) => withWin.Shortcut = new ShortcutKey(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x46);

            var plain = new ShortcutKeyBox
            {
                PlaceholderText = "未設定",
                CapturingText = "キーを押してください...",
            };
            var plainResult = new TextBlock { Text = "Shortcut: (未設定)" };
            plain.ShortcutChanged += (_, key) => plainResult.Text = Describe(key);

            var plainRequireModifier = new ToggleSwitch { Header = "修飾キーを必須にする", IsOn = true };
            plainRequireModifier.Toggled += (_, _) => plain.RequireModifier = plainRequireModifier.IsOn;

            var sampleWithWin = new StackPanel { Spacing = 12 };
            sampleWithWin.Children.Add(withWin);
            sampleWithWin.Children.Add(withWinResult);
            sampleWithWin.Children.Add(requireModifier);
            sampleWithWin.Children.Add(set);

            var samplePlain = new StackPanel { Spacing = 12 };
            samplePlain.Children.Add(plain);
            samplePlain.Children.Add(plainResult);
            samplePlain.Children.Add(plainRequireModifier);

            panel.Children.Add(SectionTitle("ShortcutKeyBoxWithWin(Winのチェックボックス付き)"));
            panel.Children.Add(Sample(sampleWithWin));
            panel.Children.Add(SectionTitle("ShortcutKeyBox(Winのチェックボックスなし)"));
            panel.Children.Add(Sample(samplePlain));

            panel.Children.Add(CodeBlock("ソース(XAML)", """
<!-- Winのチェックボックス付き -->
<ui:ShortcutKeyBoxWithWin PlaceholderText="未設定"
                          CapturingText="キーを押してください..." />

<!-- Winのチェックボックスなし -->
<ui:ShortcutKeyBox RequireModifier="True" IsClearable="True" />
"""));
            panel.Children.Add(CodeBlock("ソース(C#)", """
var box = new ShortcutKeyBoxWithWin();

// 設定(修飾キーの値はWin32のMOD_*と同じ。RegisterHotKeyにそのまま渡せる)
box.Shortcut = new ShortcutKey(
    ShortcutModifiers.Control | ShortcutModifiers.Shift,
    0x56);   // 仮想キーコード(VK_V)

box.ShortcutChanged += (sender, key) =>
{
    if (key is null)
    {
        // 解除された
        return;
    }

    uint modifiers = (uint)key.Modifiers;
    uint virtualKey = key.VirtualKey;
    string text = key.ToString();    // "Ctrl + Shift + V"
    // RegisterHotKey(hWnd, id, modifiers, virtualKey);
};
"""));
            return panel;
        }

        private static string Describe(ShortcutKey? key)
            => key is null
                ? "Shortcut: (未設定)"
                : $"Shortcut: {key}  (Modifiers={(uint)key.Modifiers}, VK=0x{key.VirtualKey:X2})";

        // ---- ChipInput ----

        private FrameworkElement BuildChipPage()
        {
            var panel = CreatePage(
                "ChipInput",
                "複数の文字列を、チップ(タグ)の並びとして入力する部品。下の入力欄にEnterか区切り文字(, ; など)で追加し、"
                + "チップの×で削除する。",
                "(1)Enterで追加され入力欄が空になる (2)×でそのチップが消える (3)入力欄が空でBackspaceを押すと最後のチップが消える "
                + "(4)「a,b,c」を貼り付けると3つ追加される (5)chromeとChromeは重複扱い(大文字小文字を区別しない) "
                + "(6)幅に収まらないと折り返す(チップを増やして確認)");

            var chips = new ChipInput
            {
                Header = "除外するアプリ",
                PlaceholderText = "名前を入力してEnter(, ; で区切って貼り付けも可)",
                Width = 460,
            };
            chips.Items.Add("chrome");
            chips.Items.Add("notepad");

            var log = new TextBlock { Text = "(イベントの結果)", TextWrapping = TextWrapping.Wrap };
            chips.ItemAdded += (_, v) => log.Text = $"追加: {v} / 合計 {chips.Items.Count} 件";
            chips.ItemRemoved += (_, v) => log.Text = $"削除: {v} / 合計 {chips.Items.Count} 件";
            chips.ItemRejected += (_, v) => log.Text = $"拒否: {v}(重複、または3文字未満)";

            var validate = new ToggleSwitch { Header = "3文字以上だけ追加できる(Validator)", IsOn = false };
            validate.Toggled += (_, _) => chips.Validator = validate.IsOn ? v => v.Length >= 3 : null;

            var duplicates = new ToggleSwitch { Header = "重複を許可する", IsOn = false };
            duplicates.Toggled += (_, _) => chips.AllowDuplicates = duplicates.IsOn;

            var sample = new StackPanel { Spacing = 12 };
            sample.Children.Add(chips);
            sample.Children.Add(log);

            var settings = new StackPanel { Spacing = 12 };
            settings.Children.Add(validate);
            settings.Children.Add(duplicates);

            panel.Children.Add(SectionTitle("サンプル"));
            panel.Children.Add(Sample(sample));
            panel.Children.Add(SectionTitle("設定"));
            panel.Children.Add(Sample(settings));

            panel.Children.Add(CodeBlock("ソース(XAML)", """
<ui:ChipInput x:Name="Chips"
              Header="除外するアプリ"
              PlaceholderText="名前を入力してEnter" />
"""));
            panel.Children.Add(CodeBlock("ソース(C#)", """
// 初期値(Itemsは ObservableCollection<string>。直接操作しても表示に反映される)
Chips.Items.Add("chrome");
Chips.Items.Add("notepad");

Chips.AllowDuplicates = false;                       // 重複を許さない(大文字小文字は区別しない)
Chips.Validator = value => value.Length >= 3;        // falseを返した文字列は追加されない
Chips.Separators = new[] { ',', ';' };                // 入力を区切る文字(Enterは常に区切り)

Chips.ItemAdded += (sender, value) => { /* 追加された */ };
Chips.ItemRemoved += (sender, value) => { /* 削除された */ };
Chips.ItemRejected += (sender, value) => { /* 重複や Validator に断られた */ };

// コードから追加・削除
bool added = Chips.TryAdd("firefox");
Chips.Remove("notepad");
"""));
            return panel;
        }
    }
}
