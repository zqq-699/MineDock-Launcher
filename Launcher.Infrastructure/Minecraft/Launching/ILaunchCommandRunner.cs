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

using System.Diagnostics;
using System.IO;

namespace Launcher.Infrastructure.Minecraft;

internal interface ILaunchCommandRunner
{
    Task RunAsync(string command, string workingDirectory, bool waitForExit, CancellationToken cancellationToken);
}

internal sealed class LaunchCommandRunner : ILaunchCommandRunner
{
    public async Task RunAsync(string command, string workingDirectory, bool waitForExit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command))
            return;

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                Arguments = "/d /s /c " + command,
                WorkingDirectory = ResolveWorkingDirectory(workingDirectory),
                CreateNoWindow = true,
                UseShellExecute = false
            }
        };

        process.Start();
        if (!waitForExit)
            return;

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Launch command exited with code {process.ExitCode}.");
    }

    private static string ResolveWorkingDirectory(string workingDirectory)
    {
        return !string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory)
            ? workingDirectory
            : Environment.CurrentDirectory;
    }
}
