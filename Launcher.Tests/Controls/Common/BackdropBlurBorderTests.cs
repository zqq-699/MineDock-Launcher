/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Launcher.App.Controls;

namespace Launcher.Tests.Controls.Common;

public sealed class BackdropBlurBorderTests
{
    [Fact]
    public void DefaultsUseTheHistoricalGaussianBlurConfiguration()
    {
        RunOnStaThread(() =>
        {
            var content = new Button();
            var control = CreateControl(content);
            Arrange(control, 80d, 40d);

            Assert.Same(content, control.Content);
            Assert.Equal(42d, control.BlurRadius);
            Assert.True(control.IsBlurEnabled);
            Assert.Equal(RenderingBias.Performance, control.BlurRenderingBias);
            Assert.Equal(KernelType.Gaussian, control.BackdropEffect?.KernelType);
            Assert.Equal(42d, control.BackdropEffect?.Radius);
        });
    }

    [Fact]
    public void RefreshBackdropOverscansTheTargetBoundsIntoTheSiblingSource()
    {
        RunOnStaThread(() =>
        {
            var root = new Canvas();
            var source = new Border
            {
                Width = 300d,
                Height = 200d,
                Background = Brushes.CornflowerBlue
            };
            var control = CreateControl(new TextBlock { Text = "Foreground" });
            control.Width = 80d;
            control.Height = 40d;
            control.SourceElement = source;
            Canvas.SetLeft(control, 50d);
            Canvas.SetTop(control, 30d);
            root.Children.Add(source);
            root.Children.Add(control);

            Arrange(root, 300d, 200d);
            control.ApplyTemplate();
            control.RefreshBackdrop();

            Assert.True(control.IsBackdropActive);
            Assert.Same(source, control.BackdropBrush?.Visual);
            Assert.Equal(63d, control.BlurOverscan);
            Assert.Equal(new Thickness(-63d), GetBlurLayer(control).Margin);
            Assert.Equal(TileMode.FlipXY, control.BackdropBrush?.TileMode);
            Assert.Equal(BrushMappingMode.Absolute, control.BackdropBrush?.ViewportUnits);
            Assert.Equal(new Rect(0d, 0d, 193d, 133d), control.BackdropBrush?.Viewbox);
            Assert.Equal(new Rect(13d, 33d, 193d, 133d), control.BackdropBrush?.Viewport);

            Canvas.SetLeft(control, 90d);
            root.InvalidateArrange();
            Arrange(root, 300d, 200d);
            control.RefreshBackdrop();

            Assert.Equal(new Rect(27d, 0d, 206d, 133d), control.BackdropBrush?.Viewbox);
            Assert.Equal(new Rect(0d, 33d, 206d, 133d), control.BackdropBrush?.Viewport);
        });
    }

    [Fact]
    public void ChangingBlurRadiusUpdatesTheOverscanAndSampleBounds()
    {
        RunOnStaThread(() =>
        {
            var root = new Canvas();
            var source = new Border { Width = 300d, Height = 200d };
            var control = CreateControl(new Border());
            control.Width = 80d;
            control.Height = 40d;
            control.SourceElement = source;
            Canvas.SetLeft(control, 50d);
            Canvas.SetTop(control, 30d);
            root.Children.Add(source);
            root.Children.Add(control);

            Arrange(root, 300d, 200d);
            control.BlurRadius = 20d;
            control.RefreshBackdrop();

            Assert.Equal(30d, control.BlurOverscan);
            Assert.Equal(new Thickness(-30d), GetBlurLayer(control).Margin);
            Assert.Equal(new Rect(20d, 0d, 140d, 100d), control.BackdropBrush?.Viewbox);
            Assert.Equal(new Rect(0d, 0d, 140d, 100d), control.BackdropBrush?.Viewport);
        });
    }

