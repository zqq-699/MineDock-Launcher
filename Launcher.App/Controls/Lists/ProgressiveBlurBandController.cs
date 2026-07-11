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

using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Launcher.App.Behaviors;
using Launcher.App.Effects;
using Serilog;

namespace Launcher.App.Controls;

internal readonly record struct ProgressiveBlurEffectCreationResult(
    ProgressiveGaussianBlurEffect? HorizontalEffect,
    ProgressiveGaussianBlurEffect? VerticalEffect,
    Exception? Exception)
{
    internal bool IsSuccess => HorizontalEffect is not null && VerticalEffect is not null;

    internal static ProgressiveBlurEffectCreationResult Failed(Exception exception)
    {
        return new ProgressiveBlurEffectCreationResult(null, null, exception);
    }
}

internal delegate ProgressiveBlurEffectCreationResult ProgressiveBlurEffectFactory();

internal delegate void ProgressiveBlurEffectAttacher(
    ProgressiveGaussianBlurEffect horizontalEffect,
    ProgressiveGaussianBlurEffect verticalEffect);

internal sealed record ProgressiveBlurVisualParts(
    FrameworkElement Owner,
    FrameworkElement ListLayer,
    FrameworkElement ListVisualSource,
    FrameworkElement DirectListHost,
    FrameworkElement BlurBandViewport,
    FrameworkElement BlurBandUpscaleHost,
    ScaleTransform BlurBandUpscaleTransform,
    FrameworkElement BlurBandHorizontalHost,
    FrameworkElement BlurBandVerticalHost,
    VisualBrush BlurBandBrush);

/// <summary>
/// 在长列表顶部维护渐进模糊带，并在效果不可用时安全退化为普通裁剪显示。
/// </summary>
internal sealed class ProgressiveBlurBandController
{
    // Shader 效果、视觉复制层和原列表裁剪必须作为一个整体启停，不能留下半激活状态。
    private static readonly DependencyPropertyDescriptor? TopFadeLengthDescriptor =
        DependencyPropertyDescriptor.FromProperty(
            VerticalEdgeOpacityMask.TopFadeLengthProperty,
            typeof(Grid));

    private static int progressiveBlurFailureLogged;

    private readonly ProgressiveBlurVisualParts parts;
    private readonly Func<bool> isActive;
    private readonly ProgressiveBlurEffectFactory effectFactory;
    private readonly ProgressiveBlurEffectAttacher effectAttacher;
    private readonly RectangleGeometry directListClipGeometry = new();
    private ProgressiveGaussianBlurEffect? horizontalEffect;
    private ProgressiveGaussianBlurEffect? verticalEffect;
    private Window? dpiWindow;
    private bool subscriptionsAttached;
    private bool activationFailureLatched;

    internal ProgressiveBlurBandController(
        ProgressiveBlurVisualParts parts,
        Func<bool> isActive,
        ProgressiveBlurEffectFactory? effectFactory = null,
        ProgressiveBlurEffectAttacher? effectAttacher = null)
    {
        this.parts = parts;
        this.isActive = isActive;
        this.effectFactory = effectFactory ?? CreateEffects;
        this.effectAttacher = effectAttacher ?? AttachEffects;
        parts.BlurBandBrush.Visual = parts.ListVisualSource;
    }

    internal void OnLoaded()
    {
        // 进入视觉树后 Window、DPI 和实际尺寸才可靠，此时再订阅并首次布局。
        AttachSubscriptions();
        Update();
    }

    internal void OnUnloaded()
    {
        // 解除窗口/DPI/尺寸事件，避免回收页面仍被视觉树或事件源持有。
        DetachSubscriptions();
        Deactivate();
    }

    internal void OnEnabledChanged(bool becameEnabled)
    {
        if (becameEnabled)
            activationFailureLatched = false;

        Update();
    }

