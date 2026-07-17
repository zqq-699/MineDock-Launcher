/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace Launcher.App.Controls;

public sealed class AdaptiveTagList : Panel
{
    private static readonly Rect HiddenArrangeRect = new(0, 0, 0, 0);

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(AdaptiveTagList),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure, OnItemsSourceChanged));

    private readonly List<Border> itemTags = [];
    private readonly TextBlock overflowText;
    private readonly Border overflowTag;
    private int visibleItemCount;
    private bool showsOverflow;

    public AdaptiveTagList()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        overflowText = CreateTagText(string.Empty);
        overflowTag = CreateTag(overflowText);
    }

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var infinite = new Size(double.PositiveInfinity, availableSize.Height);
        foreach (var tag in itemTags)
            tag.Measure(infinite);

        var availableWidth = double.IsInfinity(availableSize.Width)
            ? double.PositiveInfinity
            : Math.Max(0, availableSize.Width);
        var itemWidths = itemTags.Select(tag => tag.DesiredSize.Width).ToList();
        var allItemsWidth = itemWidths.Sum();
        if (allItemsWidth <= availableWidth)
        {
            visibleItemCount = itemTags.Count;
            showsOverflow = false;
            return new Size(allItemsWidth, ResolveHeight());
        }

        visibleItemCount = CalculateVisibleItemCount(itemWidths, availableWidth, hiddenCount =>
        {
            overflowText.Text = $"+{hiddenCount}";
            overflowTag.Measure(infinite);
            return overflowTag.DesiredSize.Width;
        });
        showsOverflow = visibleItemCount < itemTags.Count;
        var hiddenCount = itemTags.Count - visibleItemCount;
        overflowText.Text = $"+{hiddenCount}";
        overflowTag.Measure(infinite);
        var visibleWidth = Math.Min(
            itemWidths.Take(visibleItemCount).Sum() + overflowTag.DesiredSize.Width,
            availableWidth);

        return new Size(visibleWidth, ResolveHeight());
    }

    internal static int CalculateVisibleItemCount(
        IReadOnlyList<double> itemWidths,
        double availableWidth,
        Func<int, double> getOverflowWidth)
    {
        if (itemWidths.Sum() <= availableWidth)
            return itemWidths.Count;

        for (var candidateCount = itemWidths.Count - 1; candidateCount >= 0; candidateCount--)
        {
            var hiddenCount = itemWidths.Count - candidateCount;
            var candidateWidth = itemWidths.Take(candidateCount).Sum() + getOverflowWidth(hiddenCount);
            if (candidateWidth <= availableWidth || candidateCount == 0)
                return candidateCount;
        }
        return 0;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0d;
        for (var index = 0; index < itemTags.Count; index++)
        {
            var tag = itemTags[index];
            if (index >= visibleItemCount)
            {
                tag.Arrange(HiddenArrangeRect);
                continue;
            }

            tag.Arrange(new Rect(x, 0, tag.DesiredSize.Width, finalSize.Height));
            x += tag.DesiredSize.Width;
        }

        if (showsOverflow)
            overflowTag.Arrange(new Rect(x, 0, overflowTag.DesiredSize.Width, finalSize.Height));
        else
            overflowTag.Arrange(HiddenArrangeRect);
        return finalSize;
    }

    private static void OnItemsSourceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is AdaptiveTagList list)
            list.RebuildItems(args.NewValue as IEnumerable);
    }

    private void RebuildItems(IEnumerable? items)
    {
        Children.Clear();
        itemTags.Clear();
        foreach (var text in items?.Cast<object?>()
                     .Select(value => value?.ToString())
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.CurrentCulture) ?? [])
        {
            var tag = CreateTag(CreateTagText(text!));
            itemTags.Add(tag);
            Children.Add(tag);
        }
        Children.Add(overflowTag);
        InvalidateMeasure();
    }

    private Border CreateTag(TextBlock text)
    {
        var tag = new Border
        {
            MinWidth = 24,
            Margin = new Thickness(0, 0, 4, 0),
            Padding = new Thickness(6, 1, 6, 1),
            Child = text
        };
        tag.SetResourceReference(Border.BackgroundProperty, "Brush.14FFFFFF");
        tag.SetResourceReference(Border.CornerRadiusProperty, "LauncherCornerRadiusSmall");
        return tag;
    }

    private static TextBlock CreateTagText(string text)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeights.Normal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        textBlock.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text.Secondary");
        return textBlock;
    }

    private double ResolveHeight() => Children
        .Cast<UIElement>()
        .Select(child => child.DesiredSize.Height)
        .DefaultIfEmpty(0)
        .Max();
}
