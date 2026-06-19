using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.GameSettings;

public sealed partial class GameSettingsPageViewModel : ObservableObject
{
    private readonly IGameInstanceService instanceService;
    private readonly IGameVersionService gameVersionService;
    private readonly IStatusService statusService;
    private readonly IInstanceFolderService instanceFolderService;
    private readonly ILogger<GameSettingsPageViewModel> logger;
    private IReadOnlyDictionary<string, string> versionTypesByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private bool hasLoadedInstances;
    private INotifyPropertyChanged? selectedInstanceNotifier;

    [ObservableProperty]
    private GameSettingsInstanceCategory? selectedInstanceCategory;

    [ObservableProperty]
    private GameSettingsInstanceItem? selectedInstance;

    [ObservableProperty]
    private GameSettingsPageStep currentStep = GameSettingsPageStep.List;

    [ObservableProperty]
    private bool isLoadingInstances;

    [ObservableProperty]
    private string instanceLoadError = string.Empty;

    [ObservableProperty]
    private string instanceEmptyMessage = string.Empty;

    [ObservableProperty]
    private string instanceSearchQuery = string.Empty;

    [ObservableProperty]
    private int listEntranceAnimationToken;

    [ObservableProperty]
    private bool isDeleteInstanceDialogOpen;

    [ObservableProperty]
    private GameSettingsInstanceItem? instancePendingDelete;

    public GameSettingsPageViewModel(
        IGameInstanceService instanceService,
        IGameVersionService gameVersionService,
        IStatusService statusService,
        IInstanceFolderService instanceFolderService,
        IJavaRuntimeDiscoveryService javaRuntimeDiscoveryService,
        IFilePickerService filePickerService,
        IFloatingMessageService floatingMessageService,
        ILogger<GameSettingsPageViewModel>? logger = null)
    {
        this.instanceService = instanceService;
        this.gameVersionService = gameVersionService;
        this.statusService = statusService;
        this.instanceFolderService = instanceFolderService;
        this.logger = logger ?? NullLogger<GameSettingsPageViewModel>.Instance;
        EditDialog = new GameSettingsEditDialogViewModel(instanceService, statusService);
        Details = new GameSettingsDetailsViewModel(
            EditDialog,
            instanceService,
            statusService,
            instanceFolderService,
            javaRuntimeDiscoveryService,
            filePickerService,
            floatingMessageService);
        EditDialog.InstanceUpdated += EditDialog_InstanceUpdated;
        Details.InstanceSettingsSaved += Details_InstanceSettingsSaved;
        Details.DeleteInstanceRequested += Details_DeleteInstanceRequested;

        InstanceCategories.Add(new GameSettingsInstanceCategory("all", Strings.GameSettings_AllCategory, string.Empty, "general/general_all_application"));
        InstanceCategories.Add(new GameSettingsInstanceCategory("mod_loader", Strings.GameSettings_ModLoaderCategory, string.Empty, "general/general_extention"));
        InstanceCategories.Add(new GameSettingsInstanceCategory("release", Strings.Download_ReleaseCategory, string.Empty, "instance_download_page/release"));
        InstanceCategories.Add(new GameSettingsInstanceCategory("snapshot", Strings.Download_SnapshotCategory, string.Empty, "instance_download_page/snapshot"));
        InstanceCategories.Add(new GameSettingsInstanceCategory("old_beta", Strings.Download_BetaCategory, "\u03b2"));
        InstanceCategories.Add(new GameSettingsInstanceCategory("old_alpha", Strings.Download_AlphaCategory, "\u03b1"));
        foreach (var section in GameSettingsDetailSectionFactory.Create())
            DetailSections.Add(section);

        SelectInstanceCategoryCore(InstanceCategories.First());
        SelectDetailsSectionCore(DetailSections.FirstOrDefault());
    }

    public event Action<GameInstance>? LaunchInstanceRequested;

    public event Action? InstancesChanged;

    public ObservableCollection<GameSettingsInstanceCategory> InstanceCategories { get; } = [];

    public ObservableCollection<GameSettingsDetailSectionItem> DetailSections { get; } = [];

