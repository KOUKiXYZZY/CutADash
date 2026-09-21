using System;

namespace Common.Infra.Win32
{
    /// <summary>
    /// 1つのショートカットキー(例:履歴を開く、Favoriteを開く)の登録・変更・削除をまとめる。
    /// 起動時の初期値読み込みだけは具体的なストア実装(例: HotKeySettingsStore)に依存させず、
    /// 呼び出し側から関数として注入してもらう。一方、変更後の保存・削除の永続化は
    /// このクラスの責務ではない(Commonプロジェクトはpreferences非依存に保ちたいため)。
    /// Update/Removeはあくまでメモリ上の登録状態とWin32のホットキー登録だけを変更するので、
    /// 呼び出し側(PreferenceViewModel等)がそれと合わせて自分でストアへの保存/削除を行うこと。
    /// 複数のショートカットを扱う場合は、名前(Name)を変えて複数インスタンスを作る。
    /// </summary>
    public class HotKeyService : IDisposable
    {
        private readonly HotKeyMonitor _monitor;
        private readonly HotKeyMonitor.HotKeyCallback _callback;
        private readonly Func<string, HotKeyDefinition?, HotKeyDefinition?> _loadOrDefault;

        public string Name { get; }

        /// <summary>現在登録されているショートカット。未設定ならnull。</summary>
        public HotKeyDefinition? Current { get; private set; }

        /// <summary>
        /// HotKeyMonitorはWM_HOTKEYを受け取るためdispatcherにつき1つで十分なので、
        /// アプリ全体で共有するシングルトンインスタンスをコンストラクタで受け取る。
        /// </summary>
        public HotKeyService(
            string name,
            HotKeyMonitor monitor,
            HotKeyDefinition? defaultDefinition,
            HotKeyMonitor.HotKeyCallback callback,
            Func<string, HotKeyDefinition?, HotKeyDefinition?> loadOrDefault)
        {
            Name = name;
            _monitor = monitor;
            _callback = callback;
            _loadOrDefault = loadOrDefault;

            Current = _loadOrDefault(name, defaultDefinition);
            if (Current is not null)
            {
                _monitor.Register(Current, _callback);
            }
        }

        /// <summary>
        /// 既存の登録を解除し、新しい定義で登録し直して設定を保存する。
        /// </summary>
        public void Update(HotKeyDefinition newDefinition)
        {
            if (Current is not null)
            {
                _monitor.Unregister(Current, _callback);
            }

            Current = newDefinition;
            _monitor.Register(Current, _callback);
        }

        /// <summary>
        /// ショートカットの登録を解除し、未設定状態にする。
        /// </summary>
        public void Remove()
        {
            if (Current is not null)
            {
                _monitor.Unregister(Current, _callback);
                Current = null;
            }
        }

        /// <summary>
        /// monitorは共有インスタンスなのでDisposeしない。自分が登録したホットキーだけ解除する。
        /// </summary>
        public void Dispose()
        {
            if (Current is not null)
            {
                _monitor.Unregister(Current, _callback);
            }
        }
    }
}
