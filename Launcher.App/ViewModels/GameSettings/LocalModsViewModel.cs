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
    private readonly IModService modService;
    private readonly IStatusService statusService;
    private readonly IUiDispatcher uiDispatcher;
    private readonly ILogger<LocalModsViewModel> logger;
    private FileSystemWatcher? modsWatcher;
    private CancellationTokenSource? watcherRefreshCancellationTokenSource;
    private GameInstance? selectedInstance;
    private int modRefreshVersion;

    public LocalModsViewModel(
        IModService modService,
        IStatusService statusService,
        IUiDispatcher? uiDispatcher = null,
        ILogger<LocalModsViewModel>? logger = null)
    {
        this.modService = modService;
        this.statusService = statusService;
        this.uiDispatcher = uiDispatcher ?? ImmediateUiDispatcher.Instance;
        this.logger = logger ?? NullLogger<LocalModsViewModel>.Instance;
    }

    public event EventHandler? ModsChanged;

    public ObservableCollection<LocalMod> Mods { get; } = [];

    public Task SetSelectedInstanceAsync(GameInstance? instance)
    {
        selectedInstance = instance;
        ResetWatcher(instance);
        logger.LogInformation(
            "Selected instance changed for local mods view. InstanceId={InstanceId}",
            instance?.Id ?? "<none>");
        return RefreshModsAsync();
    }

    public async Task RefreshModsAsync()
    {
        var refreshVersion = Interlocked.Increment(ref modRefreshVersion);
        var instance = selectedInstance;

        if (instance is null)
        {
            uiDispatcher.Invoke(() =>
            {
                Mods.Clear();
                ModsChanged?.Invoke(this, EventArgs.Empty);
            });
            logger.LogInformation("Local mods view cleared because no instance is selected.");
            return;
        }

        IReadOnlyList<LocalMod> loadedMods;
        try
        {
            loadedMods = await modService.GetModsAsync(instance);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to load local mods. InstanceId={InstanceId}",
                instance.Id);
            throw;
        }

        if (refreshVersion != modRefreshVersion
            || !string.Equals(instance.Id, selectedInstance?.Id, StringComparison.Ordinal))
        {
            return;
        }

        uiDispatcher.Invoke(() =>
        {
            Mods.ReplaceWith(loadedMods);
            ModsChanged?.Invoke(this, EventArgs.Empty);
        });
        logger.LogInformation(
            "Local mods view refreshed. InstanceId={InstanceId} Count={ModCount}",
            instance.Id,
            Mods.Count);
    }

    public async Task ToggleModAsync(LocalMod mod)
    {
        await modService.SetEnabledAsync(mod, !mod.IsEnabled);
        await RefreshModsAsync();
    }

    public async Task DeleteModAsync(LocalMod mod)
    {
        await modService.DeleteAsync(mod);
        await RefreshModsAsync();
    }

    public async Task ImportModFromPathAsync(string path)
    {
        if (selectedInstance is null || string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            await modService.ImportAsync(selectedInstance, path);
        }
        catch (ModFileImportNotFoundException)
        {
            ReportStatus(Strings.Status_LocalModImportFileNotFound);
            return;
        }

        await RefreshModsAsync();
        ReportStatus(Strings.Status_LocalModImported);
    }

    public void Dispose()
    {
        modsWatcher?.Dispose();
        modsWatcher = null;
        watcherRefreshCancellationTokenSource?.Cancel();
        watcherRefreshCancellationTokenSource?.Dispose();
        watcherRefreshCancellationTokenSource = null;
    }

    private void ResetWatcher(GameInstance? instance)
    {
        modsWatcher?.Dispose();
        modsWatcher = null;
        watcherRefreshCancellationTokenSource?.Cancel();
        watcherRefreshCancellationTokenSource?.Dispose();
        watcherRefreshCancellationTokenSource = null;

        if (instance is null || string.IsNullOrWhiteSpace(instance.InstanceDirectory))
            return;

        if (!Directory.Exists(instance.InstanceDirectory))
            return;

        try
        {
            modsWatcher = new FileSystemWatcher(instance.InstanceDirectory, "*.jar")
            {
                IncludeSubdirectories = true,
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
        QueueWatcherRefresh(e.ChangeType.ToString(), e.FullPath);
    }

    private void ModsWatcher_StateRenamed(object sender, RenamedEventArgs e)
    {
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
}
