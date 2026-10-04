using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace UiLibrary
{
    /// <summary>
    /// 要素を左から右へ並べ、幅に収まらなくなったら次の行へ折り返すパネル。
    /// WinUI 3の標準には無いため、ChipInputのチップの並びのために持っている。
    /// </summary>
    public sealed class WrapPanel : Panel
    {
        public static readonly DependencyProperty HorizontalSpacingProperty = DependencyProperty.Register(
            nameof(HorizontalSpacing), typeof(double), typeof(WrapPanel),
            new PropertyMetadata(0.0, (d, _) => ((WrapPanel)d).InvalidateMeasure()));

        public static readonly DependencyProperty VerticalSpacingProperty = DependencyProperty.Register(
            nameof(VerticalSpacing), typeof(double), typeof(WrapPanel),
            new PropertyMetadata(0.0, (d, _) => ((WrapPanel)d).InvalidateMeasure()));

        /// <summary>同じ行の要素同士の間隔。</summary>
        public double HorizontalSpacing
        {
            get => (double)GetValue(HorizontalSpacingProperty);
            set => SetValue(HorizontalSpacingProperty, value);
        }

        /// <summary>行同士の間隔。</summary>
        public double VerticalSpacing
        {
            get => (double)GetValue(VerticalSpacingProperty);
            set => SetValue(VerticalSpacingProperty, value);
        }

        protected override Windows.Foundation.Size MeasureOverride(Windows.Foundation.Size availableSize)
        {
            double x = 0, rowHeight = 0, totalHeight = 0, maxWidth = 0;

            foreach (var child in Children)
            {
                child.Measure(new Windows.Foundation.Size(availableSize.Width, double.PositiveInfinity));
                var size = child.DesiredSize;

                // 行の先頭でなく、入りきらない時だけ折り返す
                if (x > 0 && x + size.Width > availableSize.Width)
                {
                    totalHeight += rowHeight + VerticalSpacing;
                    x = 0;
                    rowHeight = 0;
                }

                x += size.Width + HorizontalSpacing;
                rowHeight = Math.Max(rowHeight, size.Height);
                maxWidth = Math.Max(maxWidth, x - HorizontalSpacing);
            }

            totalHeight += rowHeight;
            return new Windows.Foundation.Size(
                double.IsInfinity(availableSize.Width) ? maxWidth : Math.Min(maxWidth, availableSize.Width),
                totalHeight);
        }

        protected override Windows.Foundation.Size ArrangeOverride(Windows.Foundation.Size finalSize)
        {
            double x = 0, y = 0, rowHeight = 0;

            foreach (var child in Children)
            {
                var size = child.DesiredSize;

                if (x > 0 && x + size.Width > finalSize.Width)
                {
                    y += rowHeight + VerticalSpacing;
                    x = 0;
                    rowHeight = 0;
                }

                child.Arrange(new Windows.Foundation.Rect(x, y, size.Width, size.Height));

                x += size.Width + HorizontalSpacing;
                rowHeight = Math.Max(rowHeight, size.Height);
            }

            return finalSize;
        }
    }
}
