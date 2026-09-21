using System;
using System.Threading;
using System.Threading.Tasks;

namespace CutADash.Utils
{
    /// <summary>
    /// GCのスケジューラ。個々の処理(タブ切り替え・履歴削除など)の中で同期的にGCを走らせると
    /// その分だけ操作が重くなるため、通常処理からは一切GCを呼ばず、
    /// 「ウィンドウが隠れてから一定時間後」にバックグラウンドスレッドでまとめて回収する。
    /// 隠れている間は、離れたタイミングで作られたゴミも回収できるよう一定間隔で繰り返す。
    /// </summary>
    public static class GarbageCollectionHelper
    {
        private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan RepeatInterval = TimeSpan.FromSeconds(30);
        private static CancellationTokenSource? _cts;

        /// <summary>
        /// ウィンドウが隠れた時に呼ぶ。10秒後にバックグラウンドでGCを実行し、
        /// その後は再表示されるまで30秒おきに繰り返す。
        /// 再表示されたらキャンセルされる。
        /// </summary>
        public static void OnWindowHidden()
        {
            CancelScheduled();

            var cts = new CancellationTokenSource();
            _cts = cts;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(InitialDelay, cts.Token);
                    while (true)
                    {
                         Collect();
                        await Task.Delay(RepeatInterval, cts.Token);
                    }
                }
                catch (TaskCanceledException)
                {
                    // 再表示などでキャンセルされた場合は何もしない
                }
            });
        }

        /// <summary>ウィンドウが再表示された時に呼ぶ。予約中のGCをキャンセルする。</summary>
        public static void OnWindowShown()
        {
            CancelScheduled();
        }

        private static void CancelScheduled()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        // ウィンドウが隠れている間のバックグラウンド実行なので操作の邪魔にならない。
        // そのため、圧縮までしてOSへ積極的に返すAggressiveモードを使う。
        //
        // なお計測上、このアプリのメモリはほとんどがネイティブ側(GPUドライバ・コンポジタ)で、
        // マネージドヒープは全体の1%未満しかない。GCで動かせる範囲はもともと小さい。
        private static void Collect()
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        }
    }
}
