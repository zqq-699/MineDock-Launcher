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

public sealed class LaunchDiagnosticExportService : ILaunchDiagnosticExportService
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
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
            {
                outcomes.Add(new ExportOutcome(
                    diagnostic.Type,
                    SafeFileName(diagnostic.Path),
                    null,
                    false,
                    "invalid-path",
                    0));
            }
        }

        return normalized;
    }

    private static async Task<ExportOutcome> TryAddDiagnosticAsync(
        ZipArchive archive,
        LaunchDiagnosticReference diagnostic,
        string entryName,
        string stagingDirectory,
        LaunchDiagnosticExportLimits limits,
        long remainingTotalBytes,
        CancellationToken cancellationToken)
    {
        var stagingPath = Path.Combine(stagingDirectory, $".launch-diagnostic-{Guid.NewGuid():N}.tmp");
        try
        {
            if (!TryValidateSafePath(diagnostic.Path, out var validationReason))
                return CreateSkippedOutcome(diagnostic, validationReason);
            if (remainingTotalBytes <= 0)
                return CreateSkippedOutcome(diagnostic, "total-size-limit");

            long initialLength;
            DateTime initialLastWriteTimeUtc;
            DateTime initialCreationTimeUtc;
            FileIdentity initialIdentity;
            try
            {
                await using var source = new FileStream(
                    diagnostic.Path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    81920,
                    useAsync: true);
                if (!TryValidateSafePath(diagnostic.Path, out validationReason))
                    return CreateSkippedOutcome(diagnostic, validationReason);

                initialLength = source.Length;
                initialLastWriteTimeUtc = File.GetLastWriteTimeUtc(diagnostic.Path);
                initialCreationTimeUtc = File.GetCreationTimeUtc(diagnostic.Path);
                if (!TryGetFileIdentity(source.SafeFileHandle, out initialIdentity))
                    return CreateSkippedOutcome(diagnostic, "io-error");
                if (initialLength > limits.MaxSourceFileBytes)
                    return CreateSkippedOutcome(diagnostic, "file-too-large");
                if (initialLength > remainingTotalBytes)
                    return CreateSkippedOutcome(diagnostic, "total-size-limit");

                await using var staging = new FileStream(
                    stagingPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    useAsync: true);
                var buffer = new byte[81920];
                long copiedBytes = 0;
                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                        break;
                    copiedBytes += read;
                    if (copiedBytes > limits.MaxSourceFileBytes)
                        return CreateSkippedOutcome(diagnostic, "file-too-large");
                    if (copiedBytes > remainingTotalBytes)
                        return CreateSkippedOutcome(diagnostic, "total-size-limit");
                    await staging.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                await staging.FlushAsync(cancellationToken);
                await using var verificationSource = new FileStream(
                    diagnostic.Path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    1,
                    useAsync: false);
                if (copiedBytes != initialLength
                    || source.Length != initialLength
                    || File.GetLastWriteTimeUtc(diagnostic.Path) != initialLastWriteTimeUtc
                    || File.GetCreationTimeUtc(diagnostic.Path) != initialCreationTimeUtc
                    || !TryGetFileIdentity(verificationSource.SafeFileHandle, out var currentIdentity)
                    || currentIdentity != initialIdentity
                    || !TryValidateSafePath(diagnostic.Path, out validationReason))
                {
                    return CreateSkippedOutcome(
                        diagnostic,
                        validationReason == "unsafe-reparse-point" ? validationReason : "source-changed");
                }
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
                null,
                initialLength);
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
        await writer.WriteLineAsync($"InstanceName: {NormalizeIndexValue(request.InstanceName)}");
        await writer.WriteLineAsync($"VersionName: {NormalizeIndexValue(request.VersionName)}");
        await writer.WriteLineAsync();
        await writer.WriteLineAsync("[Diagnostics]");
        foreach (var outcome in outcomes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(
                $"Type={outcome.Type}; Status={(outcome.IsExported ? "exported" : "skipped")}; FileName={NormalizeIndexValue(outcome.FileName)}; Entry={NormalizeIndexValue(outcome.EntryName ?? "none")}; Reason={NormalizeIndexValue(outcome.Reason ?? "none")}");
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
            reason,
            0);
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

    private static bool TryValidateSafePath(string path, out string reason)
    {
        reason = "none";
        try
        {
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrWhiteSpace(root))
            {
                reason = "invalid-path";
                return false;
            }

            var current = root;
            var relative = Path.GetRelativePath(root, fullPath);
            foreach (var component in relative.Split(
                         [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                         StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, component);
                if (!File.Exists(current) && !Directory.Exists(current))
                {
                    reason = "missing";
                    return false;
                }

                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    reason = "unsafe-reparse-point";
                    return false;
                }
            }

            if (!File.Exists(fullPath))
            {
                reason = "missing";
                return false;
            }

            return true;
        }
        catch (UnauthorizedAccessException)
        {
            reason = "access-denied";
            return false;
        }
        catch (IOException)
        {
            reason = "io-error";
            return false;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            reason = "invalid-path";
            return false;
        }
    }

    private static string NormalizeIndexValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = string.Join(
            " ",
            value.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return normalized.Length <= 512 ? normalized : normalized[..512] + "…";
    }

    private static bool TryGetFileIdentity(SafeFileHandle handle, out FileIdentity identity)
    {
        identity = default;
        if (!GetFileInformationByHandle(handle, out var information))
            return false;

        identity = new FileIdentity(
            information.VolumeSerialNumber,
            ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow);
        return true;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle fileHandle,
        out ByHandleFileInformation fileInformation);

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
        string? Reason,
        long SourceBytes);

    private readonly record struct FileIdentity(uint VolumeSerialNumber, ulong FileIndex);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}

internal sealed record LaunchDiagnosticExportLimits(
    long MaxSourceFileBytes,
    long MaxTotalSourceBytes)
{
    public static LaunchDiagnosticExportLimits Default { get; } = new(
        128L * 1024 * 1024,
        256L * 1024 * 1024);
}