    public GameSettingsDetailsViewModel Details { get; }

    public GameSettingsEditDialogViewModel EditDialog { get; }

    public List<GameSettingsInstanceItem> AllInstances { get; } = [];

    public ObservableCollection<GameSettingsInstanceItem> VisibleInstances { get; } = [];

    public bool IsListStep => CurrentStep is GameSettingsPageStep.List;

    public bool IsDetailsStep => CurrentStep is GameSettingsPageStep.Details;

    public bool HasVisibleInstances => VisibleInstances.Count > 0;

    public bool HasInstanceLoadError => !string.IsNullOrWhiteSpace(InstanceLoadError);

    public bool HasInstanceEmptyMessage => !string.IsNullOrWhiteSpace(InstanceEmptyMessage);

    public System.Collections.IEnumerable CurrentSecondaryMenuItems => IsDetailsStep
        ? (System.Collections.IEnumerable)DetailSections
        : InstanceCategories;

    public string PageTitle => IsDetailsStep && SelectedInstance is not null
        ? SelectedInstance.Name
        : SelectedInstanceCategory?.Title ?? Strings.GameSettings_AllCategory;

    public string? PageTitleIconSource => IsDetailsStep && SelectedInstance is not null
        ? SelectedInstance.IconSource
        : null;

    public string DeleteInstanceDialogMessage => InstancePendingDelete is null
        ? string.Empty
        : string.Format(Strings.Dialog_DeleteInstanceMessageFormat, InstancePendingDelete.Name);

    public void PrimeFromSettings(LauncherSettings launcherSettings)
    {
        Details.PrimeFromSettings(launcherSettings);
    }

    public async Task EnsureInstancesLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (hasLoadedInstances || IsLoadingInstances)
            return;

