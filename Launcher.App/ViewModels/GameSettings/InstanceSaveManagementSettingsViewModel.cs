using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.ViewModels.Shared;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.IO;

namespace Launcher.App.ViewModels.GameSettings;

public sealed partial class InstanceSaveManagementSettingsViewModel : GameSettingsDetailsSectionViewModelBase
{
    private readonly LocalSavesViewModel localSavesViewModel;
    private readonly IStatusService statusService;
    private readonly IInstanceFolderService instanceFolderService;
    private readonly IFilePickerService filePickerService;
    private readonly IUiDispatcher uiDispatcher;
    private readonly ILogger<InstanceSaveManagementSettingsViewModel> logger;
    private readonly LocalContentSelectionState<SaveManagementSaveItemViewModel> selectionState;
    private static readonly string[] SupportedSaveArchiveExtensions =
    [
        ".zip",
        ".7z",
        ".rar",
        ".tar",
        ".gz",
        ".tgz",
        ".bz2",
        ".tar.gz",
        ".tar.bz2",
        ".tbz2"
    ];
    private Task? loadTask;
    private GameInstance? selectedInstance;
    private bool hasPendingVisualRefresh;
    private bool isVisibleRefreshQueued;
    private bool isSectionActive;
    private bool suppressLocalCollectionEvents;

    [ObservableProperty]
    private int installedSaveCount;

    [ObservableProperty]
    private SaveManagementSaveItemViewModel? selectedSave;

    [ObservableProperty]
    private string saveSearchQuery = string.Empty;

    [ObservableProperty]
    private bool isMultiSelectMode;

    [ObservableProperty]
    private int selectedSaveCount;

    [ObservableProperty]
    private bool isLoadingSaves;

    [ObservableProperty]
    private bool hasLoadedSaves;

    [ObservableProperty]
    private IReadOnlyList<SaveManagementSaveItemViewModel> visibleSaves = Array.Empty<SaveManagementSaveItemViewModel>();

    [ObservableProperty]
    private IReadOnlyList<object> visibleSaveListItems = Array.Empty<object>();

    [ObservableProperty]
    private int listEntranceAnimationToken;

    public InstanceSaveManagementSettingsViewModel(
        GameSettingsDetailsViewModel parent,
        LocalSavesViewModel localSavesViewModel,
        IStatusService statusService,
        IInstanceFolderService instanceFolderService,
        IFilePickerService filePickerService,
        IUiDispatcher? uiDispatcher = null,
        ILogger<InstanceSaveManagementSettingsViewModel>? logger = null)
        : base(parent)
    {
        this.localSavesViewModel = localSavesViewModel;
        this.statusService = statusService;
        this.instanceFolderService = instanceFolderService;
        this.filePickerService = filePickerService;
        this.uiDispatcher = uiDispatcher ?? ImmediateUiDispatcher.Instance;
        this.logger = logger ?? NullLogger<InstanceSaveManagementSettingsViewModel>.Instance;
        selectionState = new LocalContentSelectionState<SaveManagementSaveItemViewModel>(
            save => save.FullPath,
            save => save.IsSelected,
            static (save, isSelected) => save.IsSelected = isSelected);
        this.localSavesViewModel.SavesChanged += LocalSavesViewModel_SavesChanged;
    }

    public event Action<SaveDeleteRequest>? DeleteSavesRequested;
    public event Action<SaveImportFailureRequest>? SaveImportFailedRequested;

    public override bool UsesFullViewportLayout => true;

    public IReadOnlyList<SaveManagementSaveItemViewModel> Saves => VisibleSaves;

    public bool CanShowSaveInfoSection => selectedInstance is not null;

    public bool HasSaves => Saves.Count > 0;

    public bool CanShowSaveScrollableContent => selectedInstance is not null;

    public bool HasInstalledSaves => InstalledSaveCount > 0;

    public bool CanShowSaveListSection => selectedInstance is not null && (IsLoadingSaves || HasInstalledSaves);

