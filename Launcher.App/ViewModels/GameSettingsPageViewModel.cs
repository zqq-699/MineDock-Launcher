using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels;

public sealed partial class GameSettingsPageViewModel : ObservableObject
{
    private readonly IGameInstanceService instanceService;
    private readonly IGameVersionService gameVersionService;
    private IReadOnlyDictionary<string, string> versionTypesByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private bool hasLoadedInstances;

    [ObservableProperty]
    private GameSettingsInstanceCategory? selectedInstanceCategory;

    [ObservableProperty]
    private GameSettingsInstanceItem? selectedInstance;

    [ObservableProperty]
    private bool isLoadingInstances;

    [ObservableProperty]
    private string instanceLoadError = string.Empty;

    [ObservableProperty]
    private string instanceEmptyMessage = string.Empty;

    [ObservableProperty]
    private string instanceSearchQuery = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<GameSettingsInstanceItem> visibleInstances = Array.Empty<GameSettingsInstanceItem>();

    [ObservableProperty]
    private int listEntranceAnimationToken;

    public GameSettingsPageViewModel(
        IGameInstanceService instanceService,
        IGameVersionService gameVersionService)
    {
        this.instanceService = instanceService;
        this.gameVersionService = gameVersionService;

        InstanceCategories.Add(new GameSettingsInstanceCategory("all", Strings.GameSettings_AllCategory, string.Empty, "main_menu_instance_setting"));
        InstanceCategories.Add(new GameSettingsInstanceCategory("mod_loader", Strings.GameSettings_ModLoaderCategory, string.Empty, "main_menu_library"));
        InstanceCategories.Add(new GameSettingsInstanceCategory("release", Strings.Download_ReleaseCategory, string.Empty, "instance_download_page/release"));
        InstanceCategories.Add(new GameSettingsInstanceCategory("snapshot", Strings.Download_SnapshotCategory, string.Empty, "instance_download_page/snapshot"));
        InstanceCategories.Add(new GameSettingsInstanceCategory("old_beta", Strings.Download_BetaCategory, "\u03b2"));
        InstanceCategories.Add(new GameSettingsInstanceCategory("old_alpha", Strings.Download_AlphaCategory, "\u03b1"));

        SelectInstanceCategoryCore(InstanceCategories.First());
    }

    public ObservableCollection<GameSettingsInstanceCategory> InstanceCategories { get; } = [];

    public List<GameSettingsInstanceItem> AllInstances { get; } = [];

    public bool HasVisibleInstances => VisibleInstances.Count > 0;

    public bool HasInstanceLoadError => !string.IsNullOrWhiteSpace(InstanceLoadError);

    public bool HasInstanceEmptyMessage => !string.IsNullOrWhiteSpace(InstanceEmptyMessage);

    public string PageTitle => SelectedInstanceCategory?.Title ?? Strings.GameSettings_AllCategory;

    public async Task EnsureInstancesLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (hasLoadedInstances || IsLoadingInstances)
            return;

