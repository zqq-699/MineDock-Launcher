using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.GameSettings;

public sealed partial class InstanceManagementViewModel : ObservableObject
{
    private readonly ISettingsService settingsService;
    private readonly IGameInstanceService instanceService;
    private readonly IStatusService statusService;
    private readonly ILogger<InstanceManagementViewModel> logger;
    private readonly object refreshInstancesSync = new();
    private LauncherSettings settings = new();
    private Task? refreshInstancesTask;
    private bool hasLoadedInstances;

    [ObservableProperty]
    private GameInstance? selectedInstance;

    [ObservableProperty]
    private string newInstanceName = string.Empty;

    public InstanceManagementViewModel(
        ISettingsService settingsService,
        IGameInstanceService instanceService,
        IStatusService statusService,
        ILogger<InstanceManagementViewModel>? logger = null)
    {
        this.settingsService = settingsService;
        this.instanceService = instanceService;
        this.statusService = statusService;
        this.logger = logger ?? NullLogger<InstanceManagementViewModel>.Instance;
    }

    public ObservableCollection<GameInstance> Instances { get; } = [];

    public bool HasLoadedInstances => hasLoadedInstances;

    public async Task InitializeAsync(LauncherSettings launcherSettings)
    {
        settings = launcherSettings;
        await EnsureInstancesLoadedAsync();
    }

    public async Task EnsureInstancesLoadedAsync()
    {
        if (hasLoadedInstances)
            return;

        await RefreshInstancesAsync();
    }

    public async Task RefreshInstancesAsync()
    {
        Task refreshTask;
        lock (refreshInstancesSync)
        {
            refreshTask = refreshInstancesTask ??= RefreshInstancesCoreAsync();
        }

        try
        {
            await refreshTask;
        }
        finally
        {
            lock (refreshInstancesSync)
            {
                if (ReferenceEquals(refreshInstancesTask, refreshTask))
                    refreshInstancesTask = null;
            }
        }
    }

    public async Task<GameInstance?> CreateInstanceAsync(
        MinecraftVersionInfo? minecraftVersion,
        LoaderKind loader,
        LoaderVersionInfo? loaderVersion,
        IProgress<LauncherProgress>? progress)
    {
        if (minecraftVersion is null)
        {
            ReportStatus(Strings.Status_SelectMinecraftVersionFirst);
            return null;
        }

        var resolvedLoaderVersion = loader is LoaderKind.Vanilla ? null : loaderVersion?.Version;
        GameInstance instance;
        try
        {
            instance = await instanceService.CreateInstanceAsync(
                minecraftVersion.Name,
                loader,
                resolvedLoaderVersion,
                NewInstanceName,
                progress,
                downloadSourcePreference: settings.DownloadSourcePreference,
                downloadSpeedLimitMbPerSecond: settings.DownloadSpeedLimitMbPerSecond);
        }
        catch (DuplicateGameInstanceNameException)
        {
            ReportStatus(Strings.Status_DuplicateInstanceName);
            return null;
        }

        Instances.Add(instance);
        SelectedInstance = instance;
        ReportStatus(string.Format(Strings.Status_InstanceCreatedFormat, instance.Name));
        return instance;
    }

    public async Task SaveSettingsAsync()
    {
        await settingsService.SaveAsync(settings);
        ReportStatus(Strings.Status_SettingsSaved);
    }

    public async Task SaveInstanceAsync()
    {
        if (SelectedInstance is null)
            return;

        await instanceService.SaveInstanceAsync(SelectedInstance);
        ReportStatus(Strings.Status_InstanceSettingsSaved);
    }

    public async Task SetDefaultInstanceAsync()
    {
        if (SelectedInstance is null)
            return;

        var saved = await SelectLaunchInstanceAsync(SelectedInstance);
        ReportStatus(saved
            ? string.Format(Strings.Status_DefaultInstanceSetFormat, SelectedInstance.Name)
            : Strings.Status_LaunchInstanceSelectionFailed);
    }

    public async Task<bool> SelectLaunchInstanceAsync(GameInstance instance)
    {
        var selected = Instances.FirstOrDefault(existing =>
            string.Equals(existing.Id, instance.Id, StringComparison.OrdinalIgnoreCase));

        if (selected is null)
            return false;

        var saved = await instanceService.SetDefaultInstanceAsync(selected.Id);
        if (!saved)
            return false;

        settings.DefaultInstanceId = selected.Id;
        SelectedInstance = selected;
        return true;
    }

    public void ApplyUpdatedInstance(GameInstance instance)
    {
        var index = FindInstanceIndex(instance.Id);
        var wasSelected = string.Equals(SelectedInstance?.Id, instance.Id, StringComparison.OrdinalIgnoreCase);

        if (index >= 0)
            Instances[index] = instance;
        else
            Instances.Add(instance);

        if (wasSelected || SelectedInstance is null)
            SelectedInstance = instance;

        hasLoadedInstances = true;
        logger.LogInformation(
            "Game management instance updated locally. InstanceId={InstanceId} Count={InstanceCount} SelectedInstanceId={SelectedInstanceId}",
            instance.Id,
            Instances.Count,
            SelectedInstance?.Id);
    }

    private async Task RefreshInstancesCoreAsync()
    {
        var loadedInstances = await instanceService.GetInstancesAsync();
        var previousSelectedId = SelectedInstance?.Id;

        Instances.ReplaceWith(loadedInstances);

        var selected = !string.IsNullOrWhiteSpace(settings.DefaultInstanceId)
            ? Instances.FirstOrDefault(instance => instance.Id == settings.DefaultInstanceId)
            : null;
        selected ??= !string.IsNullOrWhiteSpace(previousSelectedId)
            ? Instances.FirstOrDefault(instance => instance.Id == previousSelectedId)
            : null;
        selected ??= Instances.FirstOrDefault();

        SelectedInstance = selected;
        hasLoadedInstances = true;
        logger.LogInformation(
            "Game management instances refreshed. Count={InstanceCount} SelectedInstanceId={SelectedInstanceId}",
            Instances.Count,
            SelectedInstance?.Id);
    }

    private int FindInstanceIndex(string instanceId)
    {
        for (var index = 0; index < Instances.Count; index++)
        {
            if (string.Equals(Instances[index].Id, instanceId, StringComparison.OrdinalIgnoreCase))
                return index;
        }

        return -1;
    }

    private void ReportStatus(string message)
    {
        statusService.Report(message);
    }
}