    public bool CanShowNoSavesEmptyState => selectedInstance is not null && HasLoadedSaves && !IsLoadingSaves && !HasInstalledSaves;

    public bool CanShowSaveEmptyState => selectedInstance is not null && HasLoadedSaves && !IsLoadingSaves && HasInstalledSaves && !HasSaves;

    public bool CanShowSaveLoadingState => selectedInstance is not null && IsLoadingSaves && !HasLoadedSaves;

    public bool HasSelectedSaves => SelectedSaveCount > 0;

    public bool AreAllVisibleSavesSelected => HasSaves && SelectedSaveCount == Saves.Count;

    public bool CanImportLocalSave => selectedInstance is not null;

    public string SelectAllButtonText => AreAllVisibleSavesSelected
        ? Strings.GameSettings_SaveManagementCancelSelectAllButton
        : Strings.GameSettings_SaveManagementSelectAllButton;

    public string InstalledSummaryText => IsLoadingSaves && !HasLoadedSaves
        ? Strings.GameSettings_SaveManagementLoading
        : string.Format(
            Strings.GameSettings_SaveManagementInstalledSummaryFormat,
            InstalledSaveCount);

    public string SaveEmptyMessage => !HasInstalledSaves || string.IsNullOrWhiteSpace(SaveSearchQuery)
        ? Strings.GameSettings_SaveManagementEmptyMessage
        : Strings.GameSettings_SaveManagementSearchEmptyMessage;

    public Task SetSelectedInstanceAsync(GameInstance? instance)
    {
        OnSelectedInstanceChanged(instance);
        return EnsureLoadedForSelectedInstanceAsync();
    }

    public override void OnSelectedInstanceChanged(GameInstance? instance)
    {
        selectedInstance = instance;
        loadTask = null;
        hasPendingVisualRefresh = false;
        isVisibleRefreshQueued = false;
        suppressLocalCollectionEvents = true;
        try
        {
            localSavesViewModel.SetSelectedInstance(instance);
            localSavesViewModel.SetWatcherEnabled(isSectionActive && selectedInstance is not null);
        }
        finally
        {
            suppressLocalCollectionEvents = false;
        }

        IsLoadingSaves = false;
        HasLoadedSaves = false;
        selectionState.ClearCache();
        ListEntranceAnimationToken = 0;
        ResetSelectionState();
        ClearDisplayedSaves();
        ImportLocalSaveCommand.NotifyCanExecuteChanged();
    }

    public bool RefreshSelectedInstanceReference(GameInstance? instance)
    {
        if (ShouldResetForInstanceReference(instance))
        {
            OnSelectedInstanceChanged(instance);
            return true;
        }

        selectedInstance = instance;
        ImportLocalSaveCommand.NotifyCanExecuteChanged();
        return false;
    }

    public override void OnSectionDeactivated()
    {
        isSectionActive = false;
        localSavesViewModel.SetWatcherEnabled(false);
    }

    public void SuspendLocalWatchersForInstanceRename()
    {
        localSavesViewModel.SuspendWatcherForInstanceRename();
    }

    public void ResumeLocalWatchersAfterInstanceRename()
    {
        localSavesViewModel.ResumeWatcherAfterInstanceRename();
    }

    public override Task OnSectionActivatedAsync()
    {
        isSectionActive = true;
        localSavesViewModel.SetWatcherEnabled(selectedInstance is not null);
        if (hasPendingVisualRefresh && HasLoadedSaves)
            QueueVisibleRefresh(playEntranceAnimation: true);

        return EnsureLoadedForSelectedInstanceAsync();
    }

    public Task EnsureLoadedForSelectedInstanceAsync()
    {
        if (selectedInstance is null)
            return Task.CompletedTask;

        if (loadTask is { IsCompleted: false })
            return loadTask;

        if (HasLoadedSaves)
            return Task.CompletedTask;

        loadTask = LoadSavesAsync();
        return loadTask;
    }

