/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Launcher.Application;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Persistence;

internal static class PendingInstanceInstallDirectory
{
    public const string Prefix = ".bhl-install-pending-";
    public const string MarkerFileName = ".bhl-install-pending.json";
    public const string PendingLockFileName = ".bhl-install-active.lock";
    private static readonly JsonSerializerOptions MarkerJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static bool IsPending(string path) =>
        Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    public static bool IsLogicalNameReserved(string versionsDirectory, string logicalVersionName)
    {
        if (!Directory.Exists(versionsDirectory))
            return false;
        foreach (var directory in Directory.EnumerateDirectories(versionsDirectory).Where(IsPending))
        {
            if (TryGetLogicalName(directory, out var reservedName)
                && string.Equals(reservedName, logicalVersionName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static bool TryGetLogicalName(string pendingDirectory, out string logicalVersionName)
    {
        logicalVersionName = string.Empty;
        if (!TryReadValidPendingMarker(pendingDirectory, out var marker))
            return false;
        logicalVersionName = marker.LogicalVersionName;
        return true;
    }

    public static bool TryReadValidPendingMarker(
        string pendingDirectory,
        out PendingInstanceInstallMarker marker)
    {
        marker = default!;
        try
        {
            var markerPath = Path.Combine(pendingDirectory, MarkerFileName);
            if (!File.Exists(markerPath))
                return false;
            var parsed = JsonSerializer.Deserialize<PendingInstanceInstallMarker>(
                File.ReadAllText(markerPath),
                MarkerJsonOptions);
            if (parsed is null
                || parsed.SchemaVersion != 1
                || string.IsNullOrWhiteSpace(parsed.InstanceId)
                || string.IsNullOrWhiteSpace(parsed.LogicalVersionName)
                || !Guid.TryParseExact(parsed.TransactionId, "N", out _))
                return false;
            var expectedName = $"{Prefix}{parsed.LogicalVersionName}-{parsed.TransactionId[..8].ToLowerInvariant()}";
            if (!string.Equals(Path.GetFileName(pendingDirectory), expectedName, StringComparison.OrdinalIgnoreCase))
                return false;
            marker = parsed;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }
}

internal sealed record PendingInstanceInstallMarker(
    int SchemaVersion,
    string TransactionId,
    string InstanceId,
    string LogicalVersionName,
    string InstallKind,
    bool InitializeDefaultIfEmpty,
    DateTimeOffset CreatedAtUtc);

internal static class CrossProcessVersionLock
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);
    private const string LegacyInstallLockFileName = ".bhl-install-transaction.lock";
    private const string LegacyMutationLockFileName = ".bhl-version-mutation.lock";

    public static string GetInstallCoordinationPath(string minecraftDirectory) =>
        GetPath(minecraftDirectory, "install");

    public static string GetMutationPath(string minecraftDirectory) =>
        GetPath(minecraftDirectory, "mutation");

    public static void DeleteLegacyVersionDirectoryLocks(string versionsDirectory)
    {
        TryDelete(Path.Combine(versionsDirectory, LegacyInstallLockFileName));
        TryDelete(Path.Combine(versionsDirectory, LegacyMutationLockFileName));
    }

    public static async Task<FileStream> AcquireAsync(
        string path,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken)
    {
        var queued = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException exception) when (IsSharingViolation(exception))
            {
                if (!queued)
                {
                    progress?.Report(new LauncherProgress(InstallProgressStages.Queue, string.Empty));
                    queued = true;
                }
                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public static FileStream? TryAcquire(string path)
    {
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException exception) when (IsSharingViolation(exception))
        {
            return null;
        }
    }

    private static bool IsSharingViolation(IOException exception)
    {
        var code = exception.HResult & 0xFFFF;
        return code is 32 or 33;
    }

    private static string GetPath(string minecraftDirectory, string lockKind)
    {
        var normalizedDirectory = Path.GetFullPath(minecraftDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedDirectory)))
            .ToLowerInvariant();
        var lockDirectory = Path.Combine(
            AppContext.BaseDirectory,
            LauncherApplicationIdentity.StorageDirectoryName,
            "locks",
            "versions");
        Directory.CreateDirectory(lockDirectory);
        return Path.Combine(lockDirectory, $"{hash}.{lockKind}.lock");
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

public sealed class InstanceInstallTransactionService : IInstanceInstallTransactionService
{
    private const int MaxPendingNameAttempts = 3;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly GameInstanceSettingsStore settingsStore;
    private readonly VersionDirectoryManager directoryManager;
    private readonly ILogger logger;
    private readonly Func<Guid> guidFactory;

    public InstanceInstallTransactionService(
        ILogger<InstanceInstallTransactionService>? logger = null)
        : this(logger, null)
    {
    }

    internal InstanceInstallTransactionService(
        ILogger? logger,
        Func<Guid>? guidFactory)
    {
        this.logger = logger ?? NullLogger.Instance;
        this.guidFactory = guidFactory ?? Guid.NewGuid;
        settingsStore = new GameInstanceSettingsStore(this.logger);
        directoryManager = new VersionDirectoryManager(this.logger);
    }

    public async Task<IInstanceInstallTransaction> BeginAsync(
        string minecraftDirectory,
        string logicalVersionName,
        string instanceId,
        string installKind,
        bool initializeDefaultIfEmpty,
        IProgress<LauncherProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var finalDirectory = directoryManager.GetVersionDirectory(minecraftDirectory, logicalVersionName);
        var versionsDirectory = Path.GetDirectoryName(finalDirectory)!;
        Directory.CreateDirectory(versionsDirectory);
        await using var coordinationLock = await CrossProcessVersionLock.AcquireAsync(
            CrossProcessVersionLock.GetInstallCoordinationPath(minecraftDirectory),
            progress,
            cancellationToken).ConfigureAwait(false);
        await using var mutationLock = await CrossProcessVersionLock.AcquireAsync(
            CrossProcessVersionLock.GetMutationPath(minecraftDirectory),
            progress: null,
            cancellationToken).ConfigureAwait(false);
        if (Directory.Exists(finalDirectory)
            || File.Exists(finalDirectory)
            || PendingInstanceInstallDirectory.IsLogicalNameReserved(versionsDirectory, logicalVersionName))
        {
            throw new InstanceInstallNameConflictException(logicalVersionName);
        }

        for (var attempt = 1; attempt <= MaxPendingNameAttempts; attempt++)
        {
            var transactionId = guidFactory().ToString("N");
            var pendingDirectory = Path.Combine(
                versionsDirectory,
                $"{PendingInstanceInstallDirectory.Prefix}{logicalVersionName}-{transactionId[..8].ToLowerInvariant()}");
            if (Directory.Exists(pendingDirectory))
                continue;

            Directory.CreateDirectory(pendingDirectory);
            FileStream? pendingLock = null;
            try
            {
                pendingLock = new FileStream(
                    Path.Combine(pendingDirectory, PendingInstanceInstallDirectory.PendingLockFileName),
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.Read | FileShare.Delete);
                var marker = new PendingInstanceInstallMarker(
                    1,
                    transactionId,
                    instanceId,
                    logicalVersionName,
                    installKind,
                    initializeDefaultIfEmpty,
                    DateTimeOffset.UtcNow);
                await AtomicJsonFileWriter.WriteAsync(
                    Path.Combine(pendingDirectory, PendingInstanceInstallDirectory.MarkerFileName),
                    marker,
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
                logger.LogInformation(
                    "Instance installation staged. InstanceId={InstanceId} LogicalVersionName={LogicalVersionName} PendingDirectory={PendingDirectory}",
                    instanceId,
                    logicalVersionName,
                    pendingDirectory);
                return new Transaction(
                    minecraftDirectory,
                    logicalVersionName,
                    pendingDirectory,
                    finalDirectory,
                    pendingLock,
                    settingsStore,
                    logger);
            }
            catch
            {
                if (pendingLock is not null)
                    await pendingLock.DisposeAsync().ConfigureAwait(false);
                TryDeleteTree(pendingDirectory, logger);
                throw;
            }
        }

        throw new IOException($"Unable to allocate an install staging directory after {MaxPendingNameAttempts} attempts.");
    }

    private sealed class Transaction : IInstanceInstallTransaction
    {
        private FileStream? pendingLock;
        private readonly GameInstanceSettingsStore settingsStore;
        private readonly ILogger logger;
        private bool aborted;

        public Transaction(
            string minecraftDirectory,
            string logicalVersionName,
            string pendingDirectory,
            string finalDirectory,
            FileStream pendingLock,
            GameInstanceSettingsStore settingsStore,
            ILogger logger)
        {
            MinecraftDirectory = minecraftDirectory;
            LogicalVersionName = logicalVersionName;
            PendingDirectory = pendingDirectory;
            FinalDirectory = finalDirectory;
            this.pendingLock = pendingLock;
            this.settingsStore = settingsStore;
            this.logger = logger;
        }

        public string MinecraftDirectory { get; }
        public string LogicalVersionName { get; }
        public string PendingDirectory { get; }
        public string FinalDirectory { get; }
        public bool IsCommitted { get; private set; }

        public async Task CommitAsync(GameInstance instance, CancellationToken cancellationToken = default)
        {
            if (IsCommitted)
                return;
            if (aborted)
                throw new InvalidOperationException("The installation transaction was already aborted.");

            await ValidateAsync(cancellationToken).ConfigureAwait(false);
            await settingsStore.PrepareInstallAsync(
                PendingDirectory,
                FinalDirectory,
                LogicalVersionName,
                instance,
                cancellationToken).ConfigureAwait(false);

            var versionsDirectory = Path.GetDirectoryName(FinalDirectory)!;
            await using var coordinationLock = await CrossProcessVersionLock.AcquireAsync(
                CrossProcessVersionLock.GetInstallCoordinationPath(MinecraftDirectory),
                progress: null,
                cancellationToken).ConfigureAwait(false);
            await using var mutationLock = await CrossProcessVersionLock.AcquireAsync(
                CrossProcessVersionLock.GetMutationPath(MinecraftDirectory),
                progress: null,
                cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(FinalDirectory) || File.Exists(FinalDirectory))
                throw new InstanceInstallNameConflictException(LogicalVersionName);

            await ReleasePendingLockAsync().ConfigureAwait(false);
            Directory.Move(PendingDirectory, FinalDirectory);
            IsCommitted = true;
            logger.LogInformation(
                "Instance installation committed. InstanceId={InstanceId} LogicalVersionName={LogicalVersionName} FinalDirectory={FinalDirectory}",
                instance.Id,
                LogicalVersionName,
                FinalDirectory);
        }

        public Task CompleteLogicalCommitAsync(CancellationToken cancellationToken = default)
        {
            if (!IsCommitted)
                throw new InvalidOperationException("The installation directory has not been committed.");
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                File.Delete(Path.Combine(FinalDirectory, PendingInstanceInstallDirectory.PendingLockFileName));
                File.Delete(Path.Combine(FinalDirectory, PendingInstanceInstallDirectory.MarkerFileName));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "Committed instance install marker remains for startup cleanup. FinalDirectory={FinalDirectory}", FinalDirectory);
            }
            return Task.CompletedTask;
        }

        public async Task AbortAsync(CancellationToken cancellationToken = default)
        {
            if (IsCommitted || aborted)
                return;
            var versionsDirectory = Path.GetDirectoryName(FinalDirectory)!;
            await using var coordinationLock = await CrossProcessVersionLock.AcquireAsync(
                CrossProcessVersionLock.GetInstallCoordinationPath(MinecraftDirectory),
                progress: null,
                cancellationToken).ConfigureAwait(false);
            await using var mutationLock = await CrossProcessVersionLock.AcquireAsync(
                CrossProcessVersionLock.GetMutationPath(MinecraftDirectory),
                progress: null,
                cancellationToken).ConfigureAwait(false);
            await ReleasePendingLockAsync().ConfigureAwait(false);
            TryDeleteTree(PendingDirectory, logger);
            aborted = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (!IsCommitted && !aborted)
            {
                try
                {
                    await AbortAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Failed to abort an incomplete instance installation. PendingDirectory={PendingDirectory}", PendingDirectory);
                }
            }
            await ReleasePendingLockAsync().ConfigureAwait(false);
        }

        private async Task ReleasePendingLockAsync()
        {
            if (pendingLock is null)
                return;
            await pendingLock.DisposeAsync().ConfigureAwait(false);
            pendingLock = null;
        }

        private async Task ValidateAsync(CancellationToken cancellationToken)
        {
            if (!Directory.Exists(PendingDirectory))
                throw new DirectoryNotFoundException($"Install staging directory is missing: {PendingDirectory}");
            if ((File.GetAttributes(PendingDirectory) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Install staging directory must not be a reparse point.");
            EnsureNoReparsePoints(PendingDirectory);

            var jsonPath = Path.Combine(PendingDirectory, $"{LogicalVersionName}.json");
            if (!File.Exists(jsonPath))
                throw new FileNotFoundException("Installed version JSON is missing.", jsonPath);
            await using var stream = File.OpenRead(jsonPath);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!json.RootElement.TryGetProperty("id", out var id)
                || !string.Equals(id.GetString(), LogicalVersionName, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Installed version JSON id does not match the logical version name.");
            }
        }

        private static void EnsureNoReparsePoints(string directory)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"Install staging content must not contain reparse points: {entry}");
                if ((attributes & FileAttributes.Directory) != 0)
                    EnsureNoReparsePoints(entry);
            }
        }
    }

    internal static void TryDeleteTree(string directory, ILogger logger)
    {
        try
        {
            DeleteTree(directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Failed to delete install staging directory. Directory={Directory}", directory);
        }
    }

    private static void DeleteTree(string path)
    {
        if (!Directory.Exists(path))
            return;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            Directory.Delete(path, recursive: false);
            return;
        }
        foreach (var child in Directory.EnumerateDirectories(path))
            DeleteTree(child);
        foreach (var file in Directory.EnumerateFiles(path))
            File.Delete(file);
        Directory.Delete(path, recursive: false);
    }
}

public sealed class InstanceInstallCleanupService(
    ISettingsService settingsService,
    ILogger<InstanceInstallCleanupService>? logger = null) : IInstanceInstallCleanupService
{
    private static readonly JsonSerializerOptions MarkerJsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly ILogger logger = logger ?? NullLogger<InstanceInstallCleanupService>.Instance;

    public async Task CleanupPendingAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
        var versionsDirectory = Path.GetFullPath(Path.Combine(settings.MinecraftDirectory, "versions"));
        if (!Directory.Exists(versionsDirectory))
            return;

        await using var coordinationLock = await CrossProcessVersionLock.AcquireAsync(
            CrossProcessVersionLock.GetInstallCoordinationPath(settings.MinecraftDirectory),
            progress: null,
            cancellationToken).ConfigureAwait(false);
        await using var mutationLock = await CrossProcessVersionLock.AcquireAsync(
            CrossProcessVersionLock.GetMutationPath(settings.MinecraftDirectory),
            progress: null,
            cancellationToken).ConfigureAwait(false);
        CrossProcessVersionLock.DeleteLegacyVersionDirectoryLocks(versionsDirectory);

        foreach (var directory in Directory.EnumerateDirectories(versionsDirectory).ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalized = Path.GetFullPath(directory);
            if (!string.Equals(Path.GetDirectoryName(normalized), versionsDirectory, StringComparison.OrdinalIgnoreCase))
                continue;
            if (PendingInstanceInstallDirectory.IsPending(normalized))
            {
                if (!PendingInstanceInstallDirectory.TryReadValidPendingMarker(normalized, out _))
                {
                    logger.LogWarning(
                        "Install staging directory was preserved because its transaction marker is missing or invalid. Directory={Directory}",
                        normalized);
                    continue;
                }
                var activeLock = CrossProcessVersionLock.TryAcquire(
                    Path.Combine(normalized, PendingInstanceInstallDirectory.PendingLockFileName));
                if (activeLock is null)
                {
                    logger.LogDebug("Skipping active instance installation staging directory. Directory={Directory}", normalized);
                    continue;
                }
                await activeLock.DisposeAsync().ConfigureAwait(false);
                InstanceInstallTransactionService.TryDeleteTree(normalized, logger);
                continue;
            }

            var markerPath = Path.Combine(normalized, PendingInstanceInstallDirectory.MarkerFileName);
            if (!File.Exists(markerPath))
                continue;
            try
            {
                await using (var markerStream = File.OpenRead(markerPath))
                {
                    var marker = await JsonSerializer.DeserializeAsync<PendingInstanceInstallMarker>(
                        markerStream,
                        MarkerJsonOptions,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    if (marker is not null && marker.InitializeDefaultIfEmpty)
                    {
                        var latestSettings = await settingsService.LoadAsync(CancellationToken.None).ConfigureAwait(false);
                        if (string.IsNullOrWhiteSpace(latestSettings.DefaultInstanceId))
                        {
                            latestSettings.DefaultInstanceId = marker.InstanceId;
                            await settingsService.SaveAsync(latestSettings, CancellationToken.None).ConfigureAwait(false);
                        }
                    }
                }
                File.Delete(Path.Combine(normalized, PendingInstanceInstallDirectory.PendingLockFileName));
                File.Delete(markerPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                logger.LogWarning(exception, "Failed to delete committed install marker. MarkerPath={MarkerPath}", markerPath);
            }
        }
    }
}
