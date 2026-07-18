/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Launcher.Application.Services;
using Microsoft.Win32.SafeHandles;
using Microsoft.Extensions.Logging;

namespace Launcher.Infrastructure.Minecraft;

public sealed partial class LaunchDiagnosticExportService : ILaunchDiagnosticExportService
{
    private readonly ILogger<LaunchDiagnosticExportService> logger;
    private readonly LaunchDiagnosticExportLimits limits;

    public LaunchDiagnosticExportService(ILogger<LaunchDiagnosticExportService> logger)
        : this(logger, LaunchDiagnosticExportLimits.Default)
    {
    }

    internal LaunchDiagnosticExportService(
        ILogger<LaunchDiagnosticExportService> logger,
        LaunchDiagnosticExportLimits limits)
    {
        this.logger = logger;
        this.limits = limits;
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
            long exportedSourceBytes = 0;

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
                        limits,
                        Math.Max(0, limits.MaxTotalSourceBytes - exportedSourceBytes),
                        request.SensitiveValues,
                        cancellationToken);
                    outcomes.Add(outcome);
                    if (outcome.IsExported)
                    {
                        exportedFileCount++;
                        exportedSourceBytes += outcome.SourceBytes;
                    }
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
                "Launch diagnostic export completed. InstanceName={InstanceName} VersionName={VersionName} ExportedFileCount={ExportedFileCount} SkippedFileCount={SkippedFileCount}",
                request.InstanceName,
                request.VersionName,
                exportedFileCount,
                skippedFileCount);
            logger.LogDebug("Launch diagnostic export path resolved. OutputArchivePath={OutputArchivePath}", destinationPath);
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
}