    [RelayCommand]
    private void OpenSaveFolder()
    {
        if (selectedInstance is null)
            return;

        try
        {
            var savesDirectory = instanceFolderService.EnsureDirectoryExists(
                Path.Combine(selectedInstance.InstanceDirectory, "saves"));
            logger.LogInformation(
                "Opening save folder. InstanceId={InstanceId} SavesDirectory={SavesDirectory}",
                selectedInstance.Id,
                savesDirectory);

            if (!instanceFolderService.TryOpen(savesDirectory))
            {
                logger.LogWarning(
                    "Failed to open save folder. InstanceId={InstanceId} SavesDirectory={SavesDirectory}",
                    selectedInstance.Id,
                    savesDirectory);
                statusService.Report(Strings.Status_OpenLocalSaveFolderFailed);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to prepare save folder for opening. InstanceId={InstanceId}",
                selectedInstance.Id);
            statusService.Report(Strings.Status_OpenLocalSaveFolderFailed);
        }
    }

    [RelayCommand(CanExecute = nameof(CanImportLocalSave))]
    private async Task ImportLocalSaveAsync()
    {
        if (selectedInstance is null)
            return;

        var archivePath = filePickerService.PickSaveArchive();
        if (string.IsNullOrWhiteSpace(archivePath))
            return;

        await ImportSaveArchivesAsync([archivePath], ImportTriggerSource.FilePicker);
    }

    public GameSettingsFileDropEvaluation EvaluateDroppedFiles(IReadOnlyList<string> paths)
    {
        return TryValidateImportPaths(paths, Strings.GameSettings_DropSaveArchivesOnlyMessage, out var failureMessage)
            ? GameSettingsFileDropEvaluation.Accept(Strings.GameSettings_DropImportSavesMessage)
            : GameSettingsFileDropEvaluation.Reject(failureMessage);
    }

    public Task ImportDroppedSaveArchivesAsync(IReadOnlyList<string> paths)
    {
        return ImportSaveArchivesAsync(paths, ImportTriggerSource.DragDrop);
    }

    [RelayCommand]
    private void ToggleMultiSelectMode()
    {
        if (IsMultiSelectMode)
        {
            ExitMultiSelectMode();
            return;
        }

        EnterMultiSelectMode();
    }

