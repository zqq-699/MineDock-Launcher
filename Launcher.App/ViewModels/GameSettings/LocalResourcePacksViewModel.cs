using System.Collections.ObjectModel;
using System.IO;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.GameSettings;

public sealed class LocalResourcePacksViewModel : IDisposable
{
    private readonly ILocalResourcePackService localResourcePackService;
    private readonly IStatusService statusService;
    private readonly IUiDispatcher uiDispatcher;
    private readonly ILogger<LocalResourcePacksViewModel> logger;
    private FileSystemWatcher? resourcePacksWatcher;
    private CancellationTokenSource? watcherRefreshCancellationTokenSource;
    private GameInstance? selectedInstance;
    private string? watchedResourcePacksDirectory;
    private int resourcePackRefreshVersion;

    public LocalResourcePacksViewModel(
        ILocalResourcePackService localResourcePackService,
        IStatusService statusService,
        IUiDispatcher? uiDispatcher = null,
        ILogger<LocalResourcePacksViewModel>? logger = null)
    {
        this.localResourcePackService = localResourcePackService;
        this.statusService = statusService;
        this.uiDispatcher = uiDispatcher ?? ImmediateUiDispatcher.Instance;
        this.logger = logger ?? NullLogger<LocalResourcePacksViewModel>.Instance;
    }

    public event EventHandler? ResourcePacksChanged;

    public ObservableCollection<LocalResourcePack> ResourcePacks { get; } = [];

    public Task SetSelectedInstanceAsync(GameInstance? instance)
    {
        selectedInstance = instance;
        ResetWatcher(instance);
        logger.LogInformation(
            "Selected instance changed for local resource packs view. InstanceId={InstanceId}",
            instance?.Id ?? "<none>");
        return RefreshResourcePacksAsync();
    }

    public async Task RefreshResourcePacksAsync()
    {
        var refreshVersion = Interlocked.Increment(ref resourcePackRefreshVersion);
        var instance = selectedInstance;

        if (instance is null)
        {
            uiDispatcher.Invoke(() =>
            {
                ResourcePacks.Clear();
                ResourcePacksChanged?.Invoke(this, EventArgs.Empty);
            });
            logger.LogInformation("Local resource packs view cleared because no instance is selected.");
            return;
        }

        IReadOnlyList<LocalResourcePack> loadedResourcePacks;
        try
        {
            loadedResourcePacks = await localResourcePackService.GetResourcePacksAsync(instance);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to load local resource packs. InstanceId={InstanceId}",
                instance.Id);
            throw;
        }

        if (refreshVersion != resourcePackRefreshVersion
            || !string.Equals(instance.Id, selectedInstance?.Id, StringComparison.Ordinal))
        {
            return;
        }