    internal void Update()
    {
        // Update 可由尺寸、主题资源、DPI 和启用状态触发，因此必须保持幂等。
        if (!parts.Owner.IsLoaded || !isActive())
        {
            Deactivate();
            return;
        }

        var width = parts.ListLayer.ActualWidth;
        var height = parts.ListLayer.ActualHeight;
        var blurLength = ResolveEffectiveTopBlurLength(height);
        var visibleBlurBandHeight = Math.Min(
            height,
            blurLength + ProgressiveBlurDefaults.SamplingGuardLength);
        if (width <= 0d || height <= 0d || blurLength <= 0d || visibleBlurBandHeight <= 0d)
        {
            Deactivate();
            return;
        }

        try
        {
            if (!TryEnsureEffects())
                return;

            var maximumRadius = ResolveDoubleResource(
                ProgressiveBlurResourceKeys.MaximumRadius,
                ProgressiveBlurDefaults.MaximumRadius,
                minimum: 0d,
                maximum: double.MaxValue);
            var renderScale = ResolveDoubleResource(
                ProgressiveBlurResourceKeys.RenderScale,
                ProgressiveBlurDefaults.RenderScale,
                minimum: ProgressiveBlurDefaults.MinimumRenderScale,
                maximum: 1d);
            var renderLayout = ProgressiveBlurLayoutCalculator.Calculate(
                width,
                height,
                blurLength,
                visibleBlurBandHeight,
                maximumRadius,
                renderScale,
                VisualTreeHelper.GetDpi(parts.ListLayer));

            UpdateBandLayout(width, height, renderLayout);
            ApplyEffectParameters(
                horizontalEffect!,
                renderLayout.LowResolutionWidth,
                renderLayout.LowResolutionHeight,
                renderLayout.ScaledBlurLength,
                renderLayout.HorizontalMaximumRadius);
            ApplyEffectParameters(
                verticalEffect!,
                renderLayout.LowResolutionWidth,
                renderLayout.LowResolutionHeight,
                renderLayout.ScaledBlurLength,
                renderLayout.VerticalMaximumRadius);

            effectAttacher(horizontalEffect!, verticalEffect!);
            parts.BlurBandViewport.Visibility = Visibility.Visible;
            VerticalEdgeOpacityMask.SetTopMinimumOpacity(
                parts.ListLayer,
                ResolveDoubleResource(
                    ProgressiveBlurResourceKeys.ActiveMinimumOpacity,
                    ProgressiveBlurDefaults.ActiveMinimumOpacity,
                    minimum: 0d,
                    maximum: 1d));
            VerticalEdgeOpacityMask.SetTopIntermediateOpacity(
                parts.ListLayer,
                ResolveDoubleResource(
                    ProgressiveBlurResourceKeys.ActiveIntermediateOpacity,
                    ProgressiveBlurDefaults.ActiveIntermediateOpacity,
                    minimum: 0d,
                    maximum: 1d));
        }
        catch (Exception exception)
        {
            HandleActivationFailure(exception);
        }
    }

    private void AttachSubscriptions()
    {
        if (subscriptionsAttached)
            return;

        parts.ListLayer.SizeChanged += ListLayer_SizeChanged;
        parts.Owner.IsVisibleChanged += Owner_IsVisibleChanged;
        TopFadeLengthDescriptor?.AddValueChanged(parts.ListLayer, ListLayer_TopFadeLengthChanged);
        AttachDpiSubscription();
        subscriptionsAttached = true;
    }

    private void DetachSubscriptions()
    {
        if (!subscriptionsAttached)
            return;

        parts.ListLayer.SizeChanged -= ListLayer_SizeChanged;
        parts.Owner.IsVisibleChanged -= Owner_IsVisibleChanged;
        TopFadeLengthDescriptor?.RemoveValueChanged(parts.ListLayer, ListLayer_TopFadeLengthChanged);
        DetachDpiSubscription();
        subscriptionsAttached = false;
    }

    private void AttachDpiSubscription()
    {
        var ownerWindow = Window.GetWindow(parts.Owner);
        if (ReferenceEquals(dpiWindow, ownerWindow))
            return;

        DetachDpiSubscription();
        dpiWindow = ownerWindow;
        if (dpiWindow is not null)
            dpiWindow.DpiChanged += Window_DpiChanged;
    }

    private void DetachDpiSubscription()
    {
        if (dpiWindow is null)
            return;

        dpiWindow.DpiChanged -= Window_DpiChanged;
        dpiWindow = null;
    }

    private void Window_DpiChanged(object sender, DpiChangedEventArgs e)
    {
        Update();
    }