    [RelayCommand(CanExecute = nameof(CanToggleSelectAllSaves))]
    private void SelectAllSaves()
    {
        if (AreAllVisibleSavesSelected)
        {
            selectionState.ClearVisibleSelections(Saves);
            selectionState.ClearSelectedPaths();
            SelectedSave = null;
            UpdateSelectedSaveState();
            return;
        }

        selectionState.SelectAll(Saves);
        SelectedSave = null;
        UpdateSelectedSaveState();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedSaves))]
    private void RequestDeleteSelectedSaves()
    {
        var selectedSaves = GetSelectedVisibleSaves();
        if (selectedSaves.Count == 0)
            return;

        DeleteSavesRequested?.Invoke(new SaveDeleteRequest(
            selectedSaves.Select(save => save.FullPath).ToArray(),
            selectedSaves.Select(save => save.Title).ToArray()));
    }

    [RelayCommand]
    private void OpenSaveLocation(SaveManagementSaveItemViewModel? save)
    {
        if (save is null)
            return;

        try
        {
            if (!instanceFolderService.TryOpen(save.FullPath))
            {
                logger.LogWarning(
                    "Failed to open local save directory. InstanceId={InstanceId} Path={Path}",
                    selectedInstance?.Id ?? "<none>",
                    save.FullPath);
                statusService.Report(Strings.Status_OpenLocalSaveFolderFailed);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to open local save directory. InstanceId={InstanceId} Path={Path}",
                selectedInstance?.Id ?? "<none>",
                save.FullPath);
            statusService.Report(Strings.Status_OpenLocalSaveFolderFailed);
        }
    }

    [RelayCommand]
    private void RequestDeleteSave(SaveManagementSaveItemViewModel? save)
    {
        if (save is null)
            return;

        DeleteSavesRequested?.Invoke(new SaveDeleteRequest(
            [save.FullPath],
            [save.Title]));
    }

    [RelayCommand]
    private void SelectSave(SaveManagementSaveItemViewModel? save)
    {
        if (save is null)
        {
            SelectedSave = null;
            if (IsMultiSelectMode)
                selectionState.ClearSelectedPaths();
            selectionState.ClearVisibleSelections(Saves);
            UpdateSelectedSaveState();
            return;
        }

        if (IsMultiSelectMode)
        {
            selectionState.ToggleSelection(save);
            SelectedSave = null;
            UpdateSelectedSaveState();
            return;
        }

        SelectedSave = save;
        selectionState.SelectSingle(save, Saves);
    }

    public async Task DeleteSavesAsync(IReadOnlyList<string> fullPaths)
    {
        ArgumentNullException.ThrowIfNull(fullPaths);

        var savesToDelete = ResolveLocalSaves(fullPaths);
        if (savesToDelete.Count == 0)
        {
            ExitMultiSelectMode();
            return;
        }

        logger.LogInformation(
            "Deleting selected saves. InstanceId={InstanceId} Count={Count}",
            selectedInstance?.Id ?? "<none>",
            savesToDelete.Count);
        try
        {
            var failedCount = await localSavesViewModel.DeleteSavesAsync(savesToDelete);
            ExitMultiSelectMode();
            ReportBatchOperationResult(
                savesToDelete.Count,
                failedCount,
                Strings.Status_SelectedSavesDeletedFormat,
                Strings.Status_SelectedSavesDeletePartialFailedFormat);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to delete selected saves. InstanceId={InstanceId}",
                selectedInstance?.Id ?? "<none>");
            statusService.Report(Strings.Status_SelectedSavesDeleteFailed);
        }
    }

    partial void OnInstalledSaveCountChanged(int value)
    {
        OnPropertyChanged(nameof(InstalledSummaryText));
        RaiseAvailabilityPropertyChanges();
        OnPropertyChanged(nameof(SaveEmptyMessage));
    }

    partial void OnSaveSearchQueryChanged(string value)
    {
        RefreshFromLocalSaves();
        OnPropertyChanged(nameof(SaveEmptyMessage));
    }

    partial void OnSelectedSaveCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasSelectedSaves));
        OnPropertyChanged(nameof(AreAllVisibleSavesSelected));
        OnPropertyChanged(nameof(SelectAllButtonText));
        SelectAllSavesCommand.NotifyCanExecuteChanged();
        RequestDeleteSelectedSavesCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsMultiSelectModeChanged(bool value)
    {
        OnPropertyChanged(nameof(AreAllVisibleSavesSelected));
        OnPropertyChanged(nameof(SelectAllButtonText));
        SelectAllSavesCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadSavesAsync()
    {
        if (selectedInstance is null)
            return;

        IsLoadingSaves = true;
        OnPropertyChanged(nameof(InstalledSummaryText));
        RaiseAvailabilityPropertyChanges();

        try
        {
            await localSavesViewModel.RefreshSavesAsync();
            HasLoadedSaves = true;
            if (isSectionActive)
                ListEntranceAnimationToken++;
            else
                hasPendingVisualRefresh = true;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to load saves for section activation. InstanceId={InstanceId}",
                selectedInstance.Id);
            HasLoadedSaves = false;
            ClearDisplayedSaves();
            statusService.Report(Strings.Status_LoadLocalSavesFailed);
        }
        finally
        {
            IsLoadingSaves = false;
            loadTask = null;
            OnPropertyChanged(nameof(InstalledSummaryText));
            RaiseAvailabilityPropertyChanges();
            OnPropertyChanged(nameof(SaveEmptyMessage));
        }
    }

    private void RefreshSummary()
    {
        InstalledSaveCount = localSavesViewModel.CurrentSaves.Count;
    }

    private void LocalSavesViewModel_SavesChanged(object? sender, EventArgs e)
    {
        if (suppressLocalCollectionEvents)
            return;

        if (!isSectionActive)
        {
            hasPendingVisualRefresh = true;
            return;
        }

        QueueVisibleRefresh();
    }

    private void RefreshFromLocalSaves()
    {
        var selectedFullPath = selectionState.LastSingleSelectedPath ?? SelectedSave?.FullPath;
        var filteredSaves = StableFilteredItemProjection.Synchronize(
            localSavesViewModel.CurrentSaves,
            selectionState.ItemsByPath,
            save => save.FullPath,
            save => new SaveManagementSaveItemViewModel(save),
            static (item, save) => item.SyncFrom(save),
            MatchesSearch);

        selectionState.SyncSelectionToItems(filteredSaves, IsMultiSelectMode);
        SetVisibleSaves(filteredSaves);

        RefreshSummary();
        OnPropertyChanged(nameof(HasSaves));
        RaiseAvailabilityPropertyChanges();
        OnPropertyChanged(nameof(SaveEmptyMessage));
        OnPropertyChanged(nameof(AreAllVisibleSavesSelected));
        OnPropertyChanged(nameof(SelectAllButtonText));
        SelectAllSavesCommand.NotifyCanExecuteChanged();

        if (IsMultiSelectMode)
        {
            SelectedSave = null;
            UpdateSelectedSaveState();
            return;
        }

        var restoredSelection = Saves.FirstOrDefault(save =>
            string.Equals(save.FullPath, selectedFullPath, StringComparison.OrdinalIgnoreCase));
        SelectSave(restoredSelection ?? Saves.FirstOrDefault());
    }

    private void QueueVisibleRefresh(bool playEntranceAnimation = false)
    {
        if (isVisibleRefreshQueued)
            return;

        isVisibleRefreshQueued = true;
        uiDispatcher.Post(() =>
        {
            isVisibleRefreshQueued = false;
            if (!isSectionActive)
            {
                hasPendingVisualRefresh = true;
                return;
            }

            hasPendingVisualRefresh = false;
            RefreshFromLocalSaves();
            if (playEntranceAnimation && HasSaves)
                ListEntranceAnimationToken++;
        });
    }

    private bool MatchesSearch(LocalSave save)
    {
        if (string.IsNullOrWhiteSpace(SaveSearchQuery))
            return true;

        var query = SaveSearchQuery.Trim();
        return Contains(save.Name, query)
            || Contains(save.DirectoryName, query);
    }

    private static bool Contains(string? source, string query)
    {
        return !string.IsNullOrWhiteSpace(source)
            && source.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private bool ShouldResetForInstanceReference(GameInstance? instance)
    {
        if (selectedInstance is null || instance is null)
            return selectedInstance is not null || instance is not null;

        return !string.Equals(selectedInstance.Id, instance.Id, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(selectedInstance.InstanceDirectory, instance.InstanceDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private bool CanToggleSelectAllSaves()
    {
        return IsMultiSelectMode && HasSaves;
    }

    private void RaiseAvailabilityPropertyChanges()
    {
        OnPropertyChanged(nameof(CanShowSaveInfoSection));
        OnPropertyChanged(nameof(CanShowSaveScrollableContent));
        OnPropertyChanged(nameof(HasInstalledSaves));
        OnPropertyChanged(nameof(CanShowSaveListSection));
        OnPropertyChanged(nameof(CanShowNoSavesEmptyState));
        OnPropertyChanged(nameof(CanShowSaveEmptyState));
        OnPropertyChanged(nameof(CanShowSaveLoadingState));
    }

    private void EnterMultiSelectMode()
    {
        var selectedSave = SelectedSave;
        IsMultiSelectMode = true;
        SelectedSave = null;
        selectionState.BeginMultiSelect(selectedSave, Saves);
        UpdateSelectedSaveState();
    }

    private void ExitMultiSelectMode()
    {
        IsMultiSelectMode = false;
        selectionState.ClearVisibleSelections(Saves);
        selectionState.ClearSelectedPaths();
        UpdateSelectedSaveState();

        var restoredSelection = Saves.FirstOrDefault(save =>
            string.Equals(save.FullPath, selectionState.LastSingleSelectedPath, StringComparison.OrdinalIgnoreCase));
        SelectSave(restoredSelection ?? Saves.FirstOrDefault());
    }

    private void ResetSelectionState()
    {
        selectionState.Reset();
        IsMultiSelectMode = false;
        SelectedSave = null;
        SelectedSaveCount = 0;
    }

    private void ClearDisplayedSaves()
    {
        selectionState.ClearCache();
        SetVisibleSaves(Array.Empty<SaveManagementSaveItemViewModel>());
        RefreshVisibleSaveListItems();
        SelectedSave = null;
        RefreshSummary();
        OnPropertyChanged(nameof(HasSaves));
        RaiseAvailabilityPropertyChanges();
        OnPropertyChanged(nameof(SaveEmptyMessage));
        OnPropertyChanged(nameof(AreAllVisibleSavesSelected));
        OnPropertyChanged(nameof(SelectAllButtonText));
        SelectAllSavesCommand.NotifyCanExecuteChanged();
        UpdateSelectedSaveState();
    }

    private void ClearVisibleSelections()
    {
        selectionState.ClearVisibleSelections(Saves);
    }

    private IReadOnlyList<SaveManagementSaveItemViewModel> GetSelectedVisibleSaves()
    {
        return selectionState.GetSelectedVisibleItems(Saves);
    }

    private IReadOnlyList<LocalSave> ResolveLocalSaves(IEnumerable<string> fullPaths)
    {
        var pathSet = new HashSet<string>(fullPaths, StringComparer.OrdinalIgnoreCase);
        return localSavesViewModel.CurrentSaves
            .Where(save => pathSet.Contains(save.FullPath))
            .ToArray();
    }

    private void ReportBatchOperationResult(
        int totalCount,
        int failedCount,
        string successFormat,
        string partialFailureFormat)
    {
        if (failedCount <= 0)
        {
            statusService.Report(string.Format(successFormat, totalCount));
            return;
        }

        statusService.Report(string.Format(partialFailureFormat, totalCount - failedCount, failedCount));
    }

    private void UpdateSelectedSaveState()
    {
        SelectedSaveCount = selectionState.CountSelectedVisibleItems(Saves);
    }

    partial void OnVisibleSavesChanged(IReadOnlyList<SaveManagementSaveItemViewModel> value)
    {
        OnPropertyChanged(nameof(Saves));
        RefreshVisibleSaveListItems();
    }

    private void SetVisibleSaves(IReadOnlyList<SaveManagementSaveItemViewModel> saves)
    {
        if (IsSameVisibleSaves(saves))
            return;

        VisibleSaves = saves;
    }

    private bool IsSameVisibleSaves(IReadOnlyList<SaveManagementSaveItemViewModel> saves)
    {
        if (VisibleSaves.Count != saves.Count)
            return false;

        for (var index = 0; index < saves.Count; index++)
        {
            if (!ReferenceEquals(VisibleSaves[index], saves[index]))
                return false;
        }

        return true;
    }

    private void RefreshVisibleSaveListItems()
    {
        if (!CanShowSaveInfoSection)
        {
            if (VisibleSaveListItems.Count > 0)
                VisibleSaveListItems = Array.Empty<object>();
            return;
        }

        if (IsSameVisibleSaveListItems())
            return;

        var hasListSection = VisibleSaves.Count > 0;
        var items = new object[VisibleSaves.Count + (hasListSection ? 2 : 1)];
        items[0] = SaveManagementInfoPanelItem.Instance;
        if (hasListSection)
            items[1] = SaveManagementListSectionItem.Instance;

        for (var index = 0; index < VisibleSaves.Count; index++)
            items[index + (hasListSection ? 2 : 1)] = VisibleSaves[index];

        VisibleSaveListItems = items;
    }

    private bool IsSameVisibleSaveListItems()
    {
        var hasListSection = VisibleSaves.Count > 0;
        if (VisibleSaveListItems.Count != VisibleSaves.Count + (hasListSection ? 2 : 1))
            return false;

        if (!ReferenceEquals(VisibleSaveListItems[0], SaveManagementInfoPanelItem.Instance))
            return false;

        if (!hasListSection)
            return true;

        if (!ReferenceEquals(VisibleSaveListItems[1], SaveManagementListSectionItem.Instance))
            return false;

        for (var index = 0; index < VisibleSaves.Count; index++)
        {
            if (!ReferenceEquals(VisibleSaveListItems[index + 2], VisibleSaves[index]))
                return false;
        }

        return true;
    }

    private async Task ImportSaveArchivesAsync(IReadOnlyList<string> archivePaths, ImportTriggerSource source)
    {
        if (selectedInstance is null)
            return;

        if (!TryValidateImportPaths(archivePaths, Strings.GameSettings_DropSaveArchivesOnlyMessage, out var validationMessage))
        {
            if (source is ImportTriggerSource.DragDrop)
            {
                statusService.Report(validationMessage);
            }
            else
            {
                SaveImportFailedRequested?.Invoke(new SaveImportFailureRequest(Strings.Dialog_UnsupportedSaveArchiveMessage));
            }

            return;
        }

        logger.LogInformation(
            "Starting local save import batch. InstanceId={InstanceId} Source={Source} FileCount={FileCount}",
            selectedInstance.Id,
            source,
            archivePaths.Count);

        var successCount = 0;
        foreach (var archivePath in archivePaths)
        {
            logger.LogInformation(
                "Importing local save archive. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                selectedInstance.Id,
                archivePath);

            var result = await localSavesViewModel.ImportSaveFromArchiveAsync(archivePath, reportStatus: false);
            if (result.IsSuccess)
            {
                successCount++;
                continue;
            }

            switch (result.FailureReason)
            {
                case LocalSaveImportFailureReason.InvalidMinecraftSaveArchive:
                    SaveImportFailedRequested?.Invoke(new SaveImportFailureRequest(Strings.Dialog_InvalidSaveArchiveMessage));
                    break;
                case LocalSaveImportFailureReason.UnsupportedArchive:
                    SaveImportFailedRequested?.Invoke(new SaveImportFailureRequest(Strings.Dialog_UnsupportedSaveArchiveMessage));
                    break;
                case LocalSaveImportFailureReason.FileNotFound:
                    statusService.Report(Strings.Status_LocalSaveImportFileNotFound);
                    break;
                case LocalSaveImportFailureReason.UnexpectedError:
                    logger.LogWarning(
                        "Local save import failed unexpectedly after service call. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                        selectedInstance.Id,
                        archivePath);
                    statusService.Report(Strings.Status_LocalSaveImportFailed);
                    break;
            }

            return;
        }

        if (successCount > 0)
        {
            statusService.Report(successCount == 1
                ? Strings.Status_LocalSaveImported
                : string.Format(Strings.Status_LocalSavesImportedFormat, successCount));
        }
    }

    private bool TryValidateImportPaths(
        IReadOnlyList<string> paths,
        string invalidTypeMessage,
        out string failureMessage)
    {
        failureMessage = string.Empty;
        if (paths.Count == 0)
        {
            failureMessage = invalidTypeMessage;
            return false;
        }

        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                failureMessage = Strings.GameSettings_DropFoldersUnsupportedMessage;
                return false;
            }

            if (!File.Exists(path)
                || !SupportedSaveArchiveExtensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
            {
                failureMessage = invalidTypeMessage;
                return false;
            }
        }

        return true;
    }
}

public sealed class SaveManagementInfoPanelItem
{
    public static SaveManagementInfoPanelItem Instance { get; } = new();

    private SaveManagementInfoPanelItem()
    {
    }
}

public sealed class SaveManagementListSectionItem
{
    public static SaveManagementListSectionItem Instance { get; } = new();

    private SaveManagementListSectionItem()
    {
    }
}
