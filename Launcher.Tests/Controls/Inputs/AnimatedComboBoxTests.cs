/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Launcher.App.Controls;

namespace Launcher.Tests.Controls.Inputs;

public sealed class AnimatedComboBoxTests
{
    [Fact]
    public void TemplateToggleClickOpensThePopup()
    {
        RunOnStaThread(() =>
        {
            _ = System.Windows.Application.Current ?? new System.Windows.Application();
            var resources = new ResourceDictionary();
            resources.MergedDictionaries.Add(LoadAppDictionary("Resources/Themes/Shared.xaml"));
            resources.MergedDictionaries.Add(LoadAppDictionary("Resources/Themes/Dark.xaml"));
            resources.MergedDictionaries.Add(LoadAppDictionary("Styles/ControlStyles.xaml"));

            var comboBox = new AnimatedComboBox
            {
                Style = Assert.IsType<Style>(resources["LauncherComboBoxStyle"]),
                ItemsSource = new[] { "One", "Two" },
                Width = 240,
                Height = 36
            };
            var host = new Border
            {
                Resources = resources,
                Style = Assert.IsType<Style>(resources["SectionFieldSurfaceStyle"]),
                Child = comboBox
            };
            var window = new Window
            {
                Content = host,
                Width = 400,
                Height = 200,
                Left = -10000,
                Top = -10000,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            };
            window.Show();
            try
            {
                host.UpdateLayout();
                comboBox.ApplyTemplate();

                var toggle = Assert.IsType<ToggleButton>(comboBox.Template.FindName("ToggleButton", comboBox));
                var popup = Assert.IsType<Popup>(comboBox.Template.FindName("PART_Popup", comboBox));
                var popupSurface = Assert.IsAssignableFrom<FrameworkElement>(
                    comboBox.Template.FindName("PopupSurface", comboBox));
                var onClick = typeof(ToggleButton).GetMethod(
                    "OnClick",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(onClick);

                var comboCenter = comboBox.TranslatePoint(
                    new Point(comboBox.ActualWidth / 2d, comboBox.ActualHeight / 2d),
                    window);
                var hit = Assert.IsAssignableFrom<DependencyObject>(window.InputHitTest(comboCenter));
                Assert.True(toggle.IsAncestorOf(hit) || ReferenceEquals(toggle, hit));

                onClick!.Invoke(toggle, null);

                Assert.True(toggle.IsChecked);
                Assert.True(comboBox.IsDropDownOpen);
                Assert.True(comboBox.IsPopupOpen);
                Assert.True(popup.IsOpen);
                Assert.Equal(0d, popupSurface.Opacity);
                PumpDispatcher(TimeSpan.FromMilliseconds(300));
                Assert.True(
                    popupSurface.IsHitTestVisible,
                    $"DropDownOpen={comboBox.IsDropDownOpen}, PopupState={comboBox.IsPopupOpen}, PopupOpen={popup.IsOpen}, Opacity={popupSurface.Opacity}");
                Assert.True(popupSurface.Opacity > 0.9d);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static ResourceDictionary LoadAppDictionary(string relativePath) =>
        new()
        {
            Source = new Uri(
                $"/BlockHelm_Launcher_x64;component/{relativePath}",
                UriKind.Relative)
        };

    private static void PumpDispatcher(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = duration
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
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