        await RefreshInstancesCoreAsync(playEntranceAnimation: true, clearVisibleInstancesBeforeRefresh: true, logRefreshResult: true, cancellationToken: cancellationToken);
    }

    [RelayCommand]
    public async Task RefreshInstancesAsync(CancellationToken cancellationToken = default)
    {
        await RefreshInstancesCoreAsync(playEntranceAnimation: true, clearVisibleInstancesBeforeRefresh: true, logRefreshResult: true, cancellationToken: cancellationToken);
    }

    public async Task RefreshInstancesForPageActivationAsync(CancellationToken cancellationToken = default)
    {
        await RefreshInstancesCoreAsync(
            playEntranceAnimation: !hasLoadedInstances,
            clearVisibleInstancesBeforeRefresh: !hasLoadedInstances,
            logRefreshResult: true,
            cancellationToken);
    }

    public async Task RefreshInstancesSilentlyAsync(CancellationToken cancellationToken = default)
    {
        await RefreshInstancesCoreAsync(playEntranceAnimation: false, clearVisibleInstancesBeforeRefresh: false, logRefreshResult: false, cancellationToken: cancellationToken);
    }

    public async Task OpenInstanceDetailsAsync(GameInstance? instance, CancellationToken cancellationToken = default)
    {
        await RefreshInstancesCoreAsync(
            playEntranceAnimation: !hasLoadedInstances,
            clearVisibleInstancesBeforeRefresh: !hasLoadedInstances,
            logRefreshResult: true,
            cancellationToken);

        if (instance is null)
        {
            CurrentStep = GameSettingsPageStep.List;
            return;
        }

        var targetItem = AllInstances.FirstOrDefault(item =>
            string.Equals(item.Instance.Id, instance.Id, StringComparison.OrdinalIgnoreCase));
        if (targetItem is null)
        {
            CurrentStep = GameSettingsPageStep.List;
            return;
        }

        SelectInstanceCore(targetItem);
        SelectDetailsSectionCore(DetailSections.FirstOrDefault());
        CurrentStep = GameSettingsPageStep.Details;
    }

    private async Task RefreshInstancesCoreAsync(
        bool playEntranceAnimation,
        bool clearVisibleInstancesBeforeRefresh,
        bool logRefreshResult,
        CancellationToken cancellationToken = default)
    {
        if (IsLoadingInstances)
            return;

        IsLoadingInstances = true;
        InstanceLoadError = string.Empty;
        InstanceEmptyMessage = string.Empty;
        if (clearVisibleInstancesBeforeRefresh)
            ClearVisibleInstances();
        var selectedInstanceId = SelectedInstance?.Instance.Id;

        try
        {
            var instances = await instanceService.GetInstancesAsync(cancellationToken);
            var versionTypes = await LoadVersionTypesAsync(cancellationToken);
            versionTypesByName = versionTypes;

            ReconcileAllInstances(instances);
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

            if (hasLoadedInstances && playEntranceAnimation)
                ListEntranceAnimationToken++;

            RefreshVisibleInstances();
            if (hasLoadedInstances && logRefreshResult)
            {
                logger.LogInformation(
                    "Game settings instances refreshed. Count={InstanceCount} VisibleCount={VisibleCount} SelectedInstanceId={SelectedInstanceId}",
                    AllInstances.Count,
                    VisibleInstances.Count,
                    SelectedInstance?.Instance.Id);
            }
        }
    }

    [RelayCommand]
    private async Task SelectInstanceCategoryAsync(GameSettingsInstanceCategory category)
    {
        var isCategorySwitch = !ReferenceEquals(SelectedInstanceCategory, category)
            && !string.Equals(SelectedInstanceCategory?.Id, category.Id, StringComparison.OrdinalIgnoreCase);

        SelectInstanceCategoryCore(category, refreshVisibleInstances: false);
        await RefreshInstancesCoreAsync(
            playEntranceAnimation: isCategorySwitch,
            clearVisibleInstancesBeforeRefresh: isCategorySwitch,
            logRefreshResult: false);
    }

    [RelayCommand]
    private void SelectInstance(GameSettingsInstanceItem instance)
    {
        SelectInstanceCore(instance);
        SelectDetailsSectionCore(DetailSections.FirstOrDefault());
        CurrentStep = GameSettingsPageStep.Details;
    }

    [RelayCommand]
    private void SelectDetailsSection(GameSettingsDetailSectionItem section)
    {
        SelectDetailsSectionCore(section);
    }

    [RelayCommand]
    private async Task SelectSecondaryMenuItemAsync(object? item)
    {
        switch (item)
        {
            case GameSettingsInstanceCategory category:
                await SelectInstanceCategoryAsync(category);
                break;
            case GameSettingsDetailSectionItem section:
                SelectDetailsSectionCore(section);
                break;
        }
    }

    [RelayCommand]
    private void BackToInstanceList()
    {
        CurrentStep = GameSettingsPageStep.List;
    }

    [RelayCommand]
    private void CancelEditInstanceDialog()
    {
        EditDialog.Cancel();
    }

    [RelayCommand]
    private Task ConfirmEditInstanceDialogAsync()
    {
        return EditDialog.ConfirmAsync();
    }

    [RelayCommand]
    private void OpenDeleteInstanceDialog(GameSettingsInstanceItem instance)
    {
        InstancePendingDelete = instance;
        IsDeleteInstanceDialogOpen = true;
    }

    private void Details_DeleteInstanceRequested(GameSettingsInstanceItem instance)
    {
        OpenDeleteInstanceDialog(instance);
    }

    [RelayCommand]
    private void CancelDeleteInstanceDialog()
    {
        IsDeleteInstanceDialogOpen = false;
        InstancePendingDelete = null;
    }

    [RelayCommand]
    private async Task ConfirmDeleteInstanceDialogAsync()
    {
        if (InstancePendingDelete is null)
            return;

        var pendingDelete = InstancePendingDelete;
        var deletedName = pendingDelete.Name;

        IsDeleteInstanceDialogOpen = false;
        InstancePendingDelete = null;

        try
        {
            var deleted = await instanceService.DeleteInstanceAsync(pendingDelete.Instance.Id);
            if (!deleted)
            {
                statusService.Report(Strings.Status_DeleteInstanceFailed);
                return;
            }

            statusService.Report(string.Format(Strings.Status_InstanceDeletedFormat, deletedName));
            RemoveInstanceLocally(pendingDelete.Instance.Id);
            InstancesChanged?.Invoke();
        }
        catch (Exception)
        {
            statusService.Report(Strings.Status_DeleteInstanceFailed);
        }
    }

    [RelayCommand]
    private void OpenInstanceFolder(GameSettingsInstanceItem instance)
    {
        var folderPath = instance.Instance.InstanceDirectory;
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            statusService.Report(Strings.Status_InstanceFolderNotFound);
            return;
        }

        if (!instanceFolderService.TryOpen(folderPath))
            statusService.Report(Strings.Status_OpenInstanceFolderFailed);
    }

    [RelayCommand]
    private void SelectInstanceAndGoHome(GameSettingsInstanceItem instance)
    {
        LaunchInstanceRequested?.Invoke(instance.Instance);
    }

    public void AddOrUpdateInstance(GameInstance instance)
    {
        if (!hasLoadedInstances)
            return;

        var existingIndex = AllInstances.FindIndex(item => string.Equals(item.Instance.Id, instance.Id, StringComparison.OrdinalIgnoreCase));

        if (existingIndex >= 0)
        {
            var item = AllInstances[existingIndex];
            var wasSelected = ReferenceEquals(SelectedInstance, item)
                || string.Equals(SelectedInstance?.Instance.Id, instance.Id, StringComparison.OrdinalIgnoreCase);
            item.Update(instance, ResolveVersionType(instance));
            if (wasSelected)
                SelectInstanceCore(item);
        }
        else
        {
            var item = CreateInstanceItem(instance);
            AllInstances.Add(item);
        }

        RefreshVisibleInstances();
    }

    private void SelectInstanceCategoryCore(GameSettingsInstanceCategory category, bool refreshVisibleInstances = true)
    {
        CurrentStep = GameSettingsPageStep.List;
        SelectedInstanceCategory = category;
        foreach (var item in InstanceCategories)
            item.IsSelected = ReferenceEquals(item, category);

        if (refreshVisibleInstances)
            RefreshVisibleInstances();
    }

    private void SelectDetailsSectionCore(GameSettingsDetailSectionItem? section)
    {
        foreach (var item in DetailSections)
            item.IsSelected = ReferenceEquals(item, section);

        Details.SetSelectedSection(section);
    }

    partial void OnSelectedInstanceCategoryChanged(GameSettingsInstanceCategory? value)
    {
        OnPropertyChanged(nameof(PageTitle));
    }

    partial void OnCurrentStepChanged(GameSettingsPageStep value)
    {
        OnPropertyChanged(nameof(IsListStep));
        OnPropertyChanged(nameof(IsDetailsStep));
        OnPropertyChanged(nameof(CurrentSecondaryMenuItems));
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageTitleIconSource));
    }

    partial void OnSelectedInstanceChanged(GameSettingsInstanceItem? value)
    {
        if (selectedInstanceNotifier is not null)
            selectedInstanceNotifier.PropertyChanged -= SelectedInstance_PropertyChanged;

        selectedInstanceNotifier = value;
        if (selectedInstanceNotifier is not null)
            selectedInstanceNotifier.PropertyChanged += SelectedInstance_PropertyChanged;

        Details.SetSelectedInstance(value);
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageTitleIconSource));

        if (value is null && IsDetailsStep)
            CurrentStep = GameSettingsPageStep.List;
    }

    partial void OnInstancePendingDeleteChanged(GameSettingsInstanceItem? value)
    {
        OnPropertyChanged(nameof(DeleteInstanceDialogMessage));
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

        ApplyVisibleInstances(result.Instances);
    }

    private void ClearSelectedInstance()
    {
        SelectInstanceCore(null);
    }

    private void RemoveInstanceLocally(string instanceId)
    {
        var removedCount = AllInstances.RemoveAll(item =>
            string.Equals(item.Instance.Id, instanceId, StringComparison.OrdinalIgnoreCase));

        if (removedCount == 0)
            return;

        if (string.Equals(SelectedInstance?.Instance.Id, instanceId, StringComparison.OrdinalIgnoreCase))
            ClearSelectedInstance();

        RefreshVisibleInstances();
    }

    private void ReconcileAllInstances(IReadOnlyList<GameInstance> instances)
    {
        var existingById = AllInstances
            .Where(item => !string.IsNullOrWhiteSpace(item.Instance.Id))
            .GroupBy(item => item.Instance.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var nextItems = new List<GameSettingsInstanceItem>(instances.Count);

        foreach (var instance in instances)
        {
            if (!string.IsNullOrWhiteSpace(instance.Id)
                && existingById.TryGetValue(instance.Id, out var existingItem))
            {
                existingItem.Update(instance, ResolveVersionType(instance));
                nextItems.Add(existingItem);
            }
            else
            {
                nextItems.Add(CreateInstanceItem(instance));
            }
        }

        AllInstances.Clear();
        AllInstances.AddRange(nextItems);
    }

    private void ClearVisibleInstances()
    {
        if (VisibleInstances.Count == 0)
            return;

        VisibleInstances.Clear();
        NotifyVisibleInstancesChanged();
    }

    private void ApplyVisibleInstances(IReadOnlyList<GameSettingsInstanceItem> instances)
    {
        var changed = false;

        for (var index = VisibleInstances.Count - 1; index >= 0; index--)
        {
            if (ContainsReference(instances, VisibleInstances[index]))
                continue;

            VisibleInstances.RemoveAt(index);
            changed = true;
        }

        for (var index = 0; index < instances.Count; index++)
        {
            var instance = instances[index];
            if (index < VisibleInstances.Count && ReferenceEquals(VisibleInstances[index], instance))
                continue;

            var existingIndex = IndexOfVisibleInstance(instance, index + 1);
            if (existingIndex >= 0)
                VisibleInstances.Move(existingIndex, index);
            else
                VisibleInstances.Insert(index, instance);

            changed = true;
        }

        if (changed)
            NotifyVisibleInstancesChanged();
    }

    private GameSettingsInstanceItem CreateInstanceItem(GameInstance instance)
    {
        return new GameSettingsInstanceItem(instance, ResolveVersionType(instance));
    }

    private string ResolveVersionType(GameInstance instance)
    {
        if (!string.IsNullOrWhiteSpace(instance.VersionType))
            return instance.VersionType;

        var versionName = string.IsNullOrWhiteSpace(instance.MinecraftVersion)
            ? instance.VersionName
            : instance.MinecraftVersion;
        return !string.IsNullOrWhiteSpace(versionName) && versionTypesByName.TryGetValue(versionName, out var type)
            ? type
            : string.Empty;
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

    private int IndexOfVisibleInstance(GameSettingsInstanceItem instance, int startIndex)
    {
        for (var index = startIndex; index < VisibleInstances.Count; index++)
        {
            if (ReferenceEquals(VisibleInstances[index], instance))
                return index;
        }

        return -1;
    }

    private static bool ContainsReference(IEnumerable<GameSettingsInstanceItem> instances, GameSettingsInstanceItem candidate)
    {
        return instances.Any(instance => ReferenceEquals(instance, candidate));
    }

    private void NotifyVisibleInstancesChanged()
    {
        OnPropertyChanged(nameof(VisibleInstances));
        OnPropertyChanged(nameof(HasVisibleInstances));
        OnPropertyChanged(nameof(HasInstanceEmptyMessage));
    }

    private void SelectedInstance_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageTitleIconSource));
    }

    private void EditDialog_InstanceUpdated(GameInstance updatedInstance)
    {
        AddOrUpdateInstance(updatedInstance);

        var updatedItem = AllInstances.FirstOrDefault(item =>
            string.Equals(item.Instance.Id, updatedInstance.Id, StringComparison.OrdinalIgnoreCase));
        if (updatedItem is not null)
        {
            SelectInstanceCore(updatedItem);
            CurrentStep = GameSettingsPageStep.Details;
        }

        InstancesChanged?.Invoke();
    }

    private void Details_InstanceSettingsSaved(GameInstance instance)
    {
        AddOrUpdateInstance(instance);
        InstancesChanged?.Invoke();
    }
}
