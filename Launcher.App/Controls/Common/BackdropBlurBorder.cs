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
using System.Windows.Media;
using System.Windows.Media.Effects;
using Serilog;

namespace Launcher.App.Controls;

[TemplatePart(Name = BlurLayerPartName, Type = typeof(Border))]
public sealed class BackdropBlurBorder : ContentControl
{
    internal const string BlurLayerPartName = "PART_BlurLayer";
    private const double BlurOverscanFactor = 1.5d;

    public static readonly DependencyProperty SourceElementProperty =
        DependencyProperty.Register(
            nameof(SourceElement),
            typeof(FrameworkElement),
            typeof(BackdropBlurBorder),
            new FrameworkPropertyMetadata(null, OnBackdropSourceChanged));

    public static readonly DependencyProperty BlurRadiusProperty =
        DependencyProperty.Register(
            nameof(BlurRadius),
            typeof(double),
            typeof(BackdropBlurBorder),
            new FrameworkPropertyMetadata(
                42d,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnBlurRadiusChanged),
            IsNonNegativeFiniteDouble);

    public static readonly DependencyProperty IsBlurEnabledProperty =
        DependencyProperty.Register(
            nameof(IsBlurEnabled),
            typeof(bool),
            typeof(BackdropBlurBorder),
            new FrameworkPropertyMetadata(true, OnBackdropPresentationChanged));

