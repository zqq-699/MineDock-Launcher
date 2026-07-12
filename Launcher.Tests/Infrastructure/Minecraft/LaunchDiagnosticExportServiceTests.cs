/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO.Compression;
using Launcher.Application.Services;
using Launcher.Infrastructure.Minecraft;
using Launcher.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class LaunchDiagnosticExportServiceTests : TestTempDirectory
{
    [Fact]
    public async Task ExportWritesEveryDiagnosticTypeToStructuredArchive()
    {
        var sourceRoot = Path.Combine(TempRoot, "sources");
        Directory.CreateDirectory(sourceRoot);
        var crash = await WriteSourceAsync(sourceRoot, "crash-client.txt", "crash");
        var jvm = await WriteSourceAsync(sourceRoot, "hs_err_pid1.log", "jvm");
        var latest = await WriteSourceAsync(sourceRoot, "latest.log", "latest");
        var output = await WriteSourceAsync(sourceRoot, "launch-output.log", "output");
        var launcher = await WriteSourceAsync(sourceRoot, "launch-diagnostics.log", "launcher");
        var archivePath = Path.Combine(TempRoot, "exports", "report.zip");
        var service = CreateService();

        var result = await service.ExportAsync(new LaunchDiagnosticExportRequest(
            archivePath,
            "Example",
            "1.21.4",
            [
                new(LaunchDiagnosticType.MinecraftCrashReport, crash),
                new(LaunchDiagnosticType.JvmCrashReport, jvm),
                new(LaunchDiagnosticType.MinecraftLatestLog, latest),
                new(LaunchDiagnosticType.CapturedOutput, output),
                new(LaunchDiagnosticType.LauncherDiagnostic, launcher),
                new(LaunchDiagnosticType.MinecraftCrashReport, crash)
            ]));

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.ExportedFileCount);
        Assert.Equal(0, result.SkippedFileCount);
        using var archive = ZipFile.OpenRead(archivePath);
        AssertEntryContent(archive, "minecraft/crash-reports/crash-client.txt", "crash");
        AssertEntryContent(archive, "jvm/hs_err_pid1.log", "jvm");
        AssertEntryContent(archive, "minecraft/logs/latest.log", "latest");
        AssertEntryContent(archive, "launcher/captured-output/launch-output.log", "output");
        AssertEntryContent(archive, "launcher/diagnostics/launch-diagnostics.log", "launcher");
        var index = ReadEntry(archive, "report-index.txt");
        Assert.Contains("InstanceName: Example", index);
        Assert.Contains("VersionName: 1.21.4", index);
    }

    [Fact]
    public async Task ExportUsesStableSuffixForDuplicateEntryNames()
    {
        var first = await WriteSourceAsync(Path.Combine(TempRoot, "first"), "crash-client.txt", "first");
        var second = await WriteSourceAsync(Path.Combine(TempRoot, "second"), "crash-client.txt", "second");
        var archivePath = Path.Combine(TempRoot, "report.zip");

        var result = await CreateService().ExportAsync(new LaunchDiagnosticExportRequest(
            archivePath,
            "Example",
            "1.21.4",
            [
                new(LaunchDiagnosticType.MinecraftCrashReport, first),
                new(LaunchDiagnosticType.MinecraftCrashReport, second)
            ]));

        Assert.True(result.IsSuccess);
        using var archive = ZipFile.OpenRead(archivePath);
        AssertEntryContent(archive, "minecraft/crash-reports/crash-client.txt", "first");
        AssertEntryContent(archive, "minecraft/crash-reports/crash-client (2).txt", "second");
    }

    [Fact]
    public async Task ExportSkipsMissingFileAndRecordsItInIndex()
    {
        var launcher = await WriteSourceAsync(TempRoot, "launch-diagnostics.log", "launcher");
        var missing = Path.Combine(TempRoot, "missing-crash.txt");
        var archivePath = Path.Combine(TempRoot, "report.zip");

        var result = await CreateService().ExportAsync(new LaunchDiagnosticExportRequest(
            archivePath,
            "Example",
            "1.21.4",
            [
                new(LaunchDiagnosticType.MinecraftCrashReport, missing),
                new(LaunchDiagnosticType.LauncherDiagnostic, launcher)
            ]));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.ExportedFileCount);
        Assert.Equal(1, result.SkippedFileCount);
        using var archive = ZipFile.OpenRead(archivePath);
        var index = ReadEntry(archive, "report-index.txt");
        Assert.Contains("FileName=missing-crash.txt", index);
        Assert.Contains("Status=skipped", index);
        Assert.Contains("Reason=missing", index);
    }

    [Fact]
    public async Task ExportWithNoReadableFilesLeavesNoArchiveOrTemporaryFile()
    {
        var exportDirectory = Path.Combine(TempRoot, "exports");
        var archivePath = Path.Combine(exportDirectory, "report.zip");

        var result = await CreateService().ExportAsync(new LaunchDiagnosticExportRequest(
            archivePath,
            "Example",
            "1.21.4",
            [new(LaunchDiagnosticType.LauncherDiagnostic, Path.Combine(TempRoot, "missing.log"))]));

        Assert.False(result.IsSuccess);
        Assert.Equal(LaunchDiagnosticExportFailureReason.NoReadableDiagnostics, result.FailureReason);
        Assert.False(File.Exists(archivePath));
        Assert.Empty(Directory.GetFiles(exportDirectory, "*.tmp", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task ExportAtomicallyOverwritesExistingArchive()
    {
        var launcher = await WriteSourceAsync(TempRoot, "launch-diagnostics.log", "new content");
        var archivePath = Path.Combine(TempRoot, "report.zip");
        await File.WriteAllTextAsync(archivePath, "old invalid archive");

        var result = await CreateService().ExportAsync(new LaunchDiagnosticExportRequest(
            archivePath,
            "Example",
            "1.21.4",
            [new(LaunchDiagnosticType.LauncherDiagnostic, launcher)]));

        Assert.True(result.IsSuccess);
        using var archive = ZipFile.OpenRead(archivePath);
        AssertEntryContent(archive, "launcher/diagnostics/launch-diagnostics.log", "new content");
    }

    [Fact]
    public async Task ExportReturnsFileSystemErrorForInvalidDestinationDirectory()
    {
        var launcher = await WriteSourceAsync(TempRoot, "launch-diagnostics.log", "launcher");
        var parentFile = Path.Combine(TempRoot, "not-a-directory");
        await File.WriteAllTextAsync(parentFile, "file");

        var result = await CreateService().ExportAsync(new LaunchDiagnosticExportRequest(
            Path.Combine(parentFile, "report.zip"),
            "Example",
            "1.21.4",
            [new(LaunchDiagnosticType.LauncherDiagnostic, launcher)]));

        Assert.False(result.IsSuccess);
        Assert.Equal(LaunchDiagnosticExportFailureReason.FileSystemError, result.FailureReason);
    }

    [Fact]
    public async Task ExportCancellationRemovesTemporaryArchive()
    {
        var launcher = await WriteSourceAsync(TempRoot, "launch-diagnostics.log", "launcher");
        var exportDirectory = Path.Combine(TempRoot, "exports");
        var archivePath = Path.Combine(exportDirectory, "report.zip");
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => CreateService().ExportAsync(
            new LaunchDiagnosticExportRequest(
                archivePath,
                "Example",
                "1.21.4",
                [new(LaunchDiagnosticType.LauncherDiagnostic, launcher)]),
            cancellationTokenSource.Token));

        Assert.False(File.Exists(archivePath));
        Assert.Empty(Directory.GetFiles(exportDirectory, "*.tmp", SearchOption.TopDirectoryOnly));
    }

    private static LaunchDiagnosticExportService CreateService() =>
        new(NullLogger<LaunchDiagnosticExportService>.Instance);

    private static async Task<string> WriteSourceAsync(string directory, string fileName, string content)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        await File.WriteAllTextAsync(path, content);
        return path;
    }

    private static void AssertEntryContent(ZipArchive archive, string entryName, string expected) =>
        Assert.Equal(expected, ReadEntry(archive, entryName));

    private static string ReadEntry(ZipArchive archive, string entryName)
    {
        var entry = Assert.Single(archive.Entries.Where(candidate => candidate.FullName == entryName));
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }
}
