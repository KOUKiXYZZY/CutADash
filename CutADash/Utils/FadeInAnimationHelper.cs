using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Runtime.CompilerServices;

namespace CutADash.Utils
{
    /// <summary>
    /// 項目選択時などに表示コンテンツを軽く強調するための、下から浮き上がりながら
    /// フェードインするアニメーション。複数の画面(Emojiグリッド/Contents)で共用する。
    /// </summary>
    public static class FadeInAnimationHelper
    {
        private const double OffsetY = 16;
        private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(200);

        // 同じ要素に対して短時間で連続してFadeInFromBottomが呼ばれる(履歴をすばやく
        // 切り替える等)と、前回のStoryboardが終わらないうちに次のStoryboardが同じ
        // Opacity/Yプロパティを競合して操作してしまい、最終的にOpacity=0のまま
        // 止まってしまうことがあった(画像が消えたように見えるバグの原因)。
        // 要素ごとに直前のStoryboardを覚えておき、新しく始める前に必ず止める。
        private static readonly ConditionalWeakTable<UIElement, Storyboard> _runningStoryboards = new();

        public static void FadeInFromBottom(UIElement element)
        {
            if (_runningStoryboards.TryGetValue(element, out var previous))
            {
                previous.Stop();
                _runningStoryboards.Remove(element);
            }

            if (element.RenderTransform is not TranslateTransform transform)
            {
                transform = new TranslateTransform();
                element.RenderTransform = transform;
            }

            // 開始状態(透明+下に少しずらした位置)を即座にセットしてからアニメーションを開始する
            element.Opacity = 0;
            transform.Y = OffsetY;

            var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };

            var opacityAnimation = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = Duration,
                EasingFunction = easing
            };
            Storyboard.SetTarget(opacityAnimation, element);
            Storyboard.SetTargetProperty(opacityAnimation, "Opacity");

            var translateAnimation = new DoubleAnimation
            {
                From = OffsetY,
                To = 0,
                Duration = Duration,
                EasingFunction = easing
            };
            Storyboard.SetTarget(translateAnimation, transform);
            Storyboard.SetTargetProperty(translateAnimation, "Y");

            var storyboard = new Storyboard();
            storyboard.Children.Add(opacityAnimation);
            storyboard.Children.Add(translateAnimation);

            // 万一Stop()や途中終了で最終値まで届かなくても、完了時に確実に見える状態へ
            // 揃えておく(透明のまま残ってしまうのを防ぐ安全策)
            storyboard.Completed += (_, _) =>
            {
                element.Opacity = 1;
                transform.Y = 0;
                _runningStoryboards.Remove(element);
            };

            _runningStoryboards.AddOrUpdate(element, storyboard);
            storyboard.Begin();
        }
    }
}
