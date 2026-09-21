using CommunityToolkit.Mvvm.ComponentModel;
using CutADash.Models;
using CutADash.Repositories;
using Preferences;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CutADash.ViewModels
{
    public partial class ClipboardListViewModel : ObservableObject, IClipboardItemListViewModel
    {
        private readonly ClipboardRepository _repository;

        // 検索中に、古い検索結果が後から返ってきてSearchTextと食い違うのを防ぐためのトークン
        private int _searchToken;

        // Historyとして保持しておく実データ(SQLiteへの読み書きの元になる一覧)
        public ObservableCollection<ClipboardItem> Items { get; }
            = new();

        // ListViewが実際にバインドする一覧。検索中でなければItemsと同じ内容、
        // 検索中はSearchTextにヒットした項目(DB全体から検索)を表示する
        public ObservableCollection<ClipboardItem> DisplayItems { get; }
            = new();

        // ListViewの選択項目
        [ObservableProperty]
        private ClipboardItem? selectedItem = null;

        // 履歴が1件も無いときに「なし」を表示するためのフラグ
        [ObservableProperty]
        private bool isEmpty;

        // Historyとして保持しておける件数の上限。PreferenceWindowで設定でき、
        // 超えた分は古い項目からDequeue(削除)する
        [ObservableProperty]
        private int maxHistoryCount;

        // Search欄の入力内容。空ならItems、それ以外はDB全体を検索した結果をDisplayItemsに反映する
        [ObservableProperty]
        private string searchText = string.Empty;

        // 検索結果が返ってくるまでの間だけtrueにし、ListFrame側で待機アニメーションを表示させる
        [ObservableProperty]
        private bool isSearching;

        public ClipboardListViewModel(ClipboardRepository repository)
        {
            _repository = repository;

            // Itemsの増減に合わせてIsEmptyを更新し、検索中でなければDisplayItemsにも反映する
            Items.CollectionChanged += (s, e) =>
            {
                IsEmpty = Items.Count == 0;
                if (string.IsNullOrWhiteSpace(SearchText))
                    SyncDisplayItemsFromItems();
            };

            maxHistoryCount = PreferencesGateway.GetMaxHistoryCount();

            // 設定画面で変更されたら即座に反映する(このViewModelはDIシングルトンでアプリと
            // 同じ寿命のため、購読解除はしない)。下げた場合の即座のDequeueは
            // OnMaxHistoryCountChangedが担う
            PreferencesGateway.MaxHistoryCountChanged += value => MaxHistoryCount = value;

            _ = LoadFromDatabaseAsync();
        }
        

        partial void OnSelectedItemChanged(ClipboardItem? value) {
            if (value == null)
                return;
#if DEBUG
            Debug.WriteLine($"選択: {value.Text}");
#endif
        }

        // 上限が変更されたら、表示中の一覧(メモリ上)をすぐにDequeueするだけでなく、
        // DB上のデータ自体も新しい上限に合わせて切り詰める
        partial void OnMaxHistoryCountChanged(int value)
        {
            TrimToMaxHistoryCount();
            _ = _repository.TrimToCountAsync(value);
        }

        partial void OnSearchTextChanged(string value)
        {
            _ = RunSearchAsync(value);
        }

        /// <summary>
        /// 起動時にDB(clipboard_history.db)から直近MaxHistoryCount件を読み込んで表示する。
        /// </summary>
        private async Task LoadFromDatabaseAsync()
        {
            var items = await _repository.GetAllAsync(MaxHistoryCount);

            // GetAllAsyncは新しい順で返るので、そのままAddしていけば順序が保たれる
            foreach (var item in items)
                Items.Add(item);
        }

        private async Task RunSearchAsync(string query)
        {
            var token = ++_searchToken;

            if (string.IsNullOrWhiteSpace(query))
            {
                IsSearching = false;
                SyncDisplayItemsFromItems();
                return;
            }

            IsSearching = true;
            var startedAt = DateTime.UtcNow;
            try
            {
                var results = await _repository.SearchAsync(query);

                // 検索中にSearchTextがさらに変わっていたら、この結果は古いので捨てる
                if (token != _searchToken)
                    return;

                DisplayItems.Clear();
                foreach (var item in results)
                    DisplayItems.Add(item);
            }
            finally
            {
                // 自分より後の検索が既に始まっていたら、そちらがIsSearchingの管理を引き継ぐ
                if (token == _searchToken)
                {
                    // FTS5のローカル検索はほぼ瞬時に終わるため、そのままだと待機アニメーションが
                    // 1フレームも表示されない。最低表示時間を設けて視認できるようにする
                    var elapsed = DateTime.UtcNow - startedAt;
                    var minDuration = TimeSpan.FromMilliseconds(200);
                    if (elapsed < minDuration)
                        await Task.Delay(minDuration - elapsed);

                    if (token == _searchToken)
                        IsSearching = false;
                }
            }
        }

        private void SyncDisplayItemsFromItems()
        {
            DisplayItems.Clear();
            foreach (var item in Items)
                DisplayItems.Add(item);
        }

        /// <summary>
        /// 新しい履歴項目を先頭に追加し、DBへも書き込む。同じTextの項目が既にあれば、
        /// それを取り除いてから先頭に追加する(結果的に一番上へ移動する)。
        /// MaxHistoryCountを超えた分は末尾(最も古い項目)からDequeueして取り除き、
        /// DB上のデータ・画像ファイル(ImageFilePath/ThumbnailFilePath)も削除する。
        /// 「画像を履歴に登録しない」設定が有効な場合、画像はOSクリップボードには残るが
        /// Historyには追加しない。
        /// </summary>
        public void Enqueue(ClipboardItem item)
        {
            if (item.Type == ClipboardContentType.Image && PreferencesGateway.IsImageHistoryDisabled())
                return;

            // 対応フォーマットが無い(表示するテンプレートも無い)ため、履歴には登録しない
            if (item.Type == ClipboardContentType.Unknown)
                return;

            if (!string.IsNullOrEmpty(item.Text))
            {
                // 同じ内容とみなす条件はDB側(ClipboardRepository.InsertOrBumpAsync)と揃える。
                // Textだけで判定すると、書式付きで保存してあった項目が、同じ文面を
                // プレーンテキストでコピーしただけで消えてしまう
                var existing = Items.FirstOrDefault(x => x.Text == item.Text && x.Rtf == item.Rtf);
                if (existing is not null)
                    Items.Remove(existing);
            }

            Items.Insert(0, item);
            TrimToMaxHistoryCount();

            _ = PersistAsync(item);

            if (_selectNextEnqueuedItem)
            {
                _selectNextEnqueuedItem = false;
                SelectedItem = item;
            }
        }

        // Encode/Decode結果など、CutADash自身がOSクリップボードへ書き戻した内容が
        // 履歴に追加された直後、その項目を自動的に選択状態にしたい場合に使う。
        // 通常の外部アプリからのコピーでは選択状態を勝手に変えたくないため、
        // 次の1回だけ効くフラグにしてある(ClipboardMonitor.SuppressNextChangeと同じ考え方)
        private bool _selectNextEnqueuedItem;

        public void SelectNextEnqueuedItem()
        {
            _selectNextEnqueuedItem = true;
        }

        /// <summary>DBへの登録を行う。</summary>
        private async Task PersistAsync(ClipboardItem item)
        {
            await _repository.InsertOrBumpAsync(item);
        }

        /// <summary>末尾(最も古い)の履歴項目を1件取り除く。</summary>
        private ClipboardItem? Dequeue()
        {
            if (Items.Count == 0)
                return null;

            var oldest = Items[Items.Count - 1];
            Items.RemoveAt(Items.Count - 1);
            return oldest;
        }

        // 末尾からあふれた項目をメモリ上から取り除くと同時に、DB上のデータと
        // 画像ファイル(ImageFilePath/ThumbnailFilePath、あれば)も削除する
        private void TrimToMaxHistoryCount()
        {
            while (Items.Count > MaxHistoryCount && Items.Count > 0)
            {
                var removed = Dequeue();
                if (removed is not null)
                    _ = _repository.DeleteAsync(removed);
            }
        }

        /// <summary>指定した1件を削除する(画面上・DBの両方)。</summary>
        public async Task DeleteAsync(ClipboardItem item)
        {
            Items.Remove(item);
            DisplayItems.Remove(item);

            await _repository.DeleteAsync(item);
        }

        /// <summary>
        /// 既存の履歴項目を一覧の先頭へ移動する。ペーストした項目を「一番上に出す」ために使う。
        /// Text/Image/リッチテキストいずれの種別でも、内容を書き換えずTimestampだけ更新する。
        /// </summary>
        public async Task BumpToTopAsync(ClipboardItem item)
        {
            // お気に入り等、この一覧の項目ではないものが渡されても何もしない
            if (!Items.Contains(item))
                return;

            if (Items.Count > 0 && Items[0] == item)
                return;

            item.Timestamp = DateTime.Now;

            Items.Remove(item);
            Items.Insert(0, item);

            await _repository.TouchAsync(item.Id, item.Timestamp);
        }

        /// <summary>履歴を全件削除する(画面上・DBの両方)。</summary>
        public async Task ClearAllAsync()
        {
            Items.Clear();
            await _repository.DeleteAllAsync();
        }

        /// <summary>
        /// Contentsで編集したテキスト/リッチテキストを、DBと画面上のItemへ反映する。
        /// 検索結果(DisplayItems)はSearchAsyncで読み込んだ別インスタンスのため、
        /// Itemsに同じ参照が入っているとは限らない。ここはItemsに載っているかどうかに
        /// 関わらず、渡されたitem自身のIdを基準にDBへ保存する
        /// (以前はItems.Containsで絞っていたため、検索結果から開いた項目を編集しても
        /// 保存が無言で失敗していた)。
        /// </summary>
        public async Task UpdateContentAsync(ClipboardItem item, string? text, string? rtf, string? html)
        {
            item.Text = text;
            item.Rtf = rtf;
            item.Html = html;

            await _repository.UpdateContentAsync(item.Id, text, rtf, html);
        }

        /// <summary>Shape/Image項目の表示名を変更する(画面上のitem自身とDBの両方)。</summary>
        public async Task RenameItemAsync(ClipboardItem item, string? name)
        {
            item.Name = name;
            // 検索(FTS/LIKE)がTextしか見ないため、名前が検索に掛かるようミラーする
            item.Text = name;
            await _repository.RenameItemAsync(item.Id, name);
        }

        // GCは個々の処理では行わない。ウィンドウが隠れて10秒後に
        // GarbageCollectionHelperがバックグラウンドでまとめて実行する
    }
}
