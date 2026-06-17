using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Models;
using Launcher.App.Services;
using Launcher.App.Utilities;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.GameSettings;

public sealed partial class GameManagementViewModel : ObservableObject
{
    private readonly IStatusService statusService;
    private bool suppressSelectedInstanceModRefresh;

    [ObservableProperty]
    private double progressPercent;

    public GameManagementViewModel(
        InstanceManagementViewModel instances,
        LoaderSelectionViewModel loaderSelection,
        LocalModsViewModel localMods,
        ModrinthSearchViewModel modrinthSearch,
        IStatusService statusService)
    {
        InstancesViewModel = instances;
        LoaderSelection = loaderSelection;
        LocalMods = localMods;
        ModrinthSearch = modrinthSearch;
        this.statusService = statusService;

        InstancesViewModel.PropertyChanged += InstancesViewModel_PropertyChanged;
        LoaderSelection.PropertyChanged += ForwardChildPropertyChanged;
        ModrinthSearch.PropertyChanged += ForwardChildPropertyChanged;
    }

    public InstanceManagementViewModel InstancesViewModel { get; }

    public LoaderSelectionViewModel LoaderSelection { get; }

    public LocalModsViewModel LocalMods { get; }

    public ModrinthSearchViewModel ModrinthSearch { get; }

    public ObservableCollection<GameInstance> Instances => InstancesViewModel.Instances;
    public ObservableCollection<MinecraftVersionInfo> MinecraftVersions => LoaderSelection.MinecraftVersions;
    public ObservableCollection<NavigationItem> LoaderItems => LoaderSelection.LoaderItems;
    public ObservableCollection<LoaderVersionInfo> LoaderVersions => LoaderSelection.LoaderVersions;
    public ObservableCollection<LocalMod> Mods => LocalMods.Mods;
    public ObservableCollection<ModrinthProject> ModrinthProjects => ModrinthSearch.ModrinthProjects;

    public GameInstance? SelectedInstance
    {
        get => InstancesViewModel.SelectedInstance;
        set => InstancesViewModel.SelectedInstance = value;
    }

    public MinecraftVersionInfo? SelectedMinecraftVersion
    {
        get => LoaderSelection.SelectedMinecraftVersion;
        set => LoaderSelection.SelectedMinecraftVersion = value;
    }

    public LoaderKind SelectedLoader
    {
        get => LoaderSelection.SelectedLoader;
        set => LoaderSelection.SelectedLoader = value;
    }

    public LoaderVersionInfo? SelectedLoaderVersion
    {
        get => LoaderSelection.SelectedLoaderVersion;
        set => LoaderSelection.SelectedLoaderVersion = value;
    }

    public string NewInstanceName
    {
        get => InstancesViewModel.NewInstanceName;
        set => InstancesViewModel.NewInstanceName = value;
    }

    public string ModSearchQuery
    {
        get => ModrinthSearch.ModSearchQuery;
        set => ModrinthSearch.ModSearchQuery = value;
    }

    public ModrinthProject? SelectedModrinthProject
    {
        get => ModrinthSearch.SelectedModrinthProject;
        set => ModrinthSearch.SelectedModrinthProject = value;
    }

    public async Task InitializeAsync(LauncherSettings launcherSettings)
    {
        await RunInstanceRefreshWithModSyncAsync(() => InstancesViewModel.InitializeAsync(launcherSettings));
    }

    public async Task EnsureInstancesLoadedAsync()
    {
        if (InstancesViewModel.HasLoadedInstances)
            return;

        await RunInstanceRefreshWithModSyncAsync(InstancesViewModel.EnsureInstancesLoadedAsync);
    }

    public void SelectLoader(LoaderKind loader)
    {
        LoaderSelection.SelectLoader(loader);
    }

    public Task<bool> SelectLaunchInstanceAsync(GameInstance instance)
    {
        return InstancesViewModel.SelectLaunchInstanceAsync(instance);
    }

    [RelayCommand]
    private Task LoadMinecraftVersionsAsync()
    {
        return LoaderSelection.LoadMinecraftVersionsAsync();
    }

    [RelayCommand]
    public Task RefreshInstancesAsync()
    {
        return RunInstanceRefreshWithModSyncAsync(InstancesViewModel.RefreshInstancesAsync);
    }

    [RelayCommand]
    private Task LoadLoaderVersionsAsync()
    {
        return LoaderSelection.LoadLoaderVersionsAsync();
    }

    [RelayCommand]
    private Task CreateInstanceAsync()
    {
        return InstancesViewModel.CreateInstanceAsync(
            SelectedMinecraftVersion,
            SelectedLoader,
            SelectedLoaderVersion,
            CreateProgress());
    }

    [RelayCommand]
    private Task RefreshModsAsync()
    {
        return LocalMods.RefreshModsAsync();
    }

    [RelayCommand]
    private Task ToggleModAsync(LocalMod mod)
    {
        return LocalMods.ToggleModAsync(mod);
    }

    [RelayCommand]
    private Task DeleteModAsync(LocalMod mod)
    {
        return LocalMods.DeleteModAsync(mod);
    }

    [RelayCommand]
    private Task ImportModFromPathAsync(string path)
    {
        return LocalMods.ImportModFromPathAsync(path);
    }

    [RelayCommand]
    private Task SearchModsAsync()
    {
        return ModrinthSearch.SearchModsAsync(SelectedInstance);
    }

    [RelayCommand]
    private async Task InstallSelectedModAsync()
    {
        var installed = await ModrinthSearch.InstallSelectedModAsync(SelectedInstance, CreateProgress());
        if (installed)
            await LocalMods.RefreshModsAsync();
    }

    [RelayCommand]
    private Task SaveSettingsAsync()
    {
        return InstancesViewModel.SaveSettingsAsync();
    }

    [RelayCommand]
    private Task SaveInstanceAsync()
    {
        return InstancesViewModel.SaveInstanceAsync();
    }

    [RelayCommand]
    private Task SetDefaultInstanceAsync()
    {
        return InstancesViewModel.SetDefaultInstanceAsync();
    }

    private async Task RunInstanceRefreshWithModSyncAsync(Func<Task> refresh)
    {
        suppressSelectedInstanceModRefresh = true;
        try
        {
            await refresh();
        }
        finally
        {
            suppressSelectedInstanceModRefresh = false;
        }

        await LocalMods.SetSelectedInstanceAsync(SelectedInstance);
    }

    private IProgress<LauncherProgress> CreateProgress()
    {
        return new Progress<LauncherProgress>(progress =>
        {
            ReportStatus(LauncherProgressTextFormatter.Format(progress));
            ProgressPercent = progress.Percent ?? 0;
        });
    }

    private void InstancesViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        ForwardChildPropertyChanged(sender, e);

        if (e.PropertyName == nameof(InstanceManagementViewModel.SelectedInstance)
            && !suppressSelectedInstanceModRefresh)
        {
            _ = LocalMods.SetSelectedInstanceAsync(SelectedInstance);
        }
    }

    private void ForwardChildPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.PropertyName))
            OnPropertyChanged(e.PropertyName);
    }

    private void ReportStatus(string message)
    {
        statusService.Report(message);
    }
}

