using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CutADash.Models;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;

namespace SequentialPaste
{
    /// <summary>
    /// シーケンシャルペーストのキュー本体。History/Favoriteから積まれた項目を、
    /// 先頭から順に「次にペーストする項目」(NextIndex)として選択状態にしながら貼り付けていく。
    /// 以前は貼り付けるたびにキューから取り除いていたが、複数回同じキューを使い回したい
    /// 場合に不便なため、キューには残したまま選択(NextIndex)だけを次へ進める方式に変えた。
    /// DB永続化はせず、アプリ実行中のみメモリ上に持つ(アプリを再起動するとキューは空になる)。
    ///
    /// 位置はItemsの中の「インデックス」で管理する(項目の参照から
    /// Items.IndexOf(item)で逆算しない)。同じ文字列/同じ参照の項目を複数回キューへ
    /// 積んだ場合、IndexOfは常に最初に見つかった方の位置を返してしまい、ペーストするたびに
    /// 選択が前の位置へ引き戻される(行ったり来たりする)不具合があったため。
    ///
    /// 実際にOSクリップボードへ書き込みCtrl+Vを送る処理は、CutADash本体プロジェクト内部
    /// (internal)のクラスに依存しており、このプロジェクトから直接は呼べない(呼べるように
    /// すると参照が循環する)。そのため<see cref="PasteToForeground"/>へCutADash本体側から
    /// 実処理を注入してもらう、HotKeyServiceと同じコールバック注入の形にしている。
    ///
    /// システム全体でのCtrl+V検知(WH_KEYBOARD_LL)自体はWindow/UIに依存しないため、
    /// SequentialPasteHotkeyWatcherの生成・開始/停止もこちらで持つ
    /// (View(SequentialPasteWindow)はStartStopButtonのCommand経由で呼ぶだけ)。
    /// </summary>
    public partial class SequentialPasteQueueViewModel : ObservableObject
    {
        public ObservableCollection<ClipboardItem> Items { get; } = new();

        /// <summary>
        /// 実際にOSクリップボードへ書き込み、フォアグラウンドウィンドウへペーストする処理。
        /// CutADash本体(App.xaml.cs)が起動時に注入する。
        /// </summary>
        public Func<ClipboardItem, Task>? PasteToForeground { get; set; }

        private readonly SequentialPasteHotkeyWatcher _hotkeyWatcher = new();

        /// <summary>次にペーストされる項目のインデックス。ListViewのSelectedIndexとTwoWay
        /// バインドし、見やすいよう選択行を青背景で表示する(ユーザーが直接クリックして
        /// 選び直すことも可能)。参照ではなくインデックスで管理することで、同じ内容/同じ参照の
        /// 項目が複数キューにあっても常に正しい位置を指し続ける。</summary>
        [ObservableProperty]
        private int nextIndex;

        /// <summary>NextIndexが指す項目。範囲外(キューが空・末尾まで進んだ)ならnull。</summary>
        public ClipboardItem? NextItem => NextIndex >= 0 && NextIndex < Items.Count ? Items[NextIndex] : null;

        [ObservableProperty]
        private bool isWatching;

        /// <summary>末尾まで貼り付け終えた時に発火する(View側でダイアログを出すために使う)。</summary>
        public event Action? QueueCompleted;

        public bool HasItems => Items.Count > 0;

        public bool IsEmpty => Items.Count == 0;

        /// <summary>キューの件数。表示用の文言("件"等)の組み立てはView側の責務とし、
        /// ここでは生の値だけを持つ。</summary>
        public int Count => Items.Count;

        public SequentialPasteQueueViewModel()
        {
            Items.CollectionChanged += (_, _) =>
            {
                EnsureNextIndexValid();
                OnPropertyChanged(nameof(HasItems));
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(Count));
            };

            _hotkeyWatcher.HasItems = () => Items.Count > 0;
            _hotkeyWatcher.PasteRequested += async () => await PasteNextAsync();
        }

        partial void OnNextIndexChanged(int value) => OnPropertyChanged(nameof(NextItem));

        public void Enqueue(ClipboardItem item)
        {
            Items.Add(item);
            EnsureNextIndexValid();
        }

        [RelayCommand]
        private void Remove(ClipboardItem item)
        {
            var index = Items.IndexOf(item);
            if (index < 0)
                return;

            Items.RemoveAt(index);

            // 削除した項目より前(自分自身を含む)ならその分だけ選択位置を詰める。
            // 後ろの項目を削除した場合は選択位置はそのままでよい
            if (index <= NextIndex && NextIndex > 0)
                NextIndex--;

            EnsureNextIndexValid();
        }

        [RelayCommand]
        private void Clear()
        {
            Items.Clear();
            NextIndex = 0;
        }

        [RelayCommand]
        private void ToggleWatching()
        {
            if (_hotkeyWatcher.IsRunning)
                StopWatching();
            else
                StartWatching();
        }

        private void StartWatching()
        {
            // 開始した時点で先頭のアイテムから貼り付けられるようにし、
            // 前回の実行で付いたチェックマークもリセットする
            NextIndex = 0;
            foreach (var item in Items)
                item.IsPasted = false;

            _hotkeyWatcher.Start();
            IsWatching = _hotkeyWatcher.IsRunning;
        }

        private void StopWatching()
        {
            _hotkeyWatcher.Stop();
            IsWatching = _hotkeyWatcher.IsRunning;
        }

        /// <summary>
        /// NextIndexが指す項目を1件ペーストし、成功・失敗に関わらずキューには残したまま、
        /// 選択(NextIndex)だけを1つ下(次の項目)へ進める。末尾まで進んだら監視を自動的に
        /// 止める(押しっぱなしで通常のCtrl+Vが握りつぶされ続けたまま放置しないため)。
        /// キューが空、またはペースト処理が未設定(起動シーケンスの都合等)の場合は何もしない。
        /// </summary>
        public async Task PasteNextAsync()
        {
            Debug.WriteLine($"[SeqPaste][Queue] PasteNextAsync開始 Items.Count={Items.Count} PasteToForeground={(PasteToForeground is null ? "null" : "設定済み")}");

            if (Items.Count == 0 || PasteToForeground is null)
                return;

            EnsureNextIndexValid();
            var item = Items[NextIndex];

            try
            {
                await PasteToForeground(item);
                Debug.WriteLine("[SeqPaste][Queue] PasteToForeground完了");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SeqPaste][Queue] PasteToForegroundで例外: {ex}");
            }

            // 貼り付け結果に関わらず、行った項目には印を付ける(見た目のチェックマーク用)
            item.IsPasted = true;

            // ペースト結果に関わらず、常に1つ下へ進むだけ(参照の一致で位置を探し直さない)。
            // 末尾まで進んだらそれ以上は進めず、末尾を選んだまま監視を自動的に止め、
            // 末尾に到達したことをViewへ通知する(ダイアログ表示用)
            var wasLast = NextIndex >= Items.Count - 1;
            NextIndex = Math.Min(NextIndex + 1, Items.Count - 1);

            if (wasLast)
            {
                StopWatching();
                QueueCompleted?.Invoke();
            }
        }

        /// <summary>
        /// Items変更後もNextIndexが妥当な範囲([0, Items.Count-1])を指し続けるようにする。
        /// キューが空なら0にする(NextItemは範囲外としてnullを返す)。
        /// </summary>
        private void EnsureNextIndexValid()
        {
            if (Items.Count == 0)
            {
                NextIndex = 0;
                return;
            }

            if (NextIndex < 0)
                NextIndex = 0;
            else if (NextIndex >= Items.Count)
                NextIndex = Items.Count - 1;
        }
    }
}
