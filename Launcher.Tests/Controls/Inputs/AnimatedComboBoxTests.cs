/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Runtime.ExceptionServices;
using System.Windows.Input;
using Launcher.App.Controls;

namespace Launcher.Tests.Controls.Inputs;

public sealed class AnimatedComboBoxTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpenPopupConsumesWheelBeforeItCanReachTheOuterPage(bool cursorOverPopup)
    {
        RunOnStaThread(() =>
        {
            var comboBox = new AnimatedComboBox { IsPopupOpen = true };
            var mouseWheel = CreateMouseWheelEvent();

            var handled = comboBox.ProcessOpenPopupMouseWheel(mouseWheel, cursorOverPopup);

            Assert.True(handled);
            Assert.True(mouseWheel.Handled);
        });
    }

    [Fact]
    public void ClosedPopupLeavesWheelAvailableToTheOuterPage()
    {
        RunOnStaThread(() =>
        {
            var comboBox = new AnimatedComboBox();
            var mouseWheel = CreateMouseWheelEvent();

            var handled = comboBox.ProcessOpenPopupMouseWheel(mouseWheel, cursorOverPopup: false);

            Assert.False(handled);
            Assert.False(mouseWheel.Handled);
        });
    }

    [Fact]
    public void InputGuardConsumesPreviewWheelThroughTheWpfInputPipeline()
    {
        RunOnStaThread(() =>
        {
            var comboBox = new AnimatedComboBox { IsPopupOpen = true };
            var mouseWheel = CreateMouseWheelEvent();
            comboBox.AttachPopupInputGuard();
            try
            {
                InputManager.Current.ProcessInput(mouseWheel);
                Assert.True(mouseWheel.Handled);
            }
            finally
            {
                comboBox.DetachPopupInputGuard();
            }
        });
    }

    [Fact]
    public void OpenPopupBlocksAnOuterScrollViewerAtTheClassHandlerBoundary()
    {
        RunOnStaThread(() =>
        {
            var comboBox = new AnimatedComboBox { IsPopupOpen = true };
            var outerScrollViewer = new System.Windows.Controls.ScrollViewer();
            var mouseWheel = CreateMouseWheelEvent();
            comboBox.ActivatePopupWheelIsolation();
            try
            {
                outerScrollViewer.RaiseEvent(mouseWheel);
                Assert.True(mouseWheel.Handled);
            }
            finally
            {
                comboBox.DeactivatePopupWheelIsolation();
            }
        });
    }

    private static MouseWheelEventArgs CreateMouseWheelEvent()
    {
        return new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
        {
            RoutedEvent = Mouse.PreviewMouseWheelEvent
        };
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