    public static readonly DependencyProperty BaseBrushProperty =
        DependencyProperty.Register(
            nameof(BaseBrush),
            typeof(Brush),
            typeof(BackdropBlurBorder),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TintBrushProperty =
        DependencyProperty.Register(
            nameof(TintBrush),
            typeof(Brush),
            typeof(BackdropBlurBorder),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty OverlayBrushProperty =
        DependencyProperty.Register(
            nameof(OverlayBrush),
            typeof(Brush),
            typeof(BackdropBlurBorder),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BlurRenderingBiasProperty =
        DependencyProperty.Register(
            nameof(BlurRenderingBias),
            typeof(RenderingBias),
            typeof(BackdropBlurBorder),
            new FrameworkPropertyMetadata(RenderingBias.Performance, FrameworkPropertyMetadataOptions.AffectsRender),
            IsRenderingBiasValid);

    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(
            nameof(CornerRadius),
            typeof(CornerRadius),
            typeof(BackdropBlurBorder),
            new FrameworkPropertyMetadata(default(CornerRadius), FrameworkPropertyMetadataOptions.AffectsRender),
            IsCornerRadiusValid);

    private Border? blurLayer;
    private VisualBrush? backdropBrush;
    private Rect lastViewbox = Rect.Empty;
    private Rect lastViewport = Rect.Empty;
    private bool isLoaded;
    private bool isRenderTrackingActive;
    private bool recursiveSourceWarningLogged;

    public BackdropBlurBorder()
    {
        Focusable = false;
        IsTabStop = false;
        Loaded += BackdropBlurBorder_Loaded;
        Unloaded += BackdropBlurBorder_Unloaded;
    }

    public FrameworkElement? SourceElement
    {
        get => (FrameworkElement?)GetValue(SourceElementProperty);
        set => SetValue(SourceElementProperty, value);
    }

    public double BlurRadius
    {
        get => (double)GetValue(BlurRadiusProperty);
        set => SetValue(BlurRadiusProperty, value);
    }

    public bool IsBlurEnabled
    {
        get => (bool)GetValue(IsBlurEnabledProperty);
        set => SetValue(IsBlurEnabledProperty, value);
    }

    public Brush? BaseBrush
    {
        get => (Brush?)GetValue(BaseBrushProperty);
        set => SetValue(BaseBrushProperty, value);
    }

    public Brush? TintBrush
    {
        get => (Brush?)GetValue(TintBrushProperty);
        set => SetValue(TintBrushProperty, value);
    }

    public Brush? OverlayBrush
    {
        get => (Brush?)GetValue(OverlayBrushProperty);
        set => SetValue(OverlayBrushProperty, value);
    }

    public RenderingBias BlurRenderingBias
    {
        get => (RenderingBias)GetValue(BlurRenderingBiasProperty);
        set => SetValue(BlurRenderingBiasProperty, value);
    }

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    internal VisualBrush? BackdropBrush => backdropBrush;

    internal BlurEffect? BackdropEffect => blurLayer?.Effect as BlurEffect;

    internal bool IsBackdropActive => blurLayer?.Visibility == Visibility.Visible;

    internal bool IsRenderTrackingActive => isRenderTrackingActive;

    internal double BlurOverscan => CalculateBlurOverscan(BlurRadius);

    public override void OnApplyTemplate()
    {
        if (backdropBrush is not null)
            backdropBrush.Visual = null;

        base.OnApplyTemplate();

        blurLayer = GetTemplateChild(BlurLayerPartName) as Border;
        var templateBrush = blurLayer?.Background as VisualBrush;
        if (blurLayer is not null && templateBrush is not null)
        {
            backdropBrush = templateBrush.IsFrozen
                ? templateBrush.CloneCurrentValue()
                : templateBrush;
            if (!ReferenceEquals(backdropBrush, templateBrush))
                blurLayer.Background = backdropBrush;
            backdropBrush.ViewboxUnits = BrushMappingMode.Absolute;
            backdropBrush.ViewportUnits = BrushMappingMode.Absolute;
            backdropBrush.TileMode = TileMode.FlipXY;
        }
        else
        {
            backdropBrush = null;
        }
        UpdateBlurLayerOverscan();
        lastViewbox = Rect.Empty;
        lastViewport = Rect.Empty;
        RefreshBackdrop();
    }

    internal void RefreshBackdrop()
    {
        if (blurLayer is null || backdropBrush is null)
            return;

        var source = SourceElement;
        if (!IsBlurEnabled
            || Visibility != Visibility.Visible
            || ActualWidth <= 0d
            || ActualHeight <= 0d
            || source is null
            || source.ActualWidth <= 0d
            || source.ActualHeight <= 0d)
        {
            DeactivateBackdrop();
            return;
        }

        if (ReferenceEquals(source, this) || source.IsAncestorOf(this))
        {
            LogRecursiveSourceOnce(source);
            DeactivateBackdrop();
            return;
        }

        Rect desiredViewbox;
        try
        {
            var overscan = BlurOverscan;
            desiredViewbox = TransformToVisual(source).TransformBounds(
                new Rect(
                    -overscan,
                    -overscan,
                    ActualWidth + (overscan * 2d),
                    ActualHeight + (overscan * 2d)));
        }
        catch (InvalidOperationException)
        {
            DeactivateBackdrop();
            return;
        }

        if (!IsValidViewbox(desiredViewbox))
        {
            DeactivateBackdrop();
            return;
        }

        var viewbox = Rect.Intersect(
            desiredViewbox,
            new Rect(0d, 0d, source.ActualWidth, source.ActualHeight));
        if (!IsValidViewbox(viewbox))
        {
            DeactivateBackdrop();
            return;
        }

        var overscanSize = BlurOverscan * 2d;
        var viewport = CalculateMirroredViewport(
            desiredViewbox,
            viewbox,
            ActualWidth + overscanSize,
            ActualHeight + overscanSize);
        if (!IsValidViewbox(viewport))
        {
            DeactivateBackdrop();
            return;
        }

        if (!ReferenceEquals(backdropBrush.Visual, source))
            backdropBrush.Visual = source;

        if (lastViewbox != viewbox)
        {
            backdropBrush.Viewbox = viewbox;
            lastViewbox = viewbox;
        }
        if (lastViewport != viewport)
        {
            backdropBrush.Viewport = viewport;
            lastViewport = viewport;
        }

        blurLayer.Visibility = Visibility.Visible;
    }

    private static void OnBackdropSourceChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not BackdropBlurBorder border)
            return;

        border.recursiveSourceWarningLogged = false;
        border.lastViewbox = Rect.Empty;
        border.lastViewport = Rect.Empty;
        border.UpdateRenderTracking();
        border.RefreshBackdrop();
    }

    private static void OnBackdropPresentationChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is BackdropBlurBorder border)
        {
            border.UpdateRenderTracking();
            border.RefreshBackdrop();
        }
    }

    private static void OnBlurRadiusChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not BackdropBlurBorder border)
            return;

        border.UpdateBlurLayerOverscan();
        border.lastViewbox = Rect.Empty;
        border.lastViewport = Rect.Empty;
        border.RefreshBackdrop();
    }

    private void BackdropBlurBorder_Loaded(object sender, RoutedEventArgs e)
    {
        isLoaded = true;
        UpdateRenderTracking();
        RefreshBackdrop();
    }

    private void BackdropBlurBorder_Unloaded(object sender, RoutedEventArgs e)
    {
        isLoaded = false;
        StopRenderTracking();
        DeactivateBackdrop();
    }

    private void StartRenderTracking()
    {
        if (isRenderTrackingActive)
            return;

        CompositionTarget.Rendering += CompositionTarget_Rendering;
        isRenderTrackingActive = true;
    }

    private void UpdateRenderTracking()
    {
        if (isLoaded && IsBlurEnabled && SourceElement is not null)
            StartRenderTracking();
        else
            StopRenderTracking();
    }

    private void StopRenderTracking()
    {
        if (!isRenderTrackingActive)
            return;

        CompositionTarget.Rendering -= CompositionTarget_Rendering;
        isRenderTrackingActive = false;
    }

    private void CompositionTarget_Rendering(object? sender, EventArgs e)
    {
        RefreshBackdrop();
    }

    private void UpdateBlurLayerOverscan()
    {
        if (blurLayer is null)
            return;

        var overscan = BlurOverscan;
        blurLayer.Margin = new Thickness(-overscan);
    }

    private void DeactivateBackdrop()
    {
        if (backdropBrush is not null)
            backdropBrush.Visual = null;

        lastViewbox = Rect.Empty;
        lastViewport = Rect.Empty;
        if (blurLayer is not null)
            blurLayer.Visibility = Visibility.Collapsed;
    }

    private void LogRecursiveSourceOnce(FrameworkElement source)
    {
        if (recursiveSourceWarningLogged)
            return;

        recursiveSourceWarningLogged = true;
        Log.Warning(
            "Backdrop blur source contains the blur control and cannot be sampled safely. SourceType={SourceType}",
            source.GetType().FullName);
    }

    private static bool IsValidViewbox(Rect viewbox)
    {
        return !viewbox.IsEmpty
            && double.IsFinite(viewbox.X)
            && double.IsFinite(viewbox.Y)
            && double.IsFinite(viewbox.Width)
            && double.IsFinite(viewbox.Height)
            && viewbox.Width > 0d
            && viewbox.Height > 0d;
    }

    private static Rect CalculateMirroredViewport(
        Rect desiredViewbox,
        Rect clippedViewbox,
        double destinationWidth,
        double destinationHeight)
    {
        var scaleX = destinationWidth / desiredViewbox.Width;
        var scaleY = destinationHeight / desiredViewbox.Height;
        return new Rect(
            Math.Max(0d, (clippedViewbox.Left - desiredViewbox.Left) * scaleX),
            Math.Max(0d, (clippedViewbox.Top - desiredViewbox.Top) * scaleY),
            Math.Min(destinationWidth, clippedViewbox.Width * scaleX),
            Math.Min(destinationHeight, clippedViewbox.Height * scaleY));
    }

    private static bool IsNonNegativeFiniteDouble(object value)
    {
        var number = (double)value;
        return double.IsFinite(number) && number >= 0d;
    }

    private static bool IsRenderingBiasValid(object value)
    {
        return value is RenderingBias.Performance or RenderingBias.Quality;
    }

    private static bool IsCornerRadiusValid(object value)
    {
        var radius = (CornerRadius)value;
        return IsNonNegativeFinite(radius.TopLeft)
            && IsNonNegativeFinite(radius.TopRight)
            && IsNonNegativeFinite(radius.BottomRight)
            && IsNonNegativeFinite(radius.BottomLeft);
    }

    private static bool IsNonNegativeFinite(double value)
    {
        return double.IsFinite(value) && value >= 0d;
    }

    private static double CalculateBlurOverscan(double blurRadius)
    {
        return Math.Ceiling(blurRadius * BlurOverscanFactor);
    }
}
