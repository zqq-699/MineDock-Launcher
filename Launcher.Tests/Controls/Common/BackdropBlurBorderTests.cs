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
            Assert.False(control.IsSourcePreblurred);
            Assert.False(control.IsTintEnabled);
            Assert.Equal(RenderingBias.Performance, control.BlurRenderingBias);
            Assert.Equal(KernelType.Gaussian, control.BackdropEffect?.KernelType);
            Assert.Equal(42d, control.BackdropEffect?.Radius);
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