        uiDispatcher.Invoke(() =>
        {
            ResourcePacks.ReplaceWith(loadedResourcePacks);
            ResourcePacksChanged?.Invoke(this, EventArgs.Empty);
        });
        logger.LogInformation(
            "Local resource packs view refreshed. InstanceId={InstanceId} Count={ResourcePackCount}",
            instance.Id,
            ResourcePacks.Count);
    }

    public async Task<int> DeleteResourcePacksAsync(IEnumerable<LocalResourcePack> resourcePacks)
    {
        ArgumentNullException.ThrowIfNull(resourcePacks);

        var failedCount = 0;
        foreach (var resourcePack in resourcePacks.DistinctBy(resourcePack => resourcePack.FullPath, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                await localResourcePackService.DeleteAsync(resourcePack);
            }
            catch (Exception exception)
            {
                failedCount++;
                logger.LogWarning(
                    exception,
                    "Failed to delete local resource pack. Path={Path}",
                    resourcePack.FullPath);
            }
        }

        await RefreshResourcePacksAsync();
        return failedCount;
    }

    public async Task<LocalResourcePackImportResult> ImportResourcePackAsync(string archivePath, bool reportStatus = true)
    {
        if (selectedInstance is null || string.IsNullOrWhiteSpace(archivePath))
            return LocalResourcePackImportResult.Failure(LocalResourcePackImportFailureReason.UnexpectedError);

        var result = await localResourcePackService.ImportAsync(selectedInstance, archivePath);
        if (!result.IsSuccess)
        {
            switch (result.FailureReason)
            {
                case LocalResourcePackImportFailureReason.FileNotFound:
                    if (reportStatus)
                        ReportStatus(Strings.Status_LocalResourcePackImportFileNotFound);
                    break;
                case LocalResourcePackImportFailureReason.UnexpectedError:
                    if (reportStatus)
                        ReportStatus(Strings.Status_LocalResourcePackImportFailed);
                    break;
            }

            return result;
        }

        await RefreshResourcePacksAsync();
        if (reportStatus)
            ReportStatus(Strings.Status_LocalResourcePackImported);
        return result;
    }

    public void Dispose()
    {
        resourcePacksWatcher?.Dispose();
        resourcePacksWatcher = null;
        watcherRefreshCancellationTokenSource?.Cancel();
        watcherRefreshCancellationTokenSource?.Dispose();
        watcherRefreshCancellationTokenSource = null;
    }

    private void ResetWatcher(GameInstance? instance)
    {
        resourcePacksWatcher?.Dispose();
        resourcePacksWatcher = null;
        watcherRefreshCancellationTokenSource?.Cancel();
        watcherRefreshCancellationTokenSource?.Dispose();
        watcherRefreshCancellationTokenSource = null;
        watchedResourcePacksDirectory = null;

        if (instance is null || string.IsNullOrWhiteSpace(instance.InstanceDirectory))
            return;

        watchedResourcePacksDirectory = Path.Combine(instance.InstanceDirectory, "resourcepacks");
        Directory.CreateDirectory(watchedResourcePacksDirectory);

        try
        {
            resourcePacksWatcher = new FileSystemWatcher(watchedResourcePacksDirectory, "*")
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.DirectoryName
                    | NotifyFilters.FileName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.Size
                    | NotifyFilters.CreationTime
            };
            resourcePacksWatcher.Changed += ResourcePacksWatcher_StateChanged;
            resourcePacksWatcher.Created += ResourcePacksWatcher_StateChanged;
            resourcePacksWatcher.Deleted += ResourcePacksWatcher_StateChanged;
            resourcePacksWatcher.Renamed += ResourcePacksWatcher_StateRenamed;
            resourcePacksWatcher.EnableRaisingEvents = true;
            logger.LogInformation(
                "Local resource pack watcher started. InstanceId={InstanceId} ResourcePacksDirectory={ResourcePacksDirectory}",
                instance.Id,
                watchedResourcePacksDirectory);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or ArgumentException)
        {
            logger.LogWarning(
                exception,
                "Failed to start local resource pack watcher. InstanceId={InstanceId} ResourcePacksDirectory={ResourcePacksDirectory}",
                instance.Id,
                watchedResourcePacksDirectory);
        }
    }

    private void ResourcePacksWatcher_StateChanged(object sender, FileSystemEventArgs e)
    {
        if (!IsTrackedResourcePackPath(e.FullPath))
            return;

        QueueWatcherRefresh(e.ChangeType.ToString(), e.FullPath);
    }

    private void ResourcePacksWatcher_StateRenamed(object sender, RenamedEventArgs e)
    {
        if (!IsTrackedResourcePackPath(e.FullPath) && !IsTrackedResourcePackPath(e.OldFullPath))
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
                    "Detected local resource pack folder change. InstanceId={InstanceId} ChangeType={ChangeType} Path={Path}",
                    instance.Id,
                    changeType,
                    fullPath);
                await RefreshResourcePacksAsync();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Failed to refresh local resource packs after watcher update. InstanceId={InstanceId}",
                    instance.Id);
                uiDispatcher.Post(() => ReportStatus(Strings.Status_LoadLocalResourcePacksFailed));
            }
        });
    }

    private bool IsTrackedResourcePackPath(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || string.IsNullOrWhiteSpace(watchedResourcePacksDirectory))
            return false;

        var normalizedRoot = Path.GetFullPath(watchedResourcePacksDirectory);
        var normalizedPath = Path.GetFullPath(fullPath);
        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private void ReportStatus(string message)
    {
        statusService.Report(message);
    }
}
