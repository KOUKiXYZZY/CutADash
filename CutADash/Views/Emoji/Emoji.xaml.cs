using CutADash.Models;
using CutADash.Repositories;
using CutADash.Utils;
using CutADash.ViewModels;
using CutADash.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Threading.Tasks;
using WinUIEx;

namespace CutADash.Views.Emoji
{
    /// <summary>
    /// 絵文字グリッドを表示するページ。データの読み込みはEmojiGridViewModelが担い、
    /// 表示はItemsRepeaterに任せる。スクロールではなくPipsPagerによるページ送りにしたため
    /// (目当ての絵文字を探すのにスクロール量が読めず使いづらかった)、常に1ページぶんの
    /// 件数しか実体化されない。以前ItemsRepeaterが重かったのは全件を一度に実体化して
    /// いたことが原因で、ページングそのものがその対策になる。
    ///
    /// コンパクト表示は、以前は全カテゴリを見出し付きでまとめて表示していたが、
    /// ウィンドウが狭くカテゴリ一覧(ListFrame)と同時に見られないため、Windowsの
    /// 絵文字ピッカーのようにアイコン(代表絵文字)タブでカテゴリを切り替える方式にした
    /// (中身は通常表示と同じFlatRepeater/ページングをそのまま使う)。
    ///
    /// クリック/Enter/Spaceでのペーストはコマンド呼び出し(EmojiCellPasteBehavior経由)に
    /// 任せており、ここではカテゴリ切り替え・コンパクト表示の切り替え・ページ送り・
    /// スプライト画像の描画・肌の色バリエーションFlyout(右クリック)の表示を扱う。
    /// </summary>
    public sealed partial class Emoji : Page
    {
        private bool _isCompact;
        private Brush? _hoverBrush;
        private MainWindow? _mainWindow;
        private EmojiRepository? _repository;

        // 肌の色バリエーションを持つ絵文字は、右クリックでFlyoutを出して選ばせる。
        // ToolTip.IsOpenを手動でtrueにするとPlacementTargetの状態によっては
        // WinUI内部でNullReferenceExceptionを起こすことがあった(実際に踏んだ不具合)ため、
        // ShowAtで明示的に表示できるFlyoutを使う。_variantsFlyoutAnchorは、
        // 開こうとしたFlyoutが(次の描画サイクルまで待つ間に)別のセルへの要求で
        // 上書きされていないかを確認するためのアンカー
        private Border? _variantsFlyoutAnchor;
        private Flyout? _variantsFlyout;

        // CompactCategoryTabsのSelectionChangedはコードから設定した時にも発火するため、
        // ユーザー操作によるものかを区別するためのガード
        // (PipsPagerの方はPipsPagerPageCommandBehaviorへ同じ役割を移した)
        private bool _isUpdatingCompactTabsFromCode;

        public EmojiGridViewModel? ViewModel { get; private set; }

        public Emoji() {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            // ListFrame(EmojiListFrame)でカテゴリが選ばれたら、その種類だけ表示する
            string? category = null;
            bool categorySpecified = false;
            if (e.Parameter is EmojiNavigationParameter param)
            {
                category = param.Category;
                categorySpecified = param.CategorySpecified;
                _mainWindow = param.MainWindow;
            }
            else
            {
                category = e.Parameter as string;
                categorySpecified = category is not null;
            }

            var repository = _mainWindow?.Provider?.GetService<EmojiRepository>();
            if (repository is null)
                return;

            ViewModel = new EmojiGridViewModel(repository, _mainWindow) { Category = category };
            DataContext = ViewModel;
            // x:BindはViewModelインスタンスの差し替え自体には自動追従しないため、
            // 新しいインスタンスの各プロパティを購読し直させる
            Bindings.Update();

            // ページ送りのたびに強調のフェードインを掛ける(見た目の演出はViewが持つ責務のため、
            // ここはコードビハインドに残す。ページ切り替えの実処理自体はViewModel/Behavior側)
            ViewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(EmojiGridViewModel.PageIndex))
                    FadeInAnimationHelper.FadeInFromBottom(FlatRepeater);
            };

