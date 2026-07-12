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
using Launcher.Infrastructure.Minecraft;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class LaunchCommandRunnerTests : TestTempDirectory
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task WaitingCommandCancellationTerminatesEntireProcessTree()
    {
        var runner = new LaunchCommandRunner();
        var command = await CreateLongRunningCommandAsync();
        using var cancellation = new CancellationTokenSource();
        var runTask = runner.RunAsync(command.Command, TempRoot, waitForExit: true, cancellation.Token);

        var parentProcessId = await WaitForProcessIdAsync(command.ParentProcessIdPath);
        var childProcessId = await WaitForProcessIdAsync(command.ChildProcessIdPath);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);
        await WaitUntilAsync(
            () => HasExited(parentProcessId) && HasExited(childProcessId),
            ProcessTimeout,
            "The canceled launch command process tree did not exit.");
    }

    [Fact]
    public async Task PreCanceledTokenDoesNotStartCommand()
    {
        Directory.CreateDirectory(TempRoot);
        var markerPath = Path.Combine(TempRoot, "started.txt");
        var runner = new LaunchCommandRunner();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(
            $"echo started>\"{markerPath}\"",
            TempRoot,
            waitForExit: false,
            cancellation.Token));

        Assert.False(File.Exists(markerPath));
    }

    [Fact]
    public async Task NonWaitingCommandRemainsRunningAfterCancellation()
    {
        var runner = new LaunchCommandRunner();
        var command = await CreateLongRunningCommandAsync();
        using var cancellation = new CancellationTokenSource();
        Process? parentProcess = null;

        try
        {
            await runner.RunAsync(command.Command, TempRoot, waitForExit: false, cancellation.Token);
            var parentProcessId = await WaitForProcessIdAsync(command.ParentProcessIdPath);
            parentProcess = Process.GetProcessById(parentProcessId);

            cancellation.Cancel();
            await Task.Delay(150);

            Assert.False(parentProcess.HasExited);
        }
        finally
        {
            if (parentProcess is not null)
            {
                TryKillProcessTree(parentProcess);
                await WaitForExitAsync(parentProcess);
                parentProcess.Dispose();
            }
        }
    }

    [Fact]
    public async Task ZeroExitCodeCompletesSuccessfully()
    {
        var runner = new LaunchCommandRunner();

        await runner.RunAsync("exit /b 0", TempRoot, waitForExit: true, CancellationToken.None);
    }

    [Fact]
    public async Task NonZeroExitCodeThrows()
    {
        var runner = new LaunchCommandRunner();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(
            "exit /b 7",
            TempRoot,
            waitForExit: true,
            CancellationToken.None));

        Assert.Contains("code 7", exception.Message);
    }

    private async Task<LongRunningCommand> CreateLongRunningCommandAsync()
    {
        Directory.CreateDirectory(TempRoot);
        var scriptPath = Path.Combine(TempRoot, "long-running-command.ps1");
        var parentProcessIdPath = Path.Combine(TempRoot, "parent.pid");
        var childProcessIdPath = Path.Combine(TempRoot, "child.pid");
        var script = $$"""
            $PID | Set-Content -LiteralPath '{{parentProcessIdPath}}'
            $child = Start-Process -FilePath $env:ComSpec -ArgumentList '/d', '/s', '/c', 'ping 127.0.0.1 -n 300 > nul' -PassThru
            $child.Id | Set-Content -LiteralPath '{{childProcessIdPath}}'
            Wait-Process -Id $child.Id
            """;
        await File.WriteAllTextAsync(scriptPath, script);
        return new LongRunningCommand(
            $"powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            parentProcessIdPath,
            childProcessIdPath);
    }

    private static async Task<int> WaitForProcessIdAsync(string path)
    {
        int? processId = null;
        await WaitUntilAsync(
            () => (processId = TryReadProcessId(path)) is not null,
            ProcessTimeout,
            $"The process ID file was not created: {path}");
        return processId!.Value;
    }

    private static int? TryReadProcessId(string path)
    {
        try
        {
            return File.Exists(path) && int.TryParse(File.ReadAllText(path).Trim(), out var processId)
                ? processId
                : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static bool HasExited(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task WaitForExitAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
                await process.WaitForExitAsync().WaitAsync(ProcessTimeout);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, string timeoutMessage)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed >= timeout)
                throw new TimeoutException(timeoutMessage);

            await Task.Delay(25);
        }
    }

    private sealed record LongRunningCommand(
        string Command,
        string ParentProcessIdPath,
        string ChildProcessIdPath);
}
