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
using Launcher.Infrastructure.Updates;

namespace Launcher.Tests.Infrastructure.Updates;

public sealed class LauncherUpdateApplyRunnerTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), "launcher-update-apply-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void RunReplacesTargetExecutable()
    {
        Directory.CreateDirectory(tempRoot);
        var sourcePath = Path.Combine(tempRoot, "BlockHelm_Launcher_x64.exe");
        var targetPath = Path.Combine(tempRoot, "BlockHelm-Launcher.exe");
        var logDirectory = Path.Combine(tempRoot, "log");
        File.WriteAllText(sourcePath, "new");
        File.WriteAllText(targetPath, "old");
        var runner = new LauncherUpdateApplyRunner();

        var exitCode = runner.Run(new LauncherUpdateApplyOptions(
            ProcessId: 0,
            SourcePath: sourcePath,
            TargetPath: targetPath,
            LogDirectory: logDirectory,
            Restart: false));

        Assert.Equal(0, exitCode);
        Assert.Equal("new", File.ReadAllText(targetPath));
        Assert.NotEmpty(Directory.GetFiles(logDirectory, "updater-*.log"));
    }

    [Fact]
    public void RunStartsTargetExecutableWhenRestartIsRequested()
    {
        Directory.CreateDirectory(tempRoot);
        var sourcePath = Path.Combine(tempRoot, "BlockHelm_Launcher_x64.exe");
        var targetPath = Path.Combine(tempRoot, "BlockHelm-Launcher.exe");
        var logDirectory = Path.Combine(tempRoot, "log");
        File.WriteAllText(sourcePath, "new");
        File.WriteAllText(targetPath, "old");
        ProcessStartInfo? startedProcess = null;
        var runner = new LauncherUpdateApplyRunner(startInfo => startedProcess = startInfo);

        var exitCode = runner.Run(new LauncherUpdateApplyOptions(
            ProcessId: 0,
            SourcePath: sourcePath,
            TargetPath: targetPath,
            LogDirectory: logDirectory,
            Restart: true));

        Assert.Equal(0, exitCode);
        Assert.Equal(targetPath, startedProcess?.FileName);
        Assert.True(startedProcess?.UseShellExecute);
    }

    [Fact]
    public void RunRejectsMissingSourceExecutable()
    {
        Directory.CreateDirectory(tempRoot);
        var targetPath = Path.Combine(tempRoot, "BlockHelm-Launcher.exe");
        var logDirectory = Path.Combine(tempRoot, "log");
        File.WriteAllText(targetPath, "old");
        var runner = new LauncherUpdateApplyRunner();

        var exitCode = runner.Run(new LauncherUpdateApplyOptions(
            ProcessId: 0,
            SourcePath: Path.Combine(tempRoot, "missing.exe"),
            TargetPath: targetPath,
            LogDirectory: logDirectory,
            Restart: false));

        Assert.Equal(1, exitCode);
        Assert.Equal("old", File.ReadAllText(targetPath));
        Assert.NotEmpty(Directory.GetFiles(logDirectory, "updater-*.log"));
    }

    [Fact]
    public void ParseRequiresApplyUpdateSourceTargetAndLogDirectory()
    {
        Assert.Null(LauncherUpdateApplyOptions.Parse(["--source", "a.exe", "--target", "b.exe", "--log-dir", "log"]));
        Assert.Null(LauncherUpdateApplyOptions.Parse(["--apply-update", "--source", "a.exe", "--target", "b.exe"]));

        var parsed = LauncherUpdateApplyOptions.Parse([
            "--apply-update",
            "--pid", "12",
            "--source", "a.exe",
            "--target", "b.exe",
            "--log-dir", "log",
            "--restart"
        ]);

        Assert.NotNull(parsed);
        Assert.Equal(12, parsed.ProcessId);
        Assert.True(parsed.Restart);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, recursive: true);
    }
}
