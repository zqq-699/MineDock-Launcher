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

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Launcher.App.Controls;

public partial class ListPageFrame : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(ListPageFrame), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TitleIconSourceProperty =
        DependencyProperty.Register(
            nameof(TitleIconSource),
            typeof(object),
            typeof(ListPageFrame),
            new PropertyMetadata(null, OnTitleIconSourceChanged));

    private static readonly DependencyPropertyKey ResolvedTitleIconSourcePropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(ResolvedTitleIconSource),
            typeof(ImageSource),
            typeof(ListPageFrame),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ResolvedTitleIconSourceProperty =
        ResolvedTitleIconSourcePropertyKey.DependencyProperty;

    public static readonly DependencyProperty IsHeaderBackButtonVisibleProperty =
        DependencyProperty.Register(nameof(IsHeaderBackButtonVisible), typeof(bool), typeof(ListPageFrame), new PropertyMetadata(false));

    public static readonly DependencyProperty HeaderBackCommandProperty =
        DependencyProperty.Register(nameof(HeaderBackCommand), typeof(ICommand), typeof(ListPageFrame), new PropertyMetadata(null));

    public static readonly DependencyProperty SearchTextProperty =
        DependencyProperty.Register(
            nameof(SearchText),
            typeof(string),
            typeof(ListPageFrame),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty IsSearchVisibleProperty =
        DependencyProperty.Register(nameof(IsSearchVisible), typeof(bool), typeof(ListPageFrame), new PropertyMetadata(true));

    public static readonly DependencyProperty SearchBoxCornerRadiusProperty =
        DependencyProperty.Register(
            nameof(SearchBoxCornerRadius),
            typeof(CornerRadius),
            typeof(ListPageFrame),
            new PropertyMetadata(new CornerRadius(8)));

    public static readonly DependencyProperty SearchLeadingContentProperty =
        DependencyProperty.Register(nameof(SearchLeadingContent), typeof(object), typeof(ListPageFrame), new PropertyMetadata(null));

    public static readonly DependencyProperty SearchLeadingContentTemplateProperty =
        DependencyProperty.Register(nameof(SearchLeadingContentTemplate), typeof(DataTemplate), typeof(ListPageFrame), new PropertyMetadata(null));

    public static readonly DependencyProperty IsSearchLeadingContentVisibleProperty =
        DependencyProperty.Register(nameof(IsSearchLeadingContentVisible), typeof(bool), typeof(ListPageFrame), new PropertyMetadata(true));

    public static readonly DependencyProperty SearchTrailingContentProperty =
        DependencyProperty.Register(nameof(SearchTrailingContent), typeof(object), typeof(ListPageFrame), new PropertyMetadata(null));

    public static readonly DependencyProperty SearchToolbarContentProperty =
        DependencyProperty.Register(nameof(SearchToolbarContent), typeof(object), typeof(ListPageFrame), new PropertyMetadata(null));

    public static readonly DependencyProperty IsSearchToolbarVisibleProperty =
        DependencyProperty.Register(nameof(IsSearchToolbarVisible), typeof(bool), typeof(ListPageFrame), new PropertyMetadata(false));

    public static readonly DependencyProperty SearchToolbarContentTemplateProperty =
        DependencyProperty.Register(nameof(SearchToolbarContentTemplate), typeof(DataTemplate), typeof(ListPageFrame), new PropertyMetadata(null));

    public static readonly DependencyProperty SearchFilterContentProperty =
        DependencyProperty.Register(nameof(SearchFilterContent), typeof(object), typeof(ListPageFrame), new PropertyMetadata(null));

    public static readonly DependencyProperty IsSearchFilterVisibleProperty =
        DependencyProperty.Register(nameof(IsSearchFilterVisible), typeof(bool), typeof(ListPageFrame), new PropertyMetadata(false));

    public static readonly DependencyProperty SearchFilterContentTemplateProperty =
        DependencyProperty.Register(nameof(SearchFilterContentTemplate), typeof(DataTemplate), typeof(ListPageFrame), new PropertyMetadata(null));

    public static readonly DependencyProperty IsListVisibleProperty =
        DependencyProperty.Register(
            nameof(IsListVisible),
            typeof(bool),
            typeof(ListPageFrame),
            new PropertyMetadata(true, OnListVisibilityChanged));

    public static readonly DependencyProperty IsProgressiveBlurEnabledProperty =
        DependencyProperty.Register(
            nameof(IsProgressiveBlurEnabled),
            typeof(bool),
            typeof(ListPageFrame),
            new PropertyMetadata(false, OnProgressiveBlurEnabledChanged));

    public static readonly DependencyProperty UseFrameScrollViewerProperty =
        DependencyProperty.Register(nameof(UseFrameScrollViewer), typeof(bool), typeof(ListPageFrame), new PropertyMetadata(true));

    public static readonly DependencyProperty OverlayContentProperty =
        DependencyProperty.Register(nameof(OverlayContent), typeof(object), typeof(ListPageFrame), new PropertyMetadata(null));

    public static readonly DependencyProperty ListContentProperty =
        DependencyProperty.Register(nameof(ListContent), typeof(object), typeof(ListPageFrame), new PropertyMetadata(null));

    public static readonly DependencyProperty FloatingContentProperty =
        DependencyProperty.Register(nameof(FloatingContent), typeof(object), typeof(ListPageFrame), new PropertyMetadata(null));

    public ListPageFrame()
        : this(null, null)
    {
    }

    internal ListPageFrame(
        ProgressiveBlurEffectFactory? effectFactory,
        ProgressiveBlurEffectAttacher? effectAttacher)
    {
        InitializeComponent();
        progressiveBlurController = new ProgressiveBlurBandController(
            new ProgressiveBlurVisualParts(
                this,
                PART_ListLayer,
                PART_ListVisualSource,
                PART_DirectListHost,
                PART_BlurBandViewport,
                PART_BlurBandUpscaleHost,
                PART_BlurBandUpscaleTransform,
                PART_BlurBandHorizontalHost,
                PART_BlurBandVerticalHost,
                PART_BlurBandBrush),
            () => IsVisible && IsListVisible && IsProgressiveBlurEnabled,
            effectFactory,
            effectAttacher);
        Loaded += ListPageFrame_Loaded;
        Unloaded += ListPageFrame_Unloaded;
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public object? TitleIconSource
    {
        get => GetValue(TitleIconSourceProperty);
        set => SetValue(TitleIconSourceProperty, value);
    }

    public ImageSource? ResolvedTitleIconSource
    {
        get => (ImageSource?)GetValue(ResolvedTitleIconSourceProperty);
        private set => SetValue(ResolvedTitleIconSourcePropertyKey, value);
    }

    public bool IsHeaderBackButtonVisible
    {
        get => (bool)GetValue(IsHeaderBackButtonVisibleProperty);
        set => SetValue(IsHeaderBackButtonVisibleProperty, value);
    }

    private static void OnTitleIconSourceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is ListPageFrame frame)
            frame.ResolvedTitleIconSource = IconSourceImageLoader.TryLoad(args.NewValue);
    }

    public ICommand? HeaderBackCommand
    {
        get => (ICommand?)GetValue(HeaderBackCommandProperty);
        set => SetValue(HeaderBackCommandProperty, value);
    }

    public string SearchText
    {
        get => (string)GetValue(SearchTextProperty);
        set => SetValue(SearchTextProperty, value);
    }

    public bool IsSearchVisible
    {
        get => (bool)GetValue(IsSearchVisibleProperty);
        set => SetValue(IsSearchVisibleProperty, value);
    }

    public CornerRadius SearchBoxCornerRadius
    {
        get => (CornerRadius)GetValue(SearchBoxCornerRadiusProperty);
        set => SetValue(SearchBoxCornerRadiusProperty, value);
    }

    public object? SearchLeadingContent
    {
        get => GetValue(SearchLeadingContentProperty);
        set => SetValue(SearchLeadingContentProperty, value);
    }

    public DataTemplate? SearchLeadingContentTemplate
    {
        get => (DataTemplate?)GetValue(SearchLeadingContentTemplateProperty);
        set => SetValue(SearchLeadingContentTemplateProperty, value);
    }

    public bool IsSearchLeadingContentVisible
    {
        get => (bool)GetValue(IsSearchLeadingContentVisibleProperty);
        set => SetValue(IsSearchLeadingContentVisibleProperty, value);
    }

    public object? SearchTrailingContent
    {
        get => GetValue(SearchTrailingContentProperty);
        set => SetValue(SearchTrailingContentProperty, value);
    }

    public object? SearchToolbarContent
    {
        get => GetValue(SearchToolbarContentProperty);
        set => SetValue(SearchToolbarContentProperty, value);
    }

    public bool IsSearchToolbarVisible
    {
        get => (bool)GetValue(IsSearchToolbarVisibleProperty);
        set => SetValue(IsSearchToolbarVisibleProperty, value);
    }

    public DataTemplate? SearchToolbarContentTemplate
    {
        get => (DataTemplate?)GetValue(SearchToolbarContentTemplateProperty);
        set => SetValue(SearchToolbarContentTemplateProperty, value);
    }

    public object? SearchFilterContent
    {
        get => GetValue(SearchFilterContentProperty);
        set => SetValue(SearchFilterContentProperty, value);
    }

    public bool IsSearchFilterVisible
    {
        get => (bool)GetValue(IsSearchFilterVisibleProperty);
        set => SetValue(IsSearchFilterVisibleProperty, value);
    }

    public DataTemplate? SearchFilterContentTemplate
    {
        get => (DataTemplate?)GetValue(SearchFilterContentTemplateProperty);
        set => SetValue(SearchFilterContentTemplateProperty, value);
    }

    public bool IsListVisible
    {
        get => (bool)GetValue(IsListVisibleProperty);
        set => SetValue(IsListVisibleProperty, value);
    }

    public bool IsProgressiveBlurEnabled
    {
        get => (bool)GetValue(IsProgressiveBlurEnabledProperty);
        set => SetValue(IsProgressiveBlurEnabledProperty, value);
    }

    public bool UseFrameScrollViewer
    {
        get => (bool)GetValue(UseFrameScrollViewerProperty);
        set => SetValue(UseFrameScrollViewerProperty, value);
    }

    public object? OverlayContent
    {
        get => GetValue(OverlayContentProperty);
        set => SetValue(OverlayContentProperty, value);
    }

    public object? ListContent
    {
        get => GetValue(ListContentProperty);
        set => SetValue(ListContentProperty, value);
    }

    public object? FloatingContent
    {
        get => GetValue(FloatingContentProperty);
        set => SetValue(FloatingContentProperty, value);
    }

    public ScrollViewer ScrollViewer => PART_ScrollViewer;

    internal FrameworkElement ListLayerElement => PART_ListLayer;

    internal FrameworkElement ListVisualSourceElement => PART_ListVisualSource;

    internal FrameworkElement DirectListHostElement => PART_DirectListHost;

    internal FrameworkElement BlurBandViewportElement => PART_BlurBandViewport;

    internal FrameworkElement BlurBandUpscaleHostElement => PART_BlurBandUpscaleHost;

    internal ScaleTransform BlurBandUpscaleTransform => PART_BlurBandUpscaleTransform;

    internal FrameworkElement BlurBandHorizontalHostElement => PART_BlurBandHorizontalHost;

    internal FrameworkElement BlurBandVerticalHostElement => PART_BlurBandVerticalHost;

    internal VisualBrush BlurBandBrush => PART_BlurBandBrush;

    internal FrameworkElement HeaderOverlayElement => PART_HeaderOverlay;

    internal FrameworkElement HeaderTitleRowElement => PART_HeaderTitleRow;

    private readonly ProgressiveBlurBandController? progressiveBlurController;

    private static void OnListVisibilityChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is ListPageFrame frame)
            frame.progressiveBlurController?.Update();
    }

    private static void OnProgressiveBlurEnabledChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not ListPageFrame frame)
            return;

        var becameEnabled = !(bool)e.OldValue && (bool)e.NewValue;
        frame.progressiveBlurController?.OnEnabledChanged(becameEnabled);
    }

    private void ListPageFrame_Loaded(object sender, RoutedEventArgs e)
    {
        progressiveBlurController?.OnLoaded();
    }

    private void ListPageFrame_Unloaded(object sender, RoutedEventArgs e)
    {
        progressiveBlurController?.OnUnloaded();
    }
}
