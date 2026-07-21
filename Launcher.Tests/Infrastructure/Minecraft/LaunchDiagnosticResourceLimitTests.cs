/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Diagnostics;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;
using Launcher.Tests.Helpers;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class LaunchDiagnosticResourceLimitTests : TestTempDirectory
{
    [Fact]
    public async Task BoundedReaderReturnsOnlyConfiguredHeadAndTailWindows()
    {
        Directory.CreateDirectory(TempRoot);
        var path = Path.Combine(TempRoot, "large.log");
        await File.WriteAllLinesAsync(path, Enumerable.Range(1, 200).Select(index => $"line-{index:D3}"));

        var head = await BoundedDiagnosticFileReader.ReadHeadAsync(
            path,
            CancellationToken.None,
            maxLines: 3,
            maxBytes: 80);
        var tail = await BoundedDiagnosticFileReader.ReadTailAsync(
            path,
            CancellationToken.None,
            maxLines: 3,
            maxBytes: 80);

        Assert.Equal(["line-001", "line-002", "line-003"], SplitLines(head));
        Assert.Equal(["line-198", "line-199", "line-200"], SplitLines(tail));
    }

    [Fact]
    public async Task ExceptionDiagnosticDoesNotEnumerateHistoricalCrashReports()
    {
        var context = CreateContext();
        var crashDirectory = Path.Combine(context.InstanceDirectory, "crash-reports");
        Directory.CreateDirectory(crashDirectory);
        var historicalCrash = Path.Combine(crashDirectory, "crash-historical-client.txt");
        await File.WriteAllTextAsync(
            historicalCrash,
            "Mod 'Historical' (historical) 1.0 requires any version of old_api, which is missing!");

        var result = await LaunchDiagnosticsWriter.WriteExceptionDiagnosticAsync(
            context,
            "launch_failed",
            "Launch failed.",
            new InvalidOperationException("unrelated failure"),
            startInfo: null,
            createdAt: DateTimeOffset.UtcNow,
            diagnosticCandidates: [],
            cancellationToken: CancellationToken.None);

        Assert.Null(result.Analysis);
        var candidate = Assert.Single(result.DiagnosticCandidates!);
        Assert.Equal(LaunchDiagnosticType.LauncherDiagnostic, candidate.Type);
        Assert.NotNull(result.DiagnosticPath);
        Assert.DoesNotContain("crash-historical-client.txt", await File.ReadAllTextAsync(result.DiagnosticPath!));
    }

    private static string[] SplitLines(string text) =>
        text.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private LaunchDiagnosticContext CreateContext()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var instanceDirectory = Path.Combine(minecraftDirectory, "versions", "Example");
        Directory.CreateDirectory(instanceDirectory);
        return new LaunchDiagnosticContext(
            minecraftDirectory,
            instanceDirectory,
            "instance",
            "Example",
            "Example",
            "1.21.9",
            LoaderKind.Fabric,
            "0.19.3",
            @"C:\Java\bin\java.exe",
            "21.0.1",
            "Test",
            4096,
            []);
    }
}