    private void ListLayer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        Update();
    }

    private void Owner_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Update();
    }

    private void ListLayer_TopFadeLengthChanged(object? sender, EventArgs e)
    {
        Update();
    }

    private bool TryEnsureEffects()
    {
        // Shader 可能因显卡能力或远程桌面失败；当前控件锁存失败，不在每帧反复创建。
        if (activationFailureLatched)
            return false;

        if (horizontalEffect is not null && verticalEffect is not null)
            return true;

        horizontalEffect = null;
        verticalEffect = null;
        var result = effectFactory();
        if (!result.IsSuccess)
        {
            HandleActivationFailure(
                result.Exception
                ?? new InvalidOperationException("Progressive blur shader did not create both effect instances."));
            return false;
        }

        horizontalEffect = result.HorizontalEffect;
        verticalEffect = result.VerticalEffect;
        return true;
    }

    private static ProgressiveBlurEffectCreationResult CreateEffects()
    {
        // 横向和纵向两个 pass 必须都成功，单 pass 会产生方向性错误视觉。
        if (!ProgressiveGaussianBlurEffect.TryCreate(
                directionX: 1d,
                directionY: 0d,
                out var horizontalEffect,
                out var horizontalException)
            || horizontalEffect is null)
        {
            return ProgressiveBlurEffectCreationResult.Failed(
                horizontalException
                ?? new InvalidOperationException("Progressive blur horizontal effect could not be created."));
        }

        if (!ProgressiveGaussianBlurEffect.TryCreate(
                directionX: 0d,
                directionY: 1d,
                out var verticalEffect,
                out var verticalException)
            || verticalEffect is null)
        {
            return ProgressiveBlurEffectCreationResult.Failed(
                verticalException
                ?? new InvalidOperationException("Progressive blur vertical effect could not be created."));
        }

        return new ProgressiveBlurEffectCreationResult(horizontalEffect, verticalEffect, null);
    }

    private void AttachEffects(
        ProgressiveGaussianBlurEffect horizontalBlurEffect,
        ProgressiveGaussianBlurEffect verticalBlurEffect)
    {
        parts.BlurBandHorizontalHost.Effect = horizontalBlurEffect;
        parts.BlurBandVerticalHost.Effect = verticalBlurEffect;
    }

    private static void ApplyEffectParameters(
        ProgressiveGaussianBlurEffect effect,
        double width,
        double height,
        double blurLength,
        double maximumRadius)
    {
        effect.InputWidth = Math.Max(1d, width);
        effect.InputHeight = Math.Max(1d, height);
        effect.BlurLength = Math.Clamp(blurLength, 0d, height);
        effect.MaximumRadius = Math.Max(0d, maximumRadius);
    }

    private void UpdateBandLayout(
        double width,
        double height,
        ProgressiveBlurRenderLayout renderLayout)
    {
        // 在设备像素上对齐模糊带边界，避免高 DPI 下出现一像素缝隙或重复采样。
        parts.BlurBandViewport.Height = renderLayout.PresentationHeight;
        parts.BlurBandUpscaleHost.Width = renderLayout.LowResolutionWidth;
        parts.BlurBandUpscaleHost.Height = renderLayout.LowResolutionHeight;
        parts.BlurBandHorizontalHost.Width = renderLayout.LowResolutionWidth;
        parts.BlurBandHorizontalHost.Height = renderLayout.LowResolutionHeight;
        parts.BlurBandVerticalHost.Width = renderLayout.LowResolutionWidth;
        parts.BlurBandVerticalHost.Height = renderLayout.LowResolutionHeight;
        parts.BlurBandUpscaleTransform.ScaleX = renderLayout.UpscaleX;
        parts.BlurBandUpscaleTransform.ScaleY = renderLayout.UpscaleY;
        parts.BlurBandBrush.Viewbox = new Rect(0d, 0d, width, renderLayout.TextureHeight);

        directListClipGeometry.Rect = new Rect(
            0d,
            renderLayout.DirectListStart,
            width,
            Math.Max(0d, height - renderLayout.DirectListStart));
        parts.DirectListHost.Clip = directListClipGeometry;
    }

    private double ResolveEffectiveTopBlurLength(double height)
    {
        // 主题资源给出理想长度，但始终钳制在列表高度内，避免负裁剪区域。
        if (height <= 0d)
            return 0d;

        var topLength = Math.Clamp(VerticalEdgeOpacityMask.GetTopFadeLength(parts.ListLayer), 0d, height);
        var bottomLength = Math.Clamp(VerticalEdgeOpacityMask.GetBottomFadeLength(parts.ListLayer), 0d, height);
        var totalLength = topLength + bottomLength;
        if (totalLength > height && totalLength > 0d)
            topLength *= height / totalLength;

        return topLength;
    }

    private double ResolveDoubleResource(string key, double fallback, double minimum, double maximum)
    {
        var value = parts.Owner.TryFindResource(key) is double resourceValue ? resourceValue : fallback;
        return double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;
    }

    private void HandleActivationFailure(Exception exception)
    {
        // 故障只记录一次并永久退化；模糊是装饰效果，不能影响列表交互。
        Deactivate();
        horizontalEffect = null;
        verticalEffect = null;
        activationFailureLatched = true;
        LogFailureOnce(exception);
    }

    private void Deactivate()
    {
        // 退化路径恢复原列表裁剪和可见性，关闭效果后应与普通列表等价。
        parts.BlurBandVerticalHost.Effect = null;
        parts.BlurBandHorizontalHost.Effect = null;
        parts.BlurBandViewport.Visibility = Visibility.Collapsed;
        parts.DirectListHost.Clip = null;
        parts.ListLayer.ClearValue(VerticalEdgeOpacityMask.TopMinimumOpacityProperty);
        parts.ListLayer.ClearValue(VerticalEdgeOpacityMask.TopIntermediateOpacityProperty);
    }

    private static void LogFailureOnce(Exception exception)
    {
        if (Interlocked.Exchange(ref progressiveBlurFailureLogged, 1) != 0)
            return;

        Log.Warning(
            exception,
            "Progressive blur effect activation failed; opacity fade fallback will be used.");
    }
}