    [Fact]
    public void SourceEdgeUsesMirroredTilesInsteadOfTransparentOverscan()
    {
        RunOnStaThread(() =>
        {
            var root = new Canvas();
            var source = new Border { Width = 300d, Height = 200d };
            var control = CreateControl(new Border());
            control.Width = 80d;
            control.Height = 40d;
            control.SourceElement = source;
            Canvas.SetLeft(control, 0d);
            Canvas.SetTop(control, 50d);
            root.Children.Add(source);
            root.Children.Add(control);

            Arrange(root, 300d, 200d);
            control.RefreshBackdrop();

            Assert.True(control.IsBackdropActive);
            Assert.Equal(new Rect(0d, 0d, 143d, 153d), control.BackdropBrush?.Viewbox);
            Assert.Equal(new Rect(63d, 13d, 143d, 153d), control.BackdropBrush?.Viewport);
            Assert.Equal(TileMode.FlipXY, control.BackdropBrush?.TileMode);
        });
    }

    [Fact]
    public void InvalidOrDisabledSourcesLeaveTheForegroundAndFallbackLayersAvailable()
    {
        RunOnStaThread(() =>
        {
            var root = new Grid();
            var source = new Border();
            var content = new Button();
            var control = CreateControl(content);
            root.Children.Add(source);
            root.Children.Add(control);
            Arrange(root, 160d, 90d);

            control.SourceElement = root;
            control.RefreshBackdrop();
            Assert.False(control.IsBackdropActive);
            Assert.Null(control.BackdropBrush?.Visual);
            Assert.Same(content, control.Content);

            control.SourceElement = source;
            control.IsBlurEnabled = false;
            control.RefreshBackdrop();
            Assert.False(control.IsBackdropActive);
            Assert.Null(control.BackdropBrush?.Visual);

            control.IsBlurEnabled = true;
            control.SourceElement = null;
            control.RefreshBackdrop();
            Assert.False(control.IsBackdropActive);
            Assert.Null(control.BackdropBrush?.Visual);
        });
    }

    [Fact]
    public void LoadedAndUnloadedEventsManagePerFrameTrackingWithoutLeakingTheSource()
    {
        RunOnStaThread(() =>
        {
            var control = CreateControl(new Border());
            control.SourceElement = new Border();
            control.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Assert.True(control.IsRenderTrackingActive);

            control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            Assert.False(control.IsRenderTrackingActive);
            Assert.False(control.IsBackdropActive);
            Assert.Null(control.BackdropBrush?.Visual);
        });
    }

    [Fact]
    public void DisabledBackdropDoesNotSubscribeToPerFrameRendering()
    {
        RunOnStaThread(() =>
        {
            var control = CreateControl(new Border());
            control.SourceElement = new Border();
            control.IsBlurEnabled = false;

            control.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

            Assert.False(control.IsRenderTrackingActive);
        });
    }

    private static BackdropBlurBorder CreateControl(object content)
    {
        var control = new BackdropBlurBorder
        {
            Content = content,
            Template = CreateTemplate()
        };
        control.ApplyTemplate();
        return control;
    }

    private static ControlTemplate CreateTemplate()
    {
        const string xaml = """
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             xmlns:controls="clr-namespace:Launcher.App.Controls;assembly=BlockHelm_Launcher_x64"
                             TargetType="{x:Type controls:BackdropBlurBorder}">
                <Grid>
                    <Border x:Name="PART_BlurLayer" Visibility="Collapsed">
                        <Border.Background>
                            <VisualBrush ViewboxUnits="Absolute"
                                         ViewportUnits="Absolute"
                                         TileMode="FlipXY" />
                        </Border.Background>
                        <Border.Effect>
                            <BlurEffect KernelType="Gaussian"
                                        Radius="{Binding BlurRadius, RelativeSource={RelativeSource TemplatedParent}}"
                                        RenderingBias="{Binding BlurRenderingBias, RelativeSource={RelativeSource TemplatedParent}}" />
                        </Border.Effect>
                    </Border>
                    <ContentPresenter Content="{TemplateBinding Content}" />
                </Grid>
            </ControlTemplate>
            """;
        return (ControlTemplate)XamlReader.Parse(xaml);
    }

    private static Border GetBlurLayer(BackdropBlurBorder control)
    {
        return Assert.IsType<Border>(control.Template.FindName("PART_BlurLayer", control));
    }

    private static void Arrange(FrameworkElement element, double width, double height)
    {
        var size = new Size(width, height);
        element.Measure(size);
        element.Arrange(new Rect(size));
        element.UpdateLayout();
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
