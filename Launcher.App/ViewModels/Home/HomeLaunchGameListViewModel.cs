using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Home;

public sealed partial class HomeLaunchGameListViewModel : ObservableObject
{
    private readonly IGameVersionService gameVersionService;
    private readonly IStatusService statusService;
    private readonly Func<GameInstance, Task<bool>> selectLaunchInstance;
    private IReadOnlyDictionary<string, string> versionTypesByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private bool hasLoadedVersionTypes;

    [ObservableProperty]
    private GameInstance? selectedInstance;

    public HomeLaunchGameListViewModel(
        IGameVersionService gameVersionService,
        IStatusService statusService,
        Func<GameInstance, Task<bool>> selectLaunchInstance)
    {
        this.gameVersionService = gameVersionService;
        this.statusService = statusService;
        this.selectLaunchInstance = selectLaunchInstance;
    }

    public ObservableCollection<HomeLaunchInstanceItem> LaunchInstances { get; } = [];

    public bool HasLaunchInstances => LaunchInstances.Count > 0;

    public bool HasNoLaunchInstances => !HasLaunchInstances;

    public HomeLaunchInstanceItem? SelectedLaunchInstanceItem => LaunchInstances.FirstOrDefault(item => item.IsSelected);

    public bool HasSelectedLaunchInstance => SelectedLaunchInstanceItem is not null;

    public void SetSelectedInstance(GameInstance? instance)
    {
        SelectedInstance = instance;
    }

    public void SetLaunchInstances(IEnumerable<GameInstance> instances)
    {
        var selectedInstanceId = SelectedInstance?.Id;
        LaunchInstances.ReplaceWith(
            instances
                .OrderByDescending(instance => instance.CreatedAt)
                .Select(CreateLaunchInstanceItem));
        UpdateLaunchInstanceSelection(selectedInstanceId);
        NotifyLaunchInstancesChanged();
    }

    public async Task EnsureVersionTypesLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (hasLoadedVersionTypes)
            return;

        try
        {
            var versions = await gameVersionService.GetVersionsAsync(cancellationToken);
            versionTypesByName = versions
                .GroupBy(version => version.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => MinecraftVersionIconResolver.NormalizeVersionType(group.First().Type),
                    StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            versionTypesByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        hasLoadedVersionTypes = true;

        if (LaunchInstances.Count > 0)
            SetLaunchInstances(LaunchInstances.Select(item => item.Instance).ToArray());
    }

    [RelayCommand]
    private async Task SelectLaunchInstanceAsync(HomeLaunchInstanceItem? item)
    {
        if (item is null)
            return;

        try
        {
            var saved = await selectLaunchInstance(item.Instance);
            if (!saved)
            {
                statusService.Report(Strings.Status_LaunchInstanceSelectionFailed);
                return;
            }

            SetSelectedInstance(item.Instance);
            statusService.Report(string.Format(Strings.Status_LaunchInstanceSelectedFormat, item.Name));
        }
        catch (Exception)
        {
            statusService.Report(Strings.Status_LaunchInstanceSelectionFailed);
        }
    }

    partial void OnSelectedInstanceChanged(GameInstance? value)
    {
        UpdateLaunchInstanceSelection(value?.Id);
    }

    private void NotifyLaunchInstancesChanged()
    {
        OnPropertyChanged(nameof(HasLaunchInstances));
        OnPropertyChanged(nameof(HasNoLaunchInstances));
        OnPropertyChanged(nameof(SelectedLaunchInstanceItem));
        OnPropertyChanged(nameof(HasSelectedLaunchInstance));
    }

    private void UpdateLaunchInstanceSelection(string? selectedInstanceId)
    {
        foreach (var item in LaunchInstances)
        {
            item.IsSelected = !string.IsNullOrWhiteSpace(selectedInstanceId)
                && string.Equals(item.Instance.Id, selectedInstanceId, StringComparison.OrdinalIgnoreCase);
        }

        OnPropertyChanged(nameof(SelectedLaunchInstanceItem));
        OnPropertyChanged(nameof(HasSelectedLaunchInstance));
    }

    private HomeLaunchInstanceItem CreateLaunchInstanceItem(GameInstance instance)
    {
        return new HomeLaunchInstanceItem(instance, ResolveVersionType(instance));
    }

    private string ResolveVersionType(GameInstance instance)
    {
        if (!string.IsNullOrWhiteSpace(instance.VersionType))
            return instance.VersionType;

        var versionName = string.IsNullOrWhiteSpace(instance.MinecraftVersion)
            ? instance.VersionName
            : instance.MinecraftVersion;

        return !string.IsNullOrWhiteSpace(versionName)
               && versionTypesByName.TryGetValue(versionName, out var versionType)
            ? versionType
            : string.Empty;
    }
}

