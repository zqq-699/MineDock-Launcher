using System.Collections.ObjectModel;
using System.IO;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.GameSettings;

public sealed class LocalModsViewModel : IDisposable
{
    private const string EnabledModExtension = ".jar";
    private const string DisabledModExtension = ".jar.disabled";
    private static readonly TimeSpan IgnoredWatcherPathTtl = TimeSpan.FromSeconds(2);
    private readonly IModService modService;
    private readonly ILocalModIconEnrichmentService? iconEnrichmentService;
    private readonly IStatusService statusService;
    private readonly IUiDispatcher uiDispatcher;
    private readonly ILogger<LocalModsViewModel> logger;
    private readonly object ignoredWatcherPathsLock = new();
    private readonly Dictionary<string, DateTimeOffset> ignoredWatcherPaths = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? modsWatcher;
    private CancellationTokenSource? refreshCancellationTokenSource;
    private CancellationTokenSource? iconEnrichmentCancellationTokenSource;
    private CancellationTokenSource? watcherRefreshCancellationTokenSource;
    private GameInstance? selectedInstance;
    private IReadOnlyList<LocalMod> currentMods = Array.Empty<LocalMod>();
    private bool watcherEnabled;
    private bool watcherSuspendedForRename;
    private int modRefreshVersion;

    public LocalModsViewModel(
        IModService modService,
        IStatusService statusService,
        IUiDispatcher? uiDispatcher = null,
        ILocalModIconEnrichmentService? iconEnrichmentService = null,
        ILogger<LocalModsViewModel>? logger = null)
    {
        this.modService = modService;
        this.iconEnrichmentService = iconEnrichmentService;
        this.statusService = statusService;
        this.uiDispatcher = uiDispatcher ?? ImmediateUiDispatcher.Instance;
        this.logger = logger ?? NullLogger<LocalModsViewModel>.Instance;
    }

    public event EventHandler? ModsChanged;

    public ObservableCollection<LocalMod> Mods { get; } = [];

    public IReadOnlyList<LocalMod> CurrentMods => currentMods;

    public void SetSelectedInstance(GameInstance? instance)
    {
        selectedInstance = instance;
        Interlocked.Increment(ref modRefreshVersion);
        CancelRefresh();
        CancelIconEnrichment();
        ResetWatcher();
        ClearMods();
        logger.LogInformation(
            "Selected instance changed for local mods view. InstanceId={InstanceId}",
            instance?.Id ?? "<none>");
    }

    public void SetWatcherEnabled(bool enabled)
    {
        watcherEnabled = enabled;
        ResetWatcher();
    }

    public void SuspendWatcherForInstanceRename()
    {
        watcherSuspendedForRename = true;
        ResetWatcher();
        CancelRefresh();
    }

    public void ResumeWatcherAfterInstanceRename()
    {
        if (!watcherSuspendedForRename)
            return;

        watcherSuspendedForRename = false;
        ResetWatcher();
    }

    public Task SetSelectedInstanceAsync(GameInstance? instance)
    {
        SetSelectedInstance(instance);
        SetWatcherEnabled(true);
        return RefreshModsAsync();
    }

    public async Task RefreshModsAsync()
    {
        var refreshVersion = Interlocked.Increment(ref modRefreshVersion);
        var refreshCts = ReplaceRefreshCancellationTokenSource();
        var instance = selectedInstance;

        if (instance is null)
        {
            ClearMods();
            logger.LogInformation("Local mods view cleared because no instance is selected.");
            return;
        }

        IReadOnlyList<LocalMod> loadedMods;
        try
        {
            loadedMods = await modService.GetModsAsync(instance, refreshCts.Token);
        }
        catch (OperationCanceledException) when (refreshCts.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to load local mods. InstanceId={InstanceId}",
                instance.Id);
            throw;
        }
        finally
        {
            ReleaseRefreshCancellationTokenSource(refreshCts);
        }

        if (refreshVersion != modRefreshVersion
            || !string.Equals(instance.Id, selectedInstance?.Id, StringComparison.Ordinal))
        {
            return;
        }

