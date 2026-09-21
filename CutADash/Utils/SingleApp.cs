using System;
using System.Threading;

namespace CutADash.Utils
{
    /// <summary>
    /// 名前付きMutexによる多重起動防止。
    ///
    /// Microsoft.Windows.AppLifecycle.AppInstance(以前の実装)はWindows App SDKの
    /// Bootstrap初期化に依存するが、このプロジェクトは別の不安定さ(0xc000027b系クラッシュ)
    /// を避けるためBootstrapを使わずUndockedRegFreeWinRTのみで動かしている。
    /// その状態ではAppInstanceのプロセス間共有登録が機能せず、各プロセスが自分を
    /// IsCurrentだと誤認して実際に多重起動してしまうことを確認したため、
    /// OSレベルで確実にプロセス間共有されるMutexに置き換えている。
    /// </summary>
    internal sealed class SingleApp : IDisposable
    {
        private readonly Mutex _mutex;
        private bool _disposed;

        /// <summary>このプロセスが最初の(唯一の)インスタンスかどうか。</summary>
        public bool IsCurrent { get; }

        public SingleApp(string key)
        {
            _mutex = new Mutex(initiallyOwned: true, name: $"CutADash_SingleInstance_{key}", createdNew: out var createdNew);
            IsCurrent = createdNew;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            if (IsCurrent)
            {
                try { _mutex.ReleaseMutex(); }
                catch (ApplicationException) { /* 既に解放されている場合は無視 */ }
            }

            _mutex.Dispose();
        }
    }
}