        await RefreshInstancesAsync(cancellationToken);
    }

    [RelayCommand]
    public async Task RefreshInstancesAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoadingInstances)
            return;

        IsLoadingInstances = true;
        InstanceLoadError = string.Empty;
        InstanceEmptyMessage = string.Empty;
        VisibleInstances = Array.Empty<GameSettingsInstanceItem>();
        var selectedInstanceId = SelectedInstance?.Instance.Id;

        try
        {
            var instances = await instanceService.GetInstancesAsync(cancellationToken);
            var versionTypes = await LoadVersionTypesAsync(cancellationToken);
            versionTypesByName = versionTypes;

            AllInstances.Clear();
            AllInstances.AddRange(instances.Select(instance => CreateInstanceItem(instance)));
            RestoreSelectedInstance(selectedInstanceId);
            hasLoadedInstances = true;
        }
        catch (Exception)
        {
            InstanceLoadError = Strings.Status_LoadInstancesFailed;
            hasLoadedInstances = false;
        }
        finally
        {
            IsLoadingInstances = false;

            if (hasLoadedInstances)
                ListEntranceAnimationToken++;

            RefreshVisibleInstances();
        }
    }

    [RelayCommand]
    private async Task SelectInstanceCategoryAsync(GameSettingsInstanceCategory category)
    {
        SelectInstanceCategoryCore(category, refreshVisibleInstances: false);
        await RefreshInstancesAsync();
    }

    [RelayCommand]
    private void SelectInstance(GameSettingsInstanceItem instance)
    {
        SelectInstanceCore(instance);
    }

    public void AddOrUpdateInstance(GameInstance instance)
    {
        if (!hasLoadedInstances)
            return;

        var existingIndex = AllInstances.FindIndex(item => string.Equals(item.Instance.Id, instance.Id, StringComparison.OrdinalIgnoreCase));
        var item = CreateInstanceItem(instance);

        if (existingIndex >= 0)
        {
            var wasSelected = ReferenceEquals(SelectedInstance, AllInstances[existingIndex])
                || string.Equals(SelectedInstance?.Instance.Id, instance.Id, StringComparison.OrdinalIgnoreCase);
            AllInstances[existingIndex] = item;
            if (wasSelected)
                SelectInstance(item);
        }
        else
        {
            AllInstances.Add(item);
        }

        RefreshVisibleInstances();
    }

    private void SelectInstanceCategoryCore(GameSettingsInstanceCategory category, bool refreshVisibleInstances = true)
    {
        SelectedInstanceCategory = category;
        foreach (var item in InstanceCategories)
            item.IsSelected = ReferenceEquals(item, category);

        if (refreshVisibleInstances)
            RefreshVisibleInstances();
    }

    partial void OnSelectedInstanceCategoryChanged(GameSettingsInstanceCategory? value)
    {
        OnPropertyChanged(nameof(PageTitle));
    }

    partial void OnInstanceSearchQueryChanged(string value)
    {
        RefreshVisibleInstances();
    }

    partial void OnInstanceLoadErrorChanged(string value)
    {
        OnPropertyChanged(nameof(HasInstanceLoadError));
    }

    partial void OnInstanceEmptyMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasInstanceEmptyMessage));
    }

    partial void OnVisibleInstancesChanged(IReadOnlyList<GameSettingsInstanceItem> value)
    {
        OnPropertyChanged(nameof(HasVisibleInstances));
        OnPropertyChanged(nameof(HasInstanceEmptyMessage));
    }

    private void RefreshVisibleInstances()
    {
        var result = GameSettingsInstanceFilter.Apply(
            AllInstances,
            SelectedInstanceCategory,
            InstanceSearchQuery,
            SelectedInstance,
            hasLoadedInstances,
            IsLoadingInstances,
            HasInstanceLoadError);

        InstanceEmptyMessage = result.EmptyMessage;
        if (result.ShouldClearSelectedInstance)
            ClearSelectedInstance();

        VisibleInstances = result.Instances;
    }

    private void ClearSelectedInstance()
    {
        SelectInstanceCore(null);
    }

    private GameSettingsInstanceItem CreateInstanceItem(GameInstance instance)
    {
        var versionName = string.IsNullOrWhiteSpace(instance.MinecraftVersion)
            ? instance.VersionName
            : instance.MinecraftVersion;
        var versionType = !string.IsNullOrWhiteSpace(versionName) && versionTypesByName.TryGetValue(versionName, out var type)
            ? type
            : string.Empty;

        return new GameSettingsInstanceItem(instance, versionType);
    }

    private void RestoreSelectedInstance(string? selectedInstanceId)
    {
        if (string.IsNullOrWhiteSpace(selectedInstanceId))
        {
            ClearSelectedInstance();
            return;
        }

        SelectInstanceCore(AllInstances.FirstOrDefault(item =>
            string.Equals(item.Instance.Id, selectedInstanceId, StringComparison.OrdinalIgnoreCase)));
    }

    private void SelectInstanceCore(GameSettingsInstanceItem? instance)
    {
        SelectedInstance = instance;
        foreach (var item in AllInstances)
            item.IsSelected = ReferenceEquals(item, instance);
    }

    private async Task<IReadOnlyDictionary<string, string>> LoadVersionTypesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var versions = await gameVersionService.GetVersionsAsync(cancellationToken);
            return versions
                .GroupBy(version => version.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => GameSettingsInstanceItem.NormalizeVersionType(group.First().Type),
                    StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
