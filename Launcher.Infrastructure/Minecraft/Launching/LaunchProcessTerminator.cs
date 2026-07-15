/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Diagnostics;
using System.IO;

namespace Launcher.Infrastructure.Minecraft;

internal interface ILaunchProcessTerminator
{
    Task TerminateAsync(Process process);
}

internal sealed class LaunchProcessTerminator : ILaunchProcessTerminator
{
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(10);

    public async Task TerminateAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) when (HasExited(process))
        {
            return;
        }

        try
        {
            await process.WaitForExitAsync(CancellationToken.None)
                .WaitAsync(ExitTimeout)
                .ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            throw new IOException(
                $"Timed out while terminating the Minecraft process tree. ProcessId={TryGetProcessId(process)}",
                exception);
        }

        if (!HasExited(process))
            throw new IOException($"The Minecraft process tree did not exit. ProcessId={TryGetProcessId(process)}");
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

    private static int? TryGetProcessId(Process process)
    {
        try
        {
            return process.Id;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
