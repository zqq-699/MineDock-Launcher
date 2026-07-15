/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Launcher.Infrastructure.Minecraft;

internal enum GameWindowReadinessResult
{
    WindowVisible,
    ProcessExited
}

internal interface IGameWindowReadinessWaiter
{
    Task<GameWindowReadinessResult> WaitAsync(
        Process process,
        CancellationToken cancellationToken);
}

/// <summary>
/// Waits for the launched Java process to own a visible, non-empty top-level window.
/// There is deliberately no elapsed-time fallback: launch completion means that a
/// window was observed, the process exited, or the user canceled the launch.
/// </summary>
internal sealed class GameWindowReadinessWaiter : IGameWindowReadinessWaiter
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    public async Task<GameWindowReadinessResult> WaitAsync(
        Process process,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (HasExited(process))
                return GameWindowReadinessResult.ProcessExited;

            if (HasVisibleWindow(process))
            {
                // Prefer an exit observed during the same polling turn over a
                // transient window that disappeared while startup was failing.
                return HasExited(process)
                    ? GameWindowReadinessResult.ProcessExited
                    : GameWindowReadinessResult.WindowVisible;
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static bool HasVisibleWindow(Process process)
    {
        try
        {
            process.Refresh();
            var window = process.MainWindowHandle;
            return window != IntPtr.Zero
                   && IsWindowVisible(window)
                   && GetWindowRect(window, out var bounds)
                   && bounds.Right > bounds.Left
                   && bounds.Bottom > bounds.Top;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out WindowBounds bounds);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowBounds
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