        uiDispatcher.Invoke(() =>
        {
            currentMods = loadedMods;
            Mods.ReplaceWith(loadedMods);
            ModsChanged?.Invoke(this, EventArgs.Empty);
        });
        logger.LogInformation(
            "Local mods view refreshed. InstanceId={InstanceId} Count={ModCount}",
            instance.Id,
            Mods.Count);
        QueueRemoteIconEnrichment(instance, loadedMods, refreshVersion);
    }

    public async Task ToggleModAsync(LocalMod mod)
    {
        ArgumentNullException.ThrowIfNull(mod);

        var enabled = !mod.IsEnabled;
        var sourcePath = mod.FullPath;
        var targetPath = GetPathForEnabledState(sourcePath, enabled);
        IgnoreWatcherPaths(sourcePath, targetPath);

        try
        {
            await modService.SetEnabledAsync(mod, enabled);
        }
        catch
        {
            RemoveIgnoredWatcherPaths(sourcePath, targetPath);
            throw;
        }

        ApplyEnabledStateLocally(mod, targetPath, enabled, raiseChanged: true);
    }

    public async Task DeleteModAsync(LocalMod mod)
    {
        await modService.DeleteAsync(mod);
        await RefreshModsAsync();
    }

    public async Task<int> SetModsEnabledAsync(IEnumerable<LocalMod> mods, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(mods);

        var failedCount = 0;
        var appliedUpdates = new List<(LocalMod Mod, string TargetPath)>();
        foreach (var mod in mods
                     .Where(mod => mod.IsEnabled != enabled)
                     .DistinctBy(mod => mod.FullPath, StringComparer.OrdinalIgnoreCase))
        {
            var sourcePath = mod.FullPath;
            var targetPath = GetPathForEnabledState(sourcePath, enabled);
            IgnoreWatcherPaths(sourcePath, targetPath);
            try
            {
                await modService.SetEnabledAsync(mod, enabled);
                appliedUpdates.Add((mod, targetPath));
            }
            catch (Exception exception)
            {
                RemoveIgnoredWatcherPaths(sourcePath, targetPath);
                failedCount++;
                logger.LogWarning(
                    exception,
                    "Failed to change local mod enabled state. Path={Path} Enabled={Enabled}",
                    mod.FullPath,
                    enabled);
            }
        }

        if (appliedUpdates.Count > 0)
        {
            uiDispatcher.Invoke(() =>
            {
                foreach (var (mod, targetPath) in appliedUpdates)
                    ApplyEnabledStateLocallyCore(mod, targetPath, enabled);

                ModsChanged?.Invoke(this, EventArgs.Empty);
            });
        }

        return failedCount;
    }

    public async Task<int> DeleteModsAsync(IEnumerable<LocalMod> mods)
    {
        ArgumentNullException.ThrowIfNull(mods);

        var failedCount = 0;
        foreach (var mod in mods.DistinctBy(mod => mod.FullPath, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                await modService.DeleteAsync(mod);
            }
            catch (Exception exception)
            {
                failedCount++;
                logger.LogWarning(
                    exception,
                    "Failed to delete local mod. Path={Path}",
                    mod.FullPath);
            }
        }

        await RefreshModsAsync();
        return failedCount;
    }

    public async Task<bool> ImportModFromPathAsync(string path, bool overwriteExisting = false, bool reportStatus = true)
    {
        if (selectedInstance is null || string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            await modService.ImportAsync(selectedInstance, path, overwriteExisting);
        }
        catch (ModFileImportNotFoundException)
        {
            if (reportStatus)
                ReportStatus(Strings.Status_LocalModImportFileNotFound);
            return false;
        }

        await RefreshModsAsync();
        if (reportStatus)
            ReportStatus(Strings.Status_LocalModImported);
        return true;
    }

    public void Dispose()
    {
        modsWatcher?.Dispose();
        modsWatcher = null;
        CancelRefresh();
        CancelIconEnrichment();
        watcherRefreshCancellationTokenSource?.Cancel();
        watcherRefreshCancellationTokenSource?.Dispose();
        watcherRefreshCancellationTokenSource = null;
    }

    private void ResetWatcher()
    {
        modsWatcher?.Dispose();
        modsWatcher = null;
        watcherRefreshCancellationTokenSource?.Cancel();
        watcherRefreshCancellationTokenSource?.Dispose();
        watcherRefreshCancellationTokenSource = null;

        if (!watcherEnabled || watcherSuspendedForRename)
            return;

        var instance = selectedInstance;
        if (instance is null || string.IsNullOrWhiteSpace(instance.InstanceDirectory))
            return;

        var modsDirectory = Path.Combine(instance.InstanceDirectory, "mods");
        Directory.CreateDirectory(modsDirectory);

        try
        {
            modsWatcher = new FileSystemWatcher(modsDirectory, "*")
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.DirectoryName
                    | NotifyFilters.FileName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.Size
                    | NotifyFilters.CreationTime
            };
            modsWatcher.Changed += ModsWatcher_StateChanged;
            modsWatcher.Created += ModsWatcher_StateChanged;
            modsWatcher.Deleted += ModsWatcher_StateChanged;
            modsWatcher.Renamed += ModsWatcher_StateRenamed;
            modsWatcher.EnableRaisingEvents = true;
            logger.LogInformation(
                "Local mod watcher started. InstanceId={InstanceId} InstanceDirectory={InstanceDirectory}",
                instance.Id,
                instance.InstanceDirectory);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or ArgumentException)
        {
            logger.LogWarning(
                exception,
                "Failed to start local mod watcher. InstanceId={InstanceId} InstanceDirectory={InstanceDirectory}",
                instance.Id,
                instance.InstanceDirectory);
        }
    }

    private void ModsWatcher_StateChanged(object sender, FileSystemEventArgs e)
    {
        if (!IsTrackedModPath(e.FullPath))
            return;

        if (ShouldIgnoreWatcherPath(e.FullPath))
            return;

        QueueWatcherRefresh(e.ChangeType.ToString(), e.FullPath);
    }

    private void ModsWatcher_StateRenamed(object sender, RenamedEventArgs e)
    {
        if (!IsTrackedModPath(e.FullPath) && !IsTrackedModPath(e.OldFullPath))
            return;

        if (ShouldIgnoreWatcherPath(e.FullPath) || ShouldIgnoreWatcherPath(e.OldFullPath))
            return;

        QueueWatcherRefresh("Renamed", e.FullPath);
    }

    private void QueueWatcherRefresh(string changeType, string fullPath)
    {
        var instance = selectedInstance;
        if (instance is null)
            return;

        var previousCts = Interlocked.Exchange(
            ref watcherRefreshCancellationTokenSource,
            new CancellationTokenSource());
        previousCts?.Cancel();
        previousCts?.Dispose();

        var refreshCts = watcherRefreshCancellationTokenSource!;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(200, refreshCts.Token);
                logger.LogInformation(
                    "Detected local mod folder change. InstanceId={InstanceId} ChangeType={ChangeType} Path={Path}",
                    instance.Id,
                    changeType,
                    fullPath);
                await RefreshModsAsync();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Failed to refresh local mods after watcher update. InstanceId={InstanceId}",
                    instance.Id);
                uiDispatcher.Post(() => ReportStatus(Strings.Status_LoadLocalModsFailed));
            }
        });
    }

    private void ReportStatus(string message)
    {
        statusService.Report(message);
    }

    private void ClearMods()
    {
        uiDispatcher.Invoke(() =>
        {
            currentMods = Array.Empty<LocalMod>();
            Mods.Clear();
            ModsChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private void CancelRefresh()
    {
        refreshCancellationTokenSource?.Cancel();
        refreshCancellationTokenSource?.Dispose();
        refreshCancellationTokenSource = null;
    }

    private CancellationTokenSource ReplaceRefreshCancellationTokenSource()
    {
        var next = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref refreshCancellationTokenSource, next);
        previous?.Cancel();
        previous?.Dispose();
        CancelIconEnrichment();
        return next;
    }

    private void ReleaseRefreshCancellationTokenSource(CancellationTokenSource refreshCts)
    {
        var current = Interlocked.CompareExchange(ref refreshCancellationTokenSource, null, refreshCts);
        if (ReferenceEquals(current, refreshCts))
            refreshCts.Dispose();
    }

    private void QueueRemoteIconEnrichment(GameInstance instance, IReadOnlyList<LocalMod> loadedMods, int refreshVersion)
    {
        if (iconEnrichmentService is null)
            return;

        var missingIconMods = loadedMods
            .Where(mod => string.IsNullOrWhiteSpace(mod.IconSource))
            .ToArray();
        if (missingIconMods.Length == 0)
            return;

        var enrichmentCts = ReplaceIconEnrichmentCancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                var progress = new Progress<IReadOnlyDictionary<string, string>>(resolvedIcons =>
                    ApplyResolvedIcons(instance, resolvedIcons, enrichmentCts, refreshVersion));
                var resolvedIcons = await iconEnrichmentService
                    .ResolveMissingIconSourcesAsync(missingIconMods, enrichmentCts.Token, progress)
                    .ConfigureAwait(false);
                ApplyResolvedIcons(instance, resolvedIcons, enrichmentCts, refreshVersion);
            }
            catch (OperationCanceledException) when (enrichmentCts.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Failed to enrich local mod icons. InstanceId={InstanceId}",
                    instance.Id);
            }
            finally
            {
                ReleaseIconEnrichmentCancellationTokenSource(enrichmentCts);
            }
        });
    }

    private void ApplyResolvedIcons(
        GameInstance instance,
        IReadOnlyDictionary<string, string> resolvedIcons,
        CancellationTokenSource enrichmentCts,
        int refreshVersion)
    {
        if (resolvedIcons.Count == 0
            || enrichmentCts.IsCancellationRequested
            || refreshVersion != modRefreshVersion
            || !string.Equals(instance.Id, selectedInstance?.Id, StringComparison.Ordinal))
        {
            return;
        }

        uiDispatcher.Post(() =>
        {
            if (enrichmentCts.IsCancellationRequested
                || refreshVersion != modRefreshVersion
                || !string.Equals(instance.Id, selectedInstance?.Id, StringComparison.Ordinal))
            {
                return;
            }

            var updated = false;
            foreach (var mod in currentMods)
            {
                if (!string.IsNullOrWhiteSpace(mod.IconSource)
                    || !resolvedIcons.TryGetValue(mod.FullPath, out var iconSource)
                    || string.IsNullOrWhiteSpace(iconSource))
                {
                    continue;
                }

                mod.IconSource = iconSource;
                updated = true;
            }

            if (updated)
                ModsChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private void CancelIconEnrichment()
    {
        iconEnrichmentCancellationTokenSource?.Cancel();
        iconEnrichmentCancellationTokenSource?.Dispose();
        iconEnrichmentCancellationTokenSource = null;
    }

    private CancellationTokenSource ReplaceIconEnrichmentCancellationTokenSource()
    {
        var next = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref iconEnrichmentCancellationTokenSource, next);
        previous?.Cancel();
        previous?.Dispose();
        return next;
    }

    private void ReleaseIconEnrichmentCancellationTokenSource(CancellationTokenSource enrichmentCts)
    {
        var current = Interlocked.CompareExchange(ref iconEnrichmentCancellationTokenSource, null, enrichmentCts);
        if (ReferenceEquals(current, enrichmentCts))
            enrichmentCts.Dispose();
    }

    private static bool IsTrackedModPath(string? fullPath)
    {
        return !string.IsNullOrWhiteSpace(fullPath)
            && (fullPath.EndsWith(EnabledModExtension, StringComparison.OrdinalIgnoreCase)
                || fullPath.EndsWith(DisabledModExtension, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyEnabledStateLocally(LocalMod mod, string targetPath, bool enabled, bool raiseChanged)
    {
        uiDispatcher.Invoke(() =>
        {
            ApplyEnabledStateLocallyCore(mod, targetPath, enabled);
            if (raiseChanged)
                ModsChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private static void ApplyEnabledStateLocallyCore(LocalMod mod, string targetPath, bool enabled)
    {
        mod.FullPath = targetPath;
        mod.FileName = Path.GetFileName(targetPath);
        mod.IsEnabled = enabled;
    }

    private void IgnoreWatcherPaths(params string?[] paths)
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(IgnoredWatcherPathTtl);
        lock (ignoredWatcherPathsLock)
        {
            PruneIgnoredWatcherPaths(DateTimeOffset.UtcNow);
            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                ignoredWatcherPaths[path] = expiresAt;
            }
        }
    }

    private bool ShouldIgnoreWatcherPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var now = DateTimeOffset.UtcNow;
        lock (ignoredWatcherPathsLock)
        {
            PruneIgnoredWatcherPaths(now);
            return ignoredWatcherPaths.Remove(path);
        }
    }

    private void RemoveIgnoredWatcherPaths(params string?[] paths)
    {
        lock (ignoredWatcherPathsLock)
        {
            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                ignoredWatcherPaths.Remove(path);
            }
        }
    }

    private void PruneIgnoredWatcherPaths(DateTimeOffset now)
    {
        foreach (var path in ignoredWatcherPaths
                     .Where(pair => pair.Value <= now)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            ignoredWatcherPaths.Remove(path);
        }
    }

    private static string GetPathForEnabledState(string path, bool enabled)
    {
        return enabled
            ? path.EndsWith(DisabledModExtension, StringComparison.OrdinalIgnoreCase)
                ? path[..^".disabled".Length]
                : path
            : path.EndsWith(DisabledModExtension, StringComparison.OrdinalIgnoreCase)
                ? path
                : path + ".disabled";
    }
}
