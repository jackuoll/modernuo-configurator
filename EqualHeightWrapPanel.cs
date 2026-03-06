using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;

namespace ModernUOConfigurator;

/// <summary>
/// A wrap panel where items fill the row width evenly and all items in a row share the
/// height of the tallest item. <see cref="MinItemWidth"/> controls how many columns fit.
/// </summary>
public class EqualHeightWrapPanel : Panel
{
    public static readonly StyledProperty<double> MinItemWidthProperty =
        AvaloniaProperty.Register<EqualHeightWrapPanel, double>(nameof(MinItemWidth), 420);

    static EqualHeightWrapPanel()
    {
        AffectsMeasure<EqualHeightWrapPanel>(MinItemWidthProperty);
    }

    public double MinItemWidth
    {
        get => GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    private readonly List<(int Start, int Count, double Height, double ItemWidth)> _rows = [];

    protected override Size MeasureOverride(Size availableSize)
    {
        _rows.Clear();
        var children = Children;
        if (children.Count == 0) return default;

        double availWidth = double.IsInfinity(availableSize.Width) ? double.MaxValue : availableSize.Width;
        int cols = Math.Max(1, (int)(availWidth / MinItemWidth));

        int i = 0;
        while (i < children.Count)
        {
            int rowStart = i;
            int count = Math.Min(cols, children.Count - i);
            double itemWidth = availWidth / count;

            for (int j = rowStart; j < rowStart + count; j++)
                children[j].Measure(new Size(itemWidth, double.PositiveInfinity));

            double rowHeight = 0;
            for (int j = rowStart; j < rowStart + count; j++)
                rowHeight = Math.Max(rowHeight, children[j].DesiredSize.Height);

            _rows.Add((rowStart, count, rowHeight, itemWidth));
            i += count;
        }

        double totalHeight = 0;
        foreach (var row in _rows)
            totalHeight += row.Height;

        return new Size(availWidth, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = Children;
        double y = 0;

        foreach (var (start, count, rowHeight, _) in _rows)
        {
            double itemWidth = finalSize.Width / count;
            double x = 0;
            for (int j = start; j < start + count; j++)
            {
                children[j].Arrange(new Rect(x, y, itemWidth, rowHeight));
                x += itemWidth;
            }
            y += rowHeight;
        }

        return finalSize;
    }
}