            _ = InitializeAsync(repository, categorySpecified);
        }

        // タブを開いた直後にいきなり全件表示すると描画・メモリの負荷が大きいため、
        // 初期表示は先頭カテゴリだけに絞る。
        // 「全部」を明示的に選んだ場合(CategorySpecified=true, Category=null)は全件表示する
        private async Task InitializeAsync(EmojiRepository repository, bool categorySpecified)
        {
            if (!categorySpecified && ViewModel is not null && ViewModel.Category is null)
            {
                var kinds = await repository.GetDistinctKindsAsync();
                ViewModel.Category = kinds.Count > 0 ? kinds[0] : null;
            }

            _repository = repository;

            if (ViewModel is not null)
            {
                SetLoading(true);
                try
                {
                    FlatRepeater.ItemsSource = ViewModel.Page;
                    await ViewModel.LoadEmojisAsync(CalculatePageSize());
                }
                finally
                {
                    SetLoading(false);
                }
            }
        }

        /// <summary>カテゴリを切り替えて表示し直す(EmojiListFrameのページ再利用パスから呼ばれる)。</summary>
        public async Task ShowCategoryAsync(string? category)
        {
            if (ViewModel is null)
                return;

            SetLoading(true);
            try
            {
                ViewModel.Category = category;

                await ViewModel.LoadEmojisAsync(CalculatePageSize());
                UpdateCompactTabSelection();
                TryFocusPendingIndex();
            }
            finally
            {
                SetLoading(false);
            }
        }

        // 絵文字セル1個ぶんのピッチ(幅/高さ32 + すき間4)。
        // XAML側(EmojiGridLayoutのMinItemWidth/Height, MinRow/ColumnSpacing)を
        // 変えたらここも合わせること
        private const double CellPitch = 36;

        // ページ送り(PipsPager)のおおよその幅。縦向きで右側に配置しているため、
        // 1ページに収まる列数を計算する際、GridRootの幅からこのぶんを差し引く
        private const double PagerBarWidth = 48;

        // コンパクト表示のカテゴリタブバーのおおよその高さ。表示中はGridRootの高さから差し引く
        private const double CompactTabsHeight = 44;

        /// <summary>
        /// 1ページに収まる件数を、GridRootの実際の大きさから求める。
        /// スクロールさせない前提のため、はみ出さないよう切り捨てで計算する。
        /// </summary>
        private int CalculatePageSize()
        {
            var width = GridRoot.ActualWidth - PagerBarWidth;
            var height = GridRoot.ActualHeight - (_isCompact ? CompactTabsHeight : 0);

            if (width <= 0 || height <= 0)
                return 0; // レイアウト前。ViewModel側の既定値に任せる

            var columns = Math.Max(1, (int)(width / CellPitch));
            var rows = Math.Max(1, (int)(height / CellPitch));

            return columns * rows;
        }

        // ウィンドウサイズが変わったら、1ページの件数を計算し直して組み直す
        private void GridRoot_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (ViewModel is null)
                return;

            ViewModel.Relayout(CalculatePageSize());
        }

        // タブを離れる際(History/Favoriteなど他のタブへNavigateする直前)に呼び、
        // このページインスタンスが持つ参照を全て切り離す。NavigationCacheMode="Disabled"
        // (既定)によりEmojiへ再度Navigateした時は必ず新しいPage/ViewModelが作られるため、
        // このインスタンス自体はNavigate後にContentFrameからもGCの対象になる
        public void ReleaseData()
        {
            FlatRepeater.ItemsSource = null;
            CompactCategoryTabs.ItemsSource = null;
            ViewModel?.Page.Clear();
            ViewModel?.CompactCategories.Clear();
            ViewModel = null;
            DataContext = null;

            // 右クリックで開いたバリエーション選択Flyoutが開いたままタブを離れると、
            // FlyoutのPopupがWindow直下のPopupコレクションに残り続け、そのContent
            // (Pageのthisをキャプチャしたクロージャ)経由でPage全体がGCされなくなる
            // (実際に踏んだ不具合: 2回目以降のタブ切り替えでEmojiページが回収されなくなる)。
            // nullにする前に必ず明示的に閉じる
            _variantsFlyout?.Hide();
            if (_variantsFlyout is not null)
                _variantsFlyout.Content = null;

            _repository = null;
            _mainWindow = null;
            _hoverBrush = null;
            _variantsFlyoutAnchor = null;
            _variantsFlyout = null;

            // スプライトシート(kindごとに最大96px×数百pxのBitmapImage)のキャッシュも、
            // タブを離れたタイミングで明示的に解放する
            Utils.EmojiSpriteAssets.ClearCache();

            // 参照を全て切り離した直後であれば早期解放を促してよい。ただし
            // GC.WaitForPendingFinalizers()を伴う強いGCは、BitmapImage内部の
            // ネイティブ(WinRT)リソースを不安定にし、再表示時に画像が描画されなくなる
            // 不具合を起こした(実際に踏んだ不具合)ため、ブロッキングしない
            // 最適化モードのGen2コレクションに留める
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: false);
        }

        // ウィンドウ縮小時はカテゴリをアイコンタブで切り替える表示にする
        public void SetCompactMode(bool isCompact)
        {
            if (isCompact == _isCompact)
                return;

            _isCompact = isCompact;
            CompactCategoryTabs.Visibility = isCompact ? Visibility.Visible : Visibility.Collapsed;

            _ = RefreshCompactAsync();
        }

        private async Task RefreshCompactAsync()
        {
            if (ViewModel is null)
                return;

            SetLoading(true);
            try
            {
                if (_isCompact)
                {
                    await ViewModel.LoadCompactCategoriesAsync();
                    CompactCategoryTabs.ItemsSource = ViewModel.CompactCategories;

                    // まだカテゴリが選ばれていない(「全部」表示中)場合は、
                    // タブ切り替えの都合上、先頭のカテゴリを選んだことにする
                    if (ViewModel.Category is null && ViewModel.CompactCategories.Count > 0)
                        ViewModel.Category = ViewModel.CompactCategories[0].Kind;

                    UpdateCompactTabSelection();
                }

                // タブバーの表示/非表示でページに収まる件数が変わるため、必ず組み直す
                await ViewModel.LoadEmojisAsync(CalculatePageSize());

                // PageIndexが0のまま(切り替え前から既に1ページ目)だと値が変わらず
                // PropertyChangedが飛ばない(=OnNavigatedTo側のフェードイン購読が発火しない)ため、
                // カテゴリ/タブ切り替えの直後は明示的にも掛けておく
                FadeInAnimationHelper.FadeInFromBottom(FlatRepeater);
            }
            finally
            {
                SetLoading(false);
            }
        }

        private void UpdateCompactTabSelection()
        {
            if (ViewModel is null)
                return;

            _isUpdatingCompactTabsFromCode = true;
            try
            {
                CompactCategoryTabs.SelectedItem = null;
                foreach (var tab in ViewModel.CompactCategories)
                {
                    if (tab.Kind == ViewModel.Category)
                    {
                        CompactCategoryTabs.SelectedItem = tab;
                        break;
                    }
                }
            }
            finally
            {
                _isUpdatingCompactTabsFromCode = false;
            }
        }

        private async void CompactCategoryTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingCompactTabsFromCode || ViewModel is null)
                return;

            if (CompactCategoryTabs.SelectedItem is not EmojiCategoryTab tab)
                return;

            if (tab.Kind == ViewModel.Category)
                return;

            SetLoading(true);
            try
            {
                ViewModel.Category = tab.Kind;
                await ViewModel.LoadEmojisAsync(CalculatePageSize());
                FadeInAnimationHelper.FadeInFromBottom(FlatRepeater);
            }
            finally
            {
                SetLoading(false);
            }
        }

        // ホットキーでEmojiタブを開いた直後などに呼ばれる。一覧の読み込みが終わっていない
        // 場合もあるため要求だけ立てておき、TryFocusPendingIndex完了時にも再試行する
        private int? _pendingFocusIndex;

        // 現在フォーカスされているセルのFlatRepeater内インデックスを求め、delta分だけ
        // ずらしたセルへフォーカスを移す。範囲外(先頭より前/末尾より後ろ)なら何もしない
        // (折り返しはしない)。現在の要素がFlatRepeaterのセルとして見つからない場合はfalseを返す
        private bool TryMoveFocusByIndex(int delta)
        {
            if (FocusManager.GetFocusedElement(XamlRoot) is not UIElement focused)
                return false;

            var index = FlatRepeater.GetElementIndex(focused);
            if (index < 0)
                return false;

            var itemCount = FlatRepeater.ItemsSourceView?.Count ?? 0;
            var newIndex = index + delta;
            if (newIndex < 0 || newIndex >= itemCount)
                return true; // 範囲外は「何もしない」が正しい結果なので、フォールバックはさせない

            FlatRepeater.UpdateLayout();
            if (FlatRepeater.TryGetElement(newIndex) is Border cell)
                cell.Focus(FocusState.Keyboard);

            return true;
        }

        public void SelectItemAtIndex(int index)
        {
            _pendingFocusIndex = index;
            _pendingFocusAttempts = 0;
            TryFocusPendingIndex();
        }

        // データ読み込み中にSelectItemAtIndexが呼ばれると、1回の再試行だけでは
        // 実体化が間に合わないことがあり、そのまま何もフォーカスされずに終わっていた。
        // 実際にセルへフォーカスできるまで、上限を設けて繰り返し再試行する
        private int _pendingFocusAttempts;
        private const int MaxPendingFocusAttempts = 20;

        private void TryFocusPendingIndex()
        {
            if (_pendingFocusIndex is not int index)
                return;

            // 実体化(レイアウト)が終わってからでないとTryGetElementがnullを返すため、
            // 描画キューの後ろに回してから取得する
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (_pendingFocusIndex != index)
                    return;

                FlatRepeater.UpdateLayout();
                if (FlatRepeater.TryGetElement(index) is Border cell)
                {
                    _pendingFocusIndex = null;
                    cell.Focus(FocusState.Keyboard);
                }
                else if (++_pendingFocusAttempts < MaxPendingFocusAttempts)
                {
                    // まだ実体化されていない(ItemsSource未設定/レイアウト未完了)ため、
                    // 次の描画サイクルで再試行する
                    TryFocusPendingIndex();
                }
                else
                {
                    _pendingFocusIndex = null;
                }
            });
        }

        // セルが実体化(リサイクルでの再利用も含む)されるたびに呼ぶ
        private void FlatRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
        {
            if (args.Element is Border border)
                ApplySpriteAndBadge(border);
        }

        // 該当kindのスプライトシート(Assets/Emoji/{kind}.scale-*.png、10列)全体を
        // セルのImageへSourceとして設定し、該当部分だけが見えるようMarginで平行移動する
        // (Grid.Clipで32x32の窓は既にXAML側に固定してある)。
        // スプライトの表示サイズはColumns*32 x rows*32 DIPに固定し、Stretch=Fillで
        // 合わせているため、実際に読み込んだPNGが32/64/96pxのどれでもセル位置の
        // 計算(Index%Columns等)は変わらない
        private void ApplySpriteAndBadge(Border border)
        {
            if (FindDescendant<Image>(border, "SpriteImage") is not Image image
                || border.DataContext is not EmojiItem item || _repository is null)
                return;

            var itemCount = _repository.GetKindCount(item.Kind);
            var dpiScale = XamlRoot?.RasterizationScale ?? 1.0;
            var (source, width, height) = Utils.EmojiSpriteAssets.GetOrLoad(item.Kind, itemCount, dpiScale);

            image.Source = source;
            image.Width = width;
            image.Height = height;
            image.Stretch = Stretch.Fill;

            var columns = Utils.EmojiSpriteAssets.Columns;
            var cell = Utils.EmojiSpriteAssets.CellDip;
            var col = item.Index % columns;
            var row = item.Index / columns;
            image.Margin = new Thickness(-col * cell, -row * cell, 0, 0);

            if (FindDescendant<Ellipse>(border, "VariantBadge") is Ellipse badge)
                badge.Visibility = item.HasVariants ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// 現在実体化されている全セルのスプライト画像を読み直す。
        /// CompositionTarget.SurfaceContentsLost(GPUデバイスロスト等でコンポジション面が
        /// 失われた時に発生)を受けて、絵文字がときおり表示されなくなる不具合の対策として、
        /// MainWindow側(App全体を監視)から呼ばれる。
        /// </summary>
        public void RefreshSpriteImages()
        {
            var count = VisualTreeHelper.GetChildrenCount(FlatRepeater);
            for (var i = 0; i < count; i++)
            {
                if (VisualTreeHelper.GetChild(FlatRepeater, i) is Border border)
                    ApplySpriteAndBadge(border);
            }
        }

        // x:Nameでのテンプレート内名前解決(FindName)がItemsRepeaterの実体化要素では
        // 期待通り動かなかったため、Visual Treeを直接辿って該当名前の子孫を探す
        private static T? FindDescendant<T>(DependencyObject root, string name) where T : FrameworkElement
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T typed && typed.Name == name)
                    return typed;

                if (FindDescendant<T>(child, name) is T found)
                    return found;
            }
            return null;
        }

        // 読み込み中はProgressRingを表示する
        private void SetLoading(bool isLoading)
        {
            LoadingRing.IsActive = isLoading;
            LoadingRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        }

        // ホバーで背景を付け、Textが登録されていればツールチップも設定する。
        // ツールチップはテンプレートに静的に持たせず、実際にホバーされた時だけ生成する
        private void EmojiCell_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not Border border)
                return;

            _hoverBrush ??= (Brush?)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
            border.Background = _hoverBrush;

            if (border.DataContext is EmojiItem item &&
                !string.IsNullOrWhiteSpace(item.Text) &&
                ToolTipService.GetToolTip(border) is null)
            {
                ToolTipService.SetToolTip(border, BuildTooltipContent(item, item.Text));
            }
        }

        private void EmojiCell_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not Border border)
                return;

            // Flyoutを表示するとポインターがBorderの外に出たと判定され、直後に
            // PointerExitedが発火して表示直後のFlyoutを閉じてしまう不具合を踏んだため、
            // ここではFlyoutを閉じない(Flyout自身のライトディスミス/Closedイベントに任せる)

            border.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

            // リサイクルで別の絵文字に使い回された時に古い説明文が出ないよう毎回外す
            ToolTipService.SetToolTip(border, null);
        }

        // 矢印キーでのセル間移動のみ扱う(クリック/Enter/Spaceでのペーストは
        // EmojiCellPasteBehaviorがCommand呼び出しへ変換するため、ここでは扱わない)。
        // 検索モード等で実フォーカスを持っている場合、矢印キーはWinUI標準のXYFocus
        // (GridRootのXYFocusKeyboardNavigation)がそのまま処理してしまい、先頭付近で
        // 2つ以上先に飛ぶ不具合が再現するため、左右だけは自前のindex計算に横取りし、
        // 標準のXYFocusには渡さない
        private void EmojiCell_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key is Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Right
                && TryMoveFocusByIndex(e.Key == Windows.System.VirtualKey.Left ? -1 : 1))
            {
                e.Handled = true;
            }
        }

        // 右クリックで、肌の色バリエーションを選べるFlyoutを表示する
        private void EmojiCell_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            if (sender is not Border border || border.DataContext is not EmojiItem item || !item.HasVariants)
                return;

            e.Handled = true;
            ShowVariantsFlyout(border, item);
        }

        private void ShowVariantsFlyout(Border border, EmojiItem item)
        {
            _variantsFlyoutAnchor = border;

            if (_variantsFlyout is null)
            {
                _variantsFlyout = new Flyout();
                // 既定のFlyoutPresenterはMinWidth/Paddingを持っており、中身(バリエーション
                // 画像の行)より余分に大きく表示されてしまうため、コンテンツのサイズに
                // ぴったり合わせる
                var style = new Style(typeof(Microsoft.UI.Xaml.Controls.FlyoutPresenter));
                style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 0.0));
                style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 0.0));
                style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4)));
                _variantsFlyout.FlyoutPresenterStyle = style;
                // ライトディスミス(外側クリック/Escape/フォーカス喪失)で閉じた時も、
                // アンカーを正しく解除する
                _variantsFlyout.Closed += (_, _) => _variantsFlyoutAnchor = null;
            }
            _variantsFlyout.Content = CreateVariantsRow(item);
            _variantsFlyout.Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Top;

            // Tapped/KeyDownの処理中に直接ShowAtすると、同じクリック/キー操作の
            // ポインター解放がFlyoutのライトディスミスとして扱われ、開いた直後に
            // 閉じてしまう(実際に踏んだ不具合)。次の描画サイクルまでズラして開く
            DispatcherQueue.TryEnqueue(() =>
            {
                if (ReferenceEquals(_variantsFlyoutAnchor, border))
                    _variantsFlyout.ShowAt(border);
            });
        }

        /// <summary>
        /// ホバー時の説明用ツールチップを組み立てる。バリエーションを持つ絵文字
        /// (HasVariants)は、説明文の下にバリエーションの行(CreateVariantsRow)を添える。
        /// </summary>
        private FrameworkElement BuildTooltipContent(EmojiItem item, string? caption)
        {
            var panel = new StackPanel { Spacing = 4 };

            if (!string.IsNullOrWhiteSpace(caption))
                panel.Children.Add(new TextBlock { Text = caption });

            if (item.HasVariants)
                panel.Children.Add(CreateVariantsRow(item));

            return panel;
        }

        /// <summary>
        /// バリエーション用スプライトシート(Assets/Emoji/{kind}.variants.scale-*.png、
        /// 5列、build_emoji_data.py生成)から該当行(item.VariantRow)を1列ずつ切り出し、
        /// 横に並べたセルの行を作る(メイングリッドのセルと同じClip+平行移動のテクニック)。
        /// クリックでそのバリエーションをペーストできる(ホバー時の説明用ツールチップから
        /// 使われた場合も、ToolTip自体がクリックを受け付けないためハンドラは実害無い)。
        /// </summary>
        private StackPanel CreateVariantsRow(EmojiItem item)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };

            if (_repository is null)
                return row;

            var dpiScale = XamlRoot?.RasterizationScale ?? 1.0;
            var variantRowCount = _repository.GetVariantRowCount(item.Kind);
            var (source, sheetWidth, sheetHeight) = EmojiSpriteAssets.GetOrLoadVariants(item.Kind, variantRowCount, dpiScale);
            var cell = EmojiSpriteAssets.CellDip;

            for (var col = 0; col < item.Variants.Count; col++)
            {
                row.Children.Add(CreateVariantCell(item, source, sheetWidth, sheetHeight, cell, col, item.Variants[col]));
            }

            return row;
        }

        private Border CreateVariantCell(EmojiItem item, ImageSource source, double sheetWidth, double sheetHeight, double cell, int col, string variantGlyph)
        {
            var image = new Image
            {
                Source = source,
                Width = sheetWidth,
                Height = sheetHeight,
                Stretch = Stretch.Fill,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(-col * cell, -item.VariantRow * cell, 0, 0),
            };

            var clip = new Grid { Width = cell, Height = cell };
            clip.Clip = new Microsoft.UI.Xaml.Media.RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, cell, cell) };
            clip.Children.Add(image);

            var cellBorder = new Border
            {
                Width = cell,
                Height = cell,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Child = clip,
            };
            cellBorder.PointerEntered += (_, _) =>
            {
                _hoverBrush ??= (Brush?)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
                cellBorder.Background = _hoverBrush;
            };
            cellBorder.PointerExited += (_, _) =>
                cellBorder.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            cellBorder.Tapped += async (_, _) =>
            {
                _variantsFlyout?.Hide();
                if (ViewModel is null)
                    return;
                var variantItem = new EmojiItem { Kind = item.Kind, Text = item.Text, Emoji = variantGlyph };
                await ViewModel.PasteCommand.ExecuteAsync(variantItem);
            };

            return cellBorder;
        }
    }
}
