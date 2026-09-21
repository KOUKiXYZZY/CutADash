using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using System;

namespace CutADash.Utils
{
    /// <summary>
    /// Frame.Navigateの画面遷移アニメーションを、アプリ全体で統一するための拡張。
    ///
    /// パレットの表示・タブ切り替え・カテゴリ切り替えのたびに既定のスライド等の
    /// アニメーションが入ると、切り替えの都度画面が揺れて見づらいため、
    /// アプリ全体で常にアニメーション無し(SuppressNavigationTransitionInfo)に
    /// 統一する。以前は呼び出し箇所ごとに`new SuppressNavigationTransitionInfo()`を
    /// 個別に書いていたが、画面遷移の方針(アニメーション無しにする、という判断)を
    /// 1箇所にまとめる。
    ///
    /// 項目選択時の強調(下から浮き上がるフェードイン)はFadeInAnimationHelperが別途担う。
    /// こちらは画面/ページそのものの遷移、あちらはページ内コンテンツの強調、という役割分担。
    /// </summary>
    public static class NavigationTransitions
    {
        // 状態を持たないため、単一のインスタンスを使い回して問題ない
        private static readonly SuppressNavigationTransitionInfo Suppressed = new();

        /// <summary>アプリ全体の既定(アニメーション無し)でNavigateする。</summary>
        public static bool NavigateWithoutAnimation(this Frame frame, Type sourcePageType, object? parameter)
            => frame.Navigate(sourcePageType, parameter, Suppressed);
    }
}
