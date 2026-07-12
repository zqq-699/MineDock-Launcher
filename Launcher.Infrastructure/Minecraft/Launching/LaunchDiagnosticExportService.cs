/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO;
using System.IO.Compression;
using System.Text;
using Launcher.Application.Services;
using Microsoft.Extensions.Logging;

namespace Launcher.Infrastructure.Minecraft;

public sealed class LaunchDiagnosticExportService : ILaunchDiagnosticExportService
{
    private readonly ILogger<LaunchDiagnosticExportService> logger;

    public LaunchDiagnosticExportService(ILogger<LaunchDiagnosticExportService> logger)
    {
        this.logger = logger;
    }

    public async Task<LaunchDiagnosticExportResult> ExportAsync(
        LaunchDiagnosticExportRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.OutputArchivePath))
        {
            return new LaunchDiagnosticExportResult(
                false,
                LaunchDiagnosticExportFailureReason.FileSystemError);
        }

        string destinationPath;
        string temporaryPath;
        try
        {
            destinationPath = Path.GetFullPath(request.OutputArchivePath);
            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (string.IsNullOrWhiteSpace(destinationDirectory))
            {
                return new LaunchDiagnosticExportResult(
                    false,
                    LaunchDiagnosticExportFailureReason.FileSystemError);
            }

            Directory.CreateDirectory(destinationDirectory);
            temporaryPath = Path.Combine(
                destinationDirectory,
                $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            logger.LogWarning(exception, "Failed to prepare launch diagnostic export destination.");
            return new LaunchDiagnosticExportResult(false, LaunchDiagnosticExportFailureReason.FileSystemError);
        }

        try
        {
            var outcomes = new List<ExportOutcome>();
            var usedEntryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var uniqueDiagnostics = NormalizeDiagnostics(request.Diagnostics, outcomes);
            var exportedFileCount = 0;

            await using (var output = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.ReadWrite,
                             FileShare.None,
                             81920,
                             useAsync: true))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false))
            {
                foreach (var diagnostic in uniqueDiagnostics)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entryName = ResolveUniqueEntryName(diagnostic, usedEntryNames);
                    var outcome = await TryAddDiagnosticAsync(
                        archive,
                        diagnostic,
                        entryName,
                        Path.GetDirectoryName(temporaryPath)!,
                        cancellationToken);
                    outcomes.Add(outcome);
                    if (outcome.IsExported)
                        exportedFileCount++;
                }

                await WriteIndexAsync(
                    archive,
                    request,
                    outcomes,
                    cancellationToken);
            }

            var skippedFileCount = outcomes.Count(outcome => !outcome.IsExported);
            if (exportedFileCount == 0)
            {
                TryDelete(temporaryPath);
                return new LaunchDiagnosticExportResult(
                    false,
                    LaunchDiagnosticExportFailureReason.NoReadableDiagnostics,
                    ExportedFileCount: 0,
                    SkippedFileCount: skippedFileCount);
            }

            File.Move(temporaryPath, destinationPath, overwrite: true);
            logger.LogInformation(
                "Launch diagnostic export completed. InstanceName={InstanceName} VersionName={VersionName} ExportedFileCount={ExportedFileCount} SkippedFileCount={SkippedFileCount} OutputArchivePath={OutputArchivePath}",
                request.InstanceName,
                request.VersionName,
                exportedFileCount,
                skippedFileCount,
                destinationPath);
            return new LaunchDiagnosticExportResult(
                true,
                OutputArchivePath: destinationPath,
                ExportedFileCount: exportedFileCount,
                SkippedFileCount: skippedFileCount);
        }
        catch (OperationCanceledException)
        {
            TryDelete(temporaryPath);
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDelete(temporaryPath);
            logger.LogWarning(exception, "Failed to write launch diagnostic export archive.");
            return new LaunchDiagnosticExportResult(false, LaunchDiagnosticExportFailureReason.FileSystemError);
        }
        catch (Exception exception)
        {
            TryDelete(temporaryPath);
            logger.LogError(exception, "Unexpected launch diagnostic export failure.");
            return new LaunchDiagnosticExportResult(false, LaunchDiagnosticExportFailureReason.UnexpectedError);
        }
    }

    private static IReadOnlyList<LaunchDiagnosticReference> NormalizeDiagnostics(
        IReadOnlyList<LaunchDiagnosticReference> diagnostics,
        ICollection<ExportOutcome> outcomes)
    {
        var uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<LaunchDiagnosticReference>();
        foreach (var diagnostic in diagnostics)
        {
            try
            {
                var fullPath = Path.GetFullPath(diagnostic.Path);
                if (uniquePaths.Add(fullPath))
                    normalized.Add(diagnostic with { Path = fullPath });
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                outcomes.Add(new ExportOutcome(
                    diagnostic.Type,
                    SafeFileName(diagnostic.Path),
                    null,
                    false,
                    "invalid-path"));
            }
        }

        return normalized;
    }

    private static async Task<ExportOutcome> TryAddDiagnosticAsync(
        ZipArchive archive,
        LaunchDiagnosticReference diagnostic,
        string entryName,
        string stagingDirectory,
        CancellationToken cancellationToken)
    {
        var stagingPath = Path.Combine(stagingDirectory, $".launch-diagnostic-{Guid.NewGuid():N}.tmp");
        try
        {
            try
            {
                await using var source = new FileStream(
                    diagnostic.Path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    81920,
                    useAsync: true);
                await using var staging = new FileStream(
                    stagingPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    useAsync: true);
                await source.CopyToAsync(staging, cancellationToken);
            }
            catch (FileNotFoundException)
            {
                return CreateSkippedOutcome(diagnostic, "missing");
            }
            catch (DirectoryNotFoundException)
            {
                return CreateSkippedOutcome(diagnostic, "missing");
            }
            catch (UnauthorizedAccessException)
            {
                return CreateSkippedOutcome(diagnostic, "access-denied");
            }
            catch (IOException)
            {
                return CreateSkippedOutcome(diagnostic, "io-error");
            }

            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            await using var stagedSource = new FileStream(
                stagingPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                useAsync: true);
            await using var destination = entry.Open();
            await stagedSource.CopyToAsync(destination, cancellationToken);
            return new ExportOutcome(
                diagnostic.Type,
                Path.GetFileName(diagnostic.Path),
                entryName,
                true,
                null);
        }
        finally
        {
            TryDelete(stagingPath);
        }
    }

    private static async Task WriteIndexAsync(
        ZipArchive archive,
        LaunchDiagnosticExportRequest request,
        IReadOnlyList<ExportOutcome> outcomes,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry("report-index.txt", CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteLineAsync($"CreatedAtUtc: {DateTimeOffset.UtcNow:O}");
        await writer.WriteLineAsync($"InstanceName: {request.InstanceName}");
        await writer.WriteLineAsync($"VersionName: {request.VersionName}");
        await writer.WriteLineAsync();
        await writer.WriteLineAsync("[Diagnostics]");
        foreach (var outcome in outcomes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(
                $"Type={outcome.Type}; Status={(outcome.IsExported ? "exported" : "skipped")}; FileName={outcome.FileName}; Entry={outcome.EntryName ?? "none"}; Reason={outcome.Reason ?? "none"}");
        }
    }

    private static ExportOutcome CreateSkippedOutcome(
        LaunchDiagnosticReference diagnostic,
        string reason)
    {
        return new ExportOutcome(
            diagnostic.Type,
            Path.GetFileName(diagnostic.Path),
            null,
            false,
            reason);
    }

    private static string ResolveUniqueEntryName(
        LaunchDiagnosticReference diagnostic,
        ISet<string> usedEntryNames)
    {
        var fileName = SanitizeFileName(Path.GetFileName(diagnostic.Path));
        var directory = diagnostic.Type switch
        {
            LaunchDiagnosticType.MinecraftCrashReport => "minecraft/crash-reports",
            LaunchDiagnosticType.JvmCrashReport => "jvm",
            LaunchDiagnosticType.MinecraftLatestLog => "minecraft/logs",
            LaunchDiagnosticType.CapturedOutput => "launcher/captured-output",
            _ => "launcher/diagnostics"
        };
        var candidate = $"{directory}/{fileName}";
        if (usedEntryNames.Add(candidate))
            return candidate;

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var index = 2; ; index++)
        {
            candidate = $"{directory}/{baseName} ({index}){extension}";
            if (usedEntryNames.Add(candidate))
                return candidate;
        }
    }

    private static string SanitizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "diagnostic.log";

        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(fileName
            .Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "diagnostic.log" : sanitized;
    }

    private static string SafeFileName(string? path)
    {
        try
        {
            return SanitizeFileName(Path.GetFileName(path));
        }
        catch (ArgumentException)
        {
            return "diagnostic.log";
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record ExportOutcome(
        LaunchDiagnosticType Type,
        string FileName,
        string? EntryName,
        bool IsExported,
        string? Reason);
}
