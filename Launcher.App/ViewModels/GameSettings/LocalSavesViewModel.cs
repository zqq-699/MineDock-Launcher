using System.Collections.ObjectModel;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.IO;

namespace Launcher.App.ViewModels.GameSettings;

public sealed class LocalSavesViewModel : IDisposable
{
    private readonly ILocalSaveService localSaveService;
    private readonly IStatusService statusService;
    private readonly IUiDispatcher uiDispatcher;
    private readonly ILogger<LocalSavesViewModel> logger;
    private FileSystemWatcher? savesWatcher;
    private CancellationTokenSource? watcherRefreshCancellationTokenSource;
    private GameInstance? selectedInstance;
    private string? watchedSavesDirectory;
    private int saveRefreshVersion;

    public LocalSavesViewModel(
        ILocalSaveService localSaveService,
        IStatusService statusService,
        IUiDispatcher? uiDispatcher = null,
        ILogger<LocalSavesViewModel>? logger = null)
    {
        this.localSaveService = localSaveService;
        this.statusService = statusService;
        this.uiDispatcher = uiDispatcher ?? ImmediateUiDispatcher.Instance;
        this.logger = logger ?? NullLogger<LocalSavesViewModel>.Instance;
    }

    public event EventHandler? SavesChanged;

    public ObservableCollection<LocalSave> Saves { get; } = [];

    public Task SetSelectedInstanceAsync(GameInstance? instance)
    {
        selectedInstance = instance;
        ResetWatcher(instance);
        logger.LogInformation(
            "Selected instance changed for local saves view. InstanceId={InstanceId}",
            instance?.Id ?? "<none>");
        return RefreshSavesAsync();
    }

    public async Task RefreshSavesAsync()
    {
        var refreshVersion = Interlocked.Increment(ref saveRefreshVersion);
        var instance = selectedInstance;

        if (instance is null)
        {
            uiDispatcher.Invoke(() =>
            {
                Saves.Clear();
                SavesChanged?.Invoke(this, EventArgs.Empty);
            });
            logger.LogInformation("Local saves view cleared because no instance is selected.");
            return;
        }

        IReadOnlyList<LocalSave> loadedSaves;
        try
        {
            loadedSaves = await localSaveService.GetSavesAsync(instance);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to load local saves. InstanceId={InstanceId}",
                instance.Id);
            throw;
        }

        if (refreshVersion != saveRefreshVersion
            || !string.Equals(instance.Id, selectedInstance?.Id, StringComparison.Ordinal))
        {
            return;
        }

        uiDispatcher.Invoke(() =>
        {
            Saves.ReplaceWith(loadedSaves);
            SavesChanged?.Invoke(this, EventArgs.Empty);
        });
        logger.LogInformation(
            "Local saves view refreshed. InstanceId={InstanceId} Count={SaveCount}",
            instance.Id,
            Saves.Count);
    }

    public async Task DeleteSaveAsync(LocalSave save)
    {
        await localSaveService.DeleteAsync(save);
        await RefreshSavesAsync();
    }

    public async Task<int> DeleteSavesAsync(IEnumerable<LocalSave> saves)
    {
        ArgumentNullException.ThrowIfNull(saves);

        var failedCount = 0;
        foreach (var save in saves.DistinctBy(save => save.FullPath, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                await localSaveService.DeleteAsync(save);
            }
            catch (Exception exception)
            {
                failedCount++;
                logger.LogWarning(
                    exception,
                    "Failed to delete local save. Path={Path}",
                    save.FullPath);
            }
        }

        await RefreshSavesAsync();
        return failedCount;
    }

    public void Dispose()
    {
        savesWatcher?.Dispose();
        savesWatcher = null;
        watcherRefreshCancellationTokenSource?.Cancel();
        watcherRefreshCancellationTokenSource?.Dispose();
        watcherRefreshCancellationTokenSource = null;
    }

    private void ResetWatcher(GameInstance? instance)
    {
        savesWatcher?.Dispose();
        savesWatcher = null;
        watcherRefreshCancellationTokenSource?.Cancel();
        watcherRefreshCancellationTokenSource?.Dispose();
        watcherRefreshCancellationTokenSource = null;
        watchedSavesDirectory = null;

        if (instance is null || string.IsNullOrWhiteSpace(instance.InstanceDirectory))
            return;

        watchedSavesDirectory = Path.Combine(instance.InstanceDirectory, "saves");
        Directory.CreateDirectory(watchedSavesDirectory);

        try
        {
            savesWatcher = new FileSystemWatcher(watchedSavesDirectory, "*")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.DirectoryName
                    | NotifyFilters.FileName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.Size
                    | NotifyFilters.CreationTime
            };
            savesWatcher.Changed += SavesWatcher_StateChanged;
            savesWatcher.Created += SavesWatcher_StateChanged;
            savesWatcher.Deleted += SavesWatcher_StateChanged;
            savesWatcher.Renamed += SavesWatcher_StateRenamed;
            savesWatcher.EnableRaisingEvents = true;
            logger.LogInformation(
                "Local save watcher started. InstanceId={InstanceId} SavesDirectory={SavesDirectory}",
                instance.Id,
                watchedSavesDirectory);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or ArgumentException)
        {
            logger.LogWarning(
                exception,
                "Failed to start local save watcher. InstanceId={InstanceId} SavesDirectory={SavesDirectory}",
                instance.Id,
                watchedSavesDirectory);
        }
    }

    private void SavesWatcher_StateChanged(object sender, FileSystemEventArgs e)
    {
        if (!IsTrackedSavePath(e.FullPath))
            return;

        QueueWatcherRefresh(e.ChangeType.ToString(), e.FullPath);
    }

    private void SavesWatcher_StateRenamed(object sender, RenamedEventArgs e)
    {
        if (!IsTrackedSavePath(e.FullPath) && !IsTrackedSavePath(e.OldFullPath))
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
                    "Detected local save folder change. InstanceId={InstanceId} ChangeType={ChangeType} Path={Path}",
                    instance.Id,
                    changeType,
                    fullPath);
                await RefreshSavesAsync();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Failed to refresh local saves after watcher update. InstanceId={InstanceId}",
                    instance.Id);
                uiDispatcher.Post(() => ReportStatus(Strings.Status_LoadLocalSavesFailed));
            }
        });
    }

    private bool IsTrackedSavePath(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || string.IsNullOrWhiteSpace(watchedSavesDirectory))
            return false;

        var normalizedRoot = Path.GetFullPath(watchedSavesDirectory);
        var normalizedPath = Path.GetFullPath(fullPath);
        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private void ReportStatus(string message)
    {
        statusService.Report(message);
    }
}
