/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Launcher.App.Behaviors;
using Launcher.App.Controls;

namespace Launcher.Tests.Behaviors;

public sealed class BackdropBlurHostTests
{
    [Fact]
    public void AppliedHostPreservesContentAndPaddingAboveTheBackdrop()
    {
        RunOnStaThread(() =>
        {
            var content = new TextBlock { Text = "Foreground" };
            var host = new Border
            {
                Background = Brushes.Red,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 10),
                Child = content
            };
            BackdropBlurHost.SetFallbackBrush(host, Brushes.Red);
            BackdropBlurHost.SetIsBlurEnabled(host, true);
            BackdropBlurHost.SetIsApplied(host, true);

            host.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

            var layers = Assert.IsType<Grid>(host.Child);
            var backdrop = Assert.IsType<BackdropBlurBorder>(layers.Children[0]);
            var contentHost = Assert.IsType<Border>(layers.Children[1]);
            Assert.True(backdrop.IsBlurEnabled);
            Assert.Same(content, contentHost.Child);
            Assert.Equal(new Thickness(14, 10, 14, 10), contentHost.Padding);
            Assert.Equal(default, host.Padding);
            Assert.Equal(Brushes.Transparent, host.Background);
        });
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
