/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;
using Microsoft.Extensions.Logging;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class ForegroundDownloadLogScopeTests
{
    [Fact]
    public void SuccessfulFileWritesStartAndResultWithMetricsAndSanitizedUrls()
    {
        var logger = new CollectingLogger();
        var destination = Path.Combine(Path.GetTempPath(), "scope-tests", "client.jar");
        const string originalUrl = "https://example.test/client.jar?token=secret#fragment";
        var scope = new ForegroundDownloadLogScope(
            logger,
            "MinecraftInstall",
            "client.jar",
            destination,
            originalUrl,
            expectedBytes: 10,
            position: 2,
            total: 5);
        var report = scope.BeginSource();

        report(1, 3, 10);
        report(1, 7, 10);
        report(2, 2, 10);
        scope.Complete(new ResolvedDownloadRequest(
            originalUrl,
            "https://mirror.test/client.jar?credential=hidden",
            DownloadSourcePreference.Official,
            "BMCLAPI",
            "Mojang"));

        var normalEntries = logger.Entries
            .Where(entry => entry.Level is LogLevel.Information or LogLevel.Warning or LogLevel.Error)
            .ToArray();
        Assert.Equal(2, normalEntries.Length);
        Assert.Contains("Foreground file download started", normalEntries[0].Message);
        Assert.Contains(Path.GetFullPath(destination), normalEntries[0].Message);
        Assert.Contains("Foreground file download completed", normalEntries[1].Message);
        Assert.Contains("TransferredBytes=9", normalEntries[1].Message);
        Assert.Contains("Attempts=2", normalEntries[1].Message);
        Assert.Contains("FallbackUsed=True", normalEntries[1].Message);
        Assert.Contains("https://mirror.test/client.jar", normalEntries[1].Message);
        Assert.DoesNotContain("secret", string.Join('\n', logger.Entries.Select(entry => entry.Message)), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hidden", string.Join('\n', logger.Entries.Select(entry => entry.Message)), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("?", string.Join('\n', logger.Entries.Select(entry => entry.Message)));
    }

    [Fact]
    public void ReusedFileStillWritesExactlyOneTerminalResult()
    {
        var logger = new CollectingLogger();
        var scope = new ForegroundDownloadLogScope(
            logger,
            "MinecraftRepair",
            "library.jar",
            Path.Combine(Path.GetTempPath(), "scope-tests", "library.jar"),
            "https://example.test/library.jar");

        scope.Complete(new ResolvedDownloadRequest(
            "https://example.test/library.jar",
            "https://example.test/library.jar",
            DownloadSourcePreference.Official,
            "Official",
            "Mojang"));
        scope.CompleteWithoutDownload("Canceled");

        Assert.Equal(2, logger.Entries.Count(entry => entry.Level == LogLevel.Information));
        Assert.Contains("Status=Reused", logger.Entries.Last().Message);
    }

    [Fact]
    public void FailureWritesOneNormalWarningWithoutExceptionOrSensitiveUrlParts()
    {
        var logger = new CollectingLogger();
        var scope = new ForegroundDownloadLogScope(
            logger,
            "ResourceInstall",
            "mod.jar",
            Path.Combine(Path.GetTempPath(), "scope-tests", "mod.jar"),
            "https://example.test/mod.jar?token=secret");
        var report = scope.BeginSource();
        report(1, 4, 8);

        scope.Fail(
            new HttpRequestException("request failed at https://example.test/mod.jar?token=secret"),
            "https://example.test/mod.jar?token=secret");

        var warning = Assert.Single(logger.Entries.Where(entry => entry.Level == LogLevel.Warning));
        Assert.Null(warning.Exception);
        Assert.Contains("FailureType=HttpRequestException", warning.Message);
        Assert.Contains("TransferredBytes=4", warning.Message);
        Assert.DoesNotContain("secret", string.Join('\n', logger.Entries.Select(entry => entry.Message)), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("?", string.Join('\n', logger.Entries.Select(entry => entry.Message)));
    }

    private sealed class CollectingLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);
}
