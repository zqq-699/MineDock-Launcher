using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private readonly IFloatingMessageService floatingMessageService;
    private readonly ILogger<GameSettingsPageViewModel> logger;
    private IReadOnlyDictionary<string, string> versionTypesByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private bool hasLoadedInstances;
    private string lastImportDropHintMessage = string.Empty;
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

    [ObservableProperty]
    private bool isDeleteModsDialogOpen;

    [ObservableProperty]
    private ModDeleteRequest? pendingDeleteMods;

    [ObservableProperty]
    private SaveDeleteRequest? pendingDeleteSaves;

    [ObservableProperty]
    private ResourcePackDeleteRequest? pendingDeleteResourcePacks;

    [ObservableProperty]
    private ShaderPackDeleteRequest? pendingDeleteShaderPacks;

    [ObservableProperty]
    private bool isReplaceModImportDialogOpen;

    [ObservableProperty]
    private ModImportConflictRequest? pendingModImportConflict;

    [ObservableProperty]
    private bool isInvalidSaveImportDialogOpen;

    [ObservableProperty]
    private string invalidSaveImportDialogMessage = string.Empty;

    [ObservableProperty]
    private string invalidSaveImportDialogTitle = Strings.Dialog_InvalidSaveImportTitle;

    public GameSettingsPageViewModel(
        IGameInstanceService instanceService,
        IGameVersionService gameVersionService,
        IStatusService statusService,
        IInstanceFolderService instanceFolderService,
        ISystemMemoryService systemMemoryService,
        IModService modService,
        LocalModsViewModel localModsViewModel,
        LocalSavesViewModel localSavesViewModel,
        LocalResourcePacksViewModel localResourcePacksViewModel,
        LocalShaderPacksViewModel localShaderPacksViewModel,
        IJavaRuntimeDiscoveryService javaRuntimeDiscoveryService,
        IFilePickerService filePickerService,
        IFloatingMessageService floatingMessageService,
        ILogger<GameSettingsPageViewModel>? logger = null)
    {
        this.instanceService = instanceService;
        this.gameVersionService = gameVersionService;
        this.statusService = statusService;
        this.instanceFolderService = instanceFolderService;
        this.floatingMessageService = floatingMessageService;
        this.logger = logger ?? NullLogger<GameSettingsPageViewModel>.Instance;
        EditDialog = new GameSettingsEditDialogViewModel(instanceService, statusService);
        Details = new GameSettingsDetailsViewModel(
            EditDialog,
            instanceService,
            statusService,
            instanceFolderService,
            systemMemoryService,
            modService,
            localModsViewModel,
            localSavesViewModel,
            localResourcePacksViewModel,
            localShaderPacksViewModel,
            javaRuntimeDiscoveryService,
            filePickerService,
            floatingMessageService);
        EditDialog.InstanceUpdated += EditDialog_InstanceUpdated;
        EditDialog.InstanceRenameStarting += EditDialog_InstanceRenameStarting;
        EditDialog.InstanceRenameFinished += EditDialog_InstanceRenameFinished;
        Details.InstanceSettingsSaved += Details_InstanceSettingsSaved;
        Details.DeleteInstanceRequested += Details_DeleteInstanceRequested;
        Details.DeleteModsRequested += Details_DeleteModsRequested;
        Details.DeleteSavesRequested += Details_DeleteSavesRequested;
        Details.DeleteResourcePacksRequested += Details_DeleteResourcePacksRequested;
        Details.DeleteShaderPacksRequested += Details_DeleteShaderPacksRequested;
        Details.ImportModConflictRequested += Details_ImportModConflictRequested;
        Details.OnlineModInstallRequested += Details_OnlineModInstallRequested;
        Details.SaveImportFailedRequested += Details_SaveImportFailedRequested;
        Details.ResourcePackImportFailedRequested += Details_ResourcePackImportFailedRequested;
        Details.ShaderPackImportFailedRequested += Details_ShaderPackImportFailedRequested;
        Details.PropertyChanged += Details_PropertyChanged;
        Details.ModManagement.PropertyChanged += ModManagement_PropertyChanged;
        Details.SaveManagement.PropertyChanged += SaveManagement_PropertyChanged;
        Details.ResourcePackManagement.PropertyChanged += ResourcePackManagement_PropertyChanged;
        Details.ShaderPackManagement.PropertyChanged += ShaderPackManagement_PropertyChanged;

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

    public event Action<GameSettingsInstancesChangedEventArgs>? InstancesChanged;

    public event Action<GameInstance>? OnlineModInstallRequested;

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

    public bool IsModManagementDetailsStep => IsDetailsStep
        && string.Equals(Details.SelectedSection?.Id, "mod_management", StringComparison.OrdinalIgnoreCase);

    public bool IsSaveManagementDetailsStep => IsDetailsStep
        && string.Equals(Details.SelectedSection?.Id, "saves", StringComparison.OrdinalIgnoreCase);

    public bool IsResourcePackManagementDetailsStep => IsDetailsStep
        && string.Equals(Details.SelectedSection?.Id, "resource_packs", StringComparison.OrdinalIgnoreCase);

    public bool IsShaderPackManagementDetailsStep => IsDetailsStep
        && string.Equals(Details.SelectedSection?.Id, "shaders", StringComparison.OrdinalIgnoreCase);

    public bool IsTopResourceManagementDetailsStep => IsModManagementDetailsStep
        || IsSaveManagementDetailsStep
        || IsResourcePackManagementDetailsStep
        || IsShaderPackManagementDetailsStep;

    public bool IsTopSearchVisible => IsListStep || IsTopResourceManagementDetailsStep;

    public string TopSearchQuery
    {
        get
        {
            if (IsModManagementDetailsStep)
                return Details.ModManagement.ModSearchQuery;
            if (IsSaveManagementDetailsStep)
                return Details.SaveManagement.SaveSearchQuery;
            if (IsResourcePackManagementDetailsStep)
                return Details.ResourcePackManagement.ResourcePackSearchQuery;
            if (IsShaderPackManagementDetailsStep)
                return Details.ShaderPackManagement.ShaderPackSearchQuery;

            return InstanceSearchQuery;
        }
        set
        {
            if (IsModManagementDetailsStep)
            {
                Details.ModManagement.ModSearchQuery = value;
                OnPropertyChanged();
                return;
            }

            if (IsSaveManagementDetailsStep)
            {
                Details.SaveManagement.SaveSearchQuery = value;
                OnPropertyChanged();
                return;
            }

            if (IsResourcePackManagementDetailsStep)
            {
                Details.ResourcePackManagement.ResourcePackSearchQuery = value;
                OnPropertyChanged();
                return;
            }

            if (IsShaderPackManagementDetailsStep)
            {
                Details.ShaderPackManagement.ShaderPackSearchQuery = value;
                OnPropertyChanged();
                return;
            }

            if (IsListStep)
            {
                InstanceSearchQuery = value;
                OnPropertyChanged();
            }
        }
    }

    public string DeleteInstanceDialogMessage => InstancePendingDelete is null
        ? string.Empty
        : string.Format(Strings.Dialog_DeleteInstanceMessageFormat, InstancePendingDelete.Name);

    public string DeleteModsDialogMessage
    {
        get
        {
            if (PendingDeleteSaves is not null)
            {
                return PendingDeleteSaves.Titles.Count == 1
                    ? string.Format(Strings.Dialog_DeleteSingleSaveMessageFormat, PendingDeleteSaves.Titles[0])
                    : string.Format(Strings.Dialog_DeleteMultipleSavesMessageFormat, PendingDeleteSaves.Titles.Count);
            }

            if (PendingDeleteResourcePacks is not null)
            {
                return PendingDeleteResourcePacks.Titles.Count == 1
                    ? string.Format(Strings.Dialog_DeleteSingleResourcePackMessageFormat, PendingDeleteResourcePacks.Titles[0])
                    : string.Format(Strings.Dialog_DeleteMultipleResourcePacksMessageFormat, PendingDeleteResourcePacks.Titles.Count);
            }

            if (PendingDeleteShaderPacks is not null)
            {
                return PendingDeleteShaderPacks.Titles.Count == 1
                    ? string.Format(Strings.Dialog_DeleteSingleShaderPackMessageFormat, PendingDeleteShaderPacks.Titles[0])
                    : string.Format(Strings.Dialog_DeleteMultipleShaderPacksMessageFormat, PendingDeleteShaderPacks.Titles.Count);
            }

            if (PendingDeleteMods is null)
                return string.Empty;

            return PendingDeleteMods.Titles.Count == 1
                ? string.Format(Strings.Dialog_DeleteSingleModMessageFormat, PendingDeleteMods.Titles[0])
                : string.Format(Strings.Dialog_DeleteMultipleModsMessageFormat, PendingDeleteMods.Titles.Count);
        }
    }

    public string DeleteModsDialogTitle => PendingDeleteSaves is not null
        ? Strings.Dialog_DeleteSavesTitle
        : PendingDeleteResourcePacks is not null
            ? Strings.Dialog_DeleteResourcePacksTitle
            : PendingDeleteShaderPacks is not null
                ? Strings.Dialog_DeleteShaderPacksTitle
            : Strings.Dialog_DeleteModsTitle;

    public string ReplaceModImportDialogMessage => PendingModImportConflict is null
        ? string.Empty
        : string.Format(Strings.Dialog_ReplaceModImportMessageFormat, PendingModImportConflict.FileName);

    public bool UpdateImportDropState(IReadOnlyList<string> paths)
    {
        var evaluation = EvaluateImportDrop(paths);
        ApplyImportDropHint(evaluation);
        return evaluation.ShouldHandle && evaluation.CanAccept;
    }

    public void ClearImportDropState()
    {
        lastImportDropHintMessage = string.Empty;
        floatingMessageService.Show(string.Empty);
    }

    public async Task HandleImportDropAsync(IReadOnlyList<string> paths)
    {
        var evaluation = EvaluateImportDrop(paths);
        ApplyImportDropHint(evaluation);
        if (!evaluation.ShouldHandle)
        {
            ClearImportDropState();
            return;
        }

        ClearImportDropState();
        if (!evaluation.CanAccept)
            return;
        try
        {
            logger.LogInformation(
                "Handling game settings import drop. Section={SectionId} FileCount={FileCount} InstanceId={InstanceId}",
                Details.SelectedSection?.Id ?? "<none>",
                paths.Count,
                SelectedInstance?.Instance.Id ?? "<none>");
            await Details.HandleImportDropAsync(paths);
        }
        finally
        {
            ClearImportDropState();
        }
    }

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
        await OpenInstanceDetailsAsync(instance, sectionId: null, cancellationToken);
    }

    private async Task OpenInstanceDetailsAsync(GameInstance? instance, string? sectionId, CancellationToken cancellationToken = default)
    {
        await RefreshInstancesCoreAsync(
            playEntranceAnimation: !hasLoadedInstances,
            clearVisibleInstancesBeforeRefresh: !hasLoadedInstances,
            logRefreshResult: true,
            cancellationToken);

        ShowInstanceDetails(instance, sectionId);
    }

    public void ShowInstanceDetails(GameInstance? instance, string? sectionId = null)
    {
        if (instance is null)
        {
            CurrentStep = GameSettingsPageStep.List;
            return;
        }

        var targetItem = FindInstanceItem(instance.Id);
        if (targetItem is null)
        {
            targetItem = CreateInstanceItem(instance);
            AllInstances.Add(targetItem);
            RefreshVisibleInstances();
        }

        SelectInstanceCore(targetItem);
        SelectDetailsSectionCore(ResolveDetailSection(sectionId));
        CurrentStep = GameSettingsPageStep.Details;
    }

    public async Task OpenInstanceJavaSettingsAsync(GameInstance instance, CancellationToken cancellationToken = default)
    {
        await OpenInstanceDetailsAsync(instance, "java", cancellationToken);
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
    private Task SelectInstanceCategoryAsync(GameSettingsInstanceCategory category)
    {
        var isCategorySwitch = !ReferenceEquals(SelectedInstanceCategory, category)
            && !string.Equals(SelectedInstanceCategory?.Id, category.Id, StringComparison.OrdinalIgnoreCase);

        SelectInstanceCategoryCore(category);
        if (hasLoadedInstances && isCategorySwitch)
            ListEntranceAnimationToken++;

        return Task.CompletedTask;
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
        RefreshVisibleInstances();
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

    private void Details_DeleteModsRequested(ModDeleteRequest request)
    {
        PendingDeleteSaves = null;
        PendingDeleteResourcePacks = null;
        PendingDeleteShaderPacks = null;
        PendingDeleteMods = request;
        IsDeleteModsDialogOpen = true;
    }

    private void Details_OnlineModInstallRequested(GameInstance instance)
    {
        OnlineModInstallRequested?.Invoke(instance);
    }

    private void Details_DeleteSavesRequested(SaveDeleteRequest request)
    {
        PendingDeleteMods = null;
        PendingDeleteResourcePacks = null;
        PendingDeleteShaderPacks = null;
        PendingDeleteSaves = request;
        IsDeleteModsDialogOpen = true;
    }

    private void Details_DeleteResourcePacksRequested(ResourcePackDeleteRequest request)
    {
        PendingDeleteMods = null;
        PendingDeleteSaves = null;
        PendingDeleteShaderPacks = null;
        PendingDeleteResourcePacks = request;
        IsDeleteModsDialogOpen = true;
    }

    private void Details_DeleteShaderPacksRequested(ShaderPackDeleteRequest request)
    {
        PendingDeleteMods = null;
        PendingDeleteSaves = null;
        PendingDeleteResourcePacks = null;
        PendingDeleteShaderPacks = request;
        IsDeleteModsDialogOpen = true;
    }

    private void Details_ImportModConflictRequested(ModImportConflictRequest request)
    {
        PendingModImportConflict = request;
        IsReplaceModImportDialogOpen = true;
    }

    private void Details_SaveImportFailedRequested(SaveImportFailureRequest request)
    {
        InvalidSaveImportDialogTitle = Strings.Dialog_InvalidSaveImportTitle;
        InvalidSaveImportDialogMessage = request.Message;
        IsInvalidSaveImportDialogOpen = true;
    }

    private void Details_ResourcePackImportFailedRequested(ResourcePackImportFailureRequest request)
    {
        InvalidSaveImportDialogTitle = Strings.Dialog_InvalidResourcePackImportTitle;
        InvalidSaveImportDialogMessage = request.Message;
        IsInvalidSaveImportDialogOpen = true;
    }

    private void Details_ShaderPackImportFailedRequested(ShaderPackImportFailureRequest request)
    {
        InvalidSaveImportDialogTitle = Strings.Dialog_InvalidShaderPackImportTitle;
        InvalidSaveImportDialogMessage = request.Message;
        IsInvalidSaveImportDialogOpen = true;
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
            InstancesChanged?.Invoke(GameSettingsInstancesChangedEventArgs.Deleted(pendingDelete.Instance.Id));
        }
        catch (Exception)
        {
            statusService.Report(Strings.Status_DeleteInstanceFailed);
        }
    }

    [RelayCommand]
    private void CancelDeleteModsDialog()
    {
        IsDeleteModsDialogOpen = false;
        PendingDeleteMods = null;
        PendingDeleteSaves = null;
        PendingDeleteResourcePacks = null;
        PendingDeleteShaderPacks = null;
    }

    [RelayCommand]
    private async Task ConfirmDeleteModsDialogAsync()
    {
        if (PendingDeleteMods is null && PendingDeleteSaves is null && PendingDeleteResourcePacks is null && PendingDeleteShaderPacks is null)
            return;

        var modRequest = PendingDeleteMods;
        var saveRequest = PendingDeleteSaves;
        var resourcePackRequest = PendingDeleteResourcePacks;
        var shaderPackRequest = PendingDeleteShaderPacks;
        IsDeleteModsDialogOpen = false;
        PendingDeleteMods = null;
        PendingDeleteSaves = null;
        PendingDeleteResourcePacks = null;
        PendingDeleteShaderPacks = null;

        if (modRequest is not null)
        {
            await Details.DeleteModsAsync(modRequest.FullPaths);
        }
        else if (saveRequest is not null)
        {
            await Details.DeleteSavesAsync(saveRequest.FullPaths);
        }
        else if (resourcePackRequest is not null)
        {
            await Details.DeleteResourcePacksAsync(resourcePackRequest.FullPaths);
        }
        else if (shaderPackRequest is not null)
        {
            await Details.DeleteShaderPacksAsync(shaderPackRequest.FullPaths);
        }
    }

    [RelayCommand]
    private void CancelReplaceModImportDialog()
    {
        IsReplaceModImportDialogOpen = false;
        PendingModImportConflict = null;
        Details.ResolvePendingModImportConflict(false);
    }

    [RelayCommand]
    private Task ConfirmReplaceModImportDialogAsync()
    {
        if (PendingModImportConflict is null)
            return Task.CompletedTask;

        IsReplaceModImportDialogOpen = false;
        PendingModImportConflict = null;
        Details.ResolvePendingModImportConflict(true);
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void CloseInvalidSaveImportDialog()
    {
        IsInvalidSaveImportDialogOpen = false;
        InvalidSaveImportDialogMessage = string.Empty;
        InvalidSaveImportDialogTitle = Strings.Dialog_InvalidSaveImportTitle;
    }

    [RelayCommand]
    private void OpenInstanceFolder(GameSettingsInstanceItem instance)
    {
        var folderPath = instance.Instance.InstanceDirectory;
        if (!instanceFolderService.DirectoryExists(folderPath))
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

    private void EditDialog_InstanceRenameStarting()
    {
        Details.SuspendLocalWatchersForInstanceRename();
    }

    private void EditDialog_InstanceRenameFinished()
    {
        Details.ResumeLocalWatchersAfterInstanceRename();
    }

    partial void OnSelectedInstanceCategoryChanged(GameSettingsInstanceCategory? value)
    {
        OnPropertyChanged(nameof(PageTitle));
    }

    partial void OnCurrentStepChanged(GameSettingsPageStep value)
    {
        OnPropertyChanged(nameof(IsListStep));
        OnPropertyChanged(nameof(IsDetailsStep));
        RaiseTopSearchPropertyChanges();
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

    partial void OnPendingDeleteModsChanged(ModDeleteRequest? value)
    {
        OnPropertyChanged(nameof(DeleteModsDialogTitle));
        OnPropertyChanged(nameof(DeleteModsDialogMessage));
    }

    partial void OnPendingDeleteSavesChanged(SaveDeleteRequest? value)
    {
        OnPropertyChanged(nameof(DeleteModsDialogTitle));
        OnPropertyChanged(nameof(DeleteModsDialogMessage));
    }

    partial void OnPendingDeleteResourcePacksChanged(ResourcePackDeleteRequest? value)
    {
        OnPropertyChanged(nameof(DeleteModsDialogTitle));
        OnPropertyChanged(nameof(DeleteModsDialogMessage));
    }

    partial void OnPendingDeleteShaderPacksChanged(ShaderPackDeleteRequest? value)
    {
        OnPropertyChanged(nameof(DeleteModsDialogTitle));
        OnPropertyChanged(nameof(DeleteModsDialogMessage));
    }

    partial void OnPendingModImportConflictChanged(ModImportConflictRequest? value)
    {
        OnPropertyChanged(nameof(ReplaceModImportDialogMessage));
    }

    partial void OnInstanceSearchQueryChanged(string value)
    {
        RefreshVisibleInstances();
        OnPropertyChanged(nameof(TopSearchQuery));
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
        if (ShouldClearSelectedInstanceForCurrentStep(result.ShouldClearSelectedInstance))
            ClearSelectedInstance();

        ApplyVisibleInstances(result.Instances);
    }

    private bool ShouldClearSelectedInstanceForCurrentStep(bool shouldClearSelectedInstance)
    {
        if (!shouldClearSelectedInstance)
            return false;

        if (!IsDetailsStep)
            return true;

        return SelectedInstance is not null && !IsSelectedInstanceInAllInstances();
    }

    private bool IsSelectedInstanceInAllInstances()
    {
        if (SelectedInstance is null)
            return false;

        return AllInstances.Any(item => ReferenceEquals(item, SelectedInstance)
            || (!string.IsNullOrWhiteSpace(item.Instance.Id)
                && string.Equals(item.Instance.Id, SelectedInstance.Instance.Id, StringComparison.OrdinalIgnoreCase)));
    }

    private void ClearSelectedInstance()
    {
        SelectInstanceCore(null);
    }

    private GameSettingsFileDropEvaluation EvaluateImportDrop(IReadOnlyList<string> paths)
    {
        if (!IsDetailsStep)
            return GameSettingsFileDropEvaluation.Hidden;

        return Details.EvaluateImportDrop(paths);
    }

    private void ApplyImportDropHint(GameSettingsFileDropEvaluation evaluation)
    {
        var message = evaluation.ShouldHandle
            ? evaluation.CanAccept
                ? Strings.GameSettings_DropReleaseToImportMessage
                : Strings.GameSettings_DropUnsupportedFileMessage
            : string.Empty;

        if (string.Equals(lastImportDropHintMessage, message, StringComparison.Ordinal))
            return;

        lastImportDropHintMessage = message;
        floatingMessageService.Show(message);
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

    private GameSettingsInstanceItem? FindInstanceItem(string? instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            return null;

        return AllInstances.FirstOrDefault(item =>
            string.Equals(item.Instance.Id, instanceId, StringComparison.OrdinalIgnoreCase));
    }

    private GameSettingsDetailSectionItem? ResolveDetailSection(string? sectionId)
    {
        if (!string.IsNullOrWhiteSpace(sectionId))
        {
            var section = DetailSections.FirstOrDefault(item =>
                string.Equals(item.Id, sectionId, StringComparison.OrdinalIgnoreCase));
            if (section is not null)
                return section;
        }

        return DetailSections.FirstOrDefault();
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

        SelectInstanceCore(FindInstanceItem(selectedInstanceId));
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
            var versions = await gameVersionService.GetVersionsAsync(cancellationToken: cancellationToken);
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

    private void Details_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GameSettingsDetailsViewModel.SelectedSection)
            or nameof(GameSettingsDetailsViewModel.CurrentSectionViewModel))
        {
            RaiseTopSearchPropertyChanges();
        }
    }

    private void ModManagement_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(InstanceModManagementSettingsViewModel.ModSearchQuery))
            OnPropertyChanged(nameof(TopSearchQuery));
    }

    private void SaveManagement_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(InstanceSaveManagementSettingsViewModel.SaveSearchQuery))
            OnPropertyChanged(nameof(TopSearchQuery));
    }

    private void ResourcePackManagement_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(InstanceResourcePackManagementSettingsViewModel.ResourcePackSearchQuery))
            OnPropertyChanged(nameof(TopSearchQuery));
    }

    private void ShaderPackManagement_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(InstanceShaderPackManagementSettingsViewModel.ShaderPackSearchQuery))
            OnPropertyChanged(nameof(TopSearchQuery));
    }

    private void RaiseTopSearchPropertyChanges()
    {
        OnPropertyChanged(nameof(IsModManagementDetailsStep));
        OnPropertyChanged(nameof(IsSaveManagementDetailsStep));
        OnPropertyChanged(nameof(IsResourcePackManagementDetailsStep));
        OnPropertyChanged(nameof(IsShaderPackManagementDetailsStep));
        OnPropertyChanged(nameof(IsTopResourceManagementDetailsStep));
        OnPropertyChanged(nameof(IsTopSearchVisible));
        OnPropertyChanged(nameof(TopSearchQuery));
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

        InstancesChanged?.Invoke(GameSettingsInstancesChangedEventArgs.Updated(updatedInstance));
    }

    private void Details_InstanceSettingsSaved(GameInstance instance)
    {
        AddOrUpdateInstance(instance);
        InstancesChanged?.Invoke(GameSettingsInstancesChangedEventArgs.Updated(instance));
    }
}
