using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.ObjectModel;
using System.IO;

namespace Launcher.App.ViewModels.GameSettings;

public sealed partial class InstanceSaveManagementSettingsViewModel : GameSettingsDetailsSectionViewModelBase
{
    private readonly LocalSavesViewModel localSavesViewModel;
    private readonly IStatusService statusService;
    private readonly IInstanceFolderService instanceFolderService;
    private readonly ILogger<InstanceSaveManagementSettingsViewModel> logger;
    private readonly HashSet<string> selectedSavePaths = new(StringComparer.OrdinalIgnoreCase);
    private GameInstance? selectedInstance;
    private string? lastSingleSelectedSavePath;

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

    public InstanceSaveManagementSettingsViewModel(
        GameSettingsDetailsViewModel parent,
        LocalSavesViewModel localSavesViewModel,
        IStatusService statusService,
        IInstanceFolderService instanceFolderService,
        ILogger<InstanceSaveManagementSettingsViewModel>? logger = null)
        : base(parent)
    {
        this.localSavesViewModel = localSavesViewModel;
        this.statusService = statusService;
        this.instanceFolderService = instanceFolderService;
        this.logger = logger ?? NullLogger<InstanceSaveManagementSettingsViewModel>.Instance;
        this.localSavesViewModel.SavesChanged += LocalSavesViewModel_SavesChanged;
    }

    public event Action<SaveDeleteRequest>? DeleteSavesRequested;

    public ObservableCollection<SaveManagementSaveItemViewModel> Saves { get; } = [];

    public bool CanShowSaveInfoSection => selectedInstance is not null;

    public bool HasSaves => Saves.Count > 0;

    public bool HasInstalledSaves => InstalledSaveCount > 0;

    public bool CanShowSaveListSection => selectedInstance is not null && HasInstalledSaves;

    public bool CanShowNoSavesEmptyState => selectedInstance is not null && !HasInstalledSaves;

    public bool CanShowSaveEmptyState => selectedInstance is not null && HasInstalledSaves && !HasSaves;

    public bool HasSelectedSaves => SelectedSaveCount > 0;

    public bool AreAllVisibleSavesSelected => HasSaves && SelectedSaveCount == Saves.Count;

    public bool CanImportLocalSave => false;

    public string SelectAllButtonText => AreAllVisibleSavesSelected
        ? Strings.GameSettings_SaveManagementCancelSelectAllButton
        : Strings.GameSettings_SaveManagementSelectAllButton;

    public string InstalledSummaryText => string.Format(
        Strings.GameSettings_SaveManagementInstalledSummaryFormat,
        InstalledSaveCount);

    public string SaveEmptyMessage => !HasInstalledSaves || string.IsNullOrWhiteSpace(SaveSearchQuery)
        ? Strings.GameSettings_SaveManagementEmptyMessage
        : Strings.GameSettings_SaveManagementSearchEmptyMessage;

    public async Task SetSelectedInstanceAsync(GameInstance? instance)
    {
        selectedInstance = instance;
        ResetSelectionState();
        RaiseAvailabilityPropertyChanges();
        try
        {
            await localSavesViewModel.SetSelectedInstanceAsync(instance);
            RefreshFromLocalSaves();
        }
        catch (Exception)
        {
            Saves.Clear();
            SelectedSave = null;
            RefreshSummary();
            OnPropertyChanged(nameof(HasSaves));
            RaiseAvailabilityPropertyChanges();
            statusService.Report(Strings.Status_LoadLocalSavesFailed);
        }
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
    private void ImportLocalSave()
    {
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
            foreach (var save in Saves)
                save.IsSelected = false;

            selectedSavePaths.Clear();
            SelectedSave = null;
            UpdateSelectedSaveState();
            return;
        }

        foreach (var save in Saves)
        {
            save.IsSelected = true;
            selectedSavePaths.Add(save.FullPath);
        }

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
                selectedSavePaths.Clear();
            foreach (var item in Saves)
                item.IsSelected = false;
            UpdateSelectedSaveState();
            return;
        }

        if (IsMultiSelectMode)
        {
            var isSelected = !save.IsSelected;
            save.IsSelected = isSelected;
            if (isSelected)
                selectedSavePaths.Add(save.FullPath);
            else
                selectedSavePaths.Remove(save.FullPath);

            SelectedSave = null;
            UpdateSelectedSaveState();
            return;
        }

        SelectedSave = save;
        lastSingleSelectedSavePath = save.FullPath;
        foreach (var item in Saves)
            item.IsSelected = false;
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

    private void RefreshSummary()
    {
        InstalledSaveCount = localSavesViewModel.Saves.Count;
    }

    private void LocalSavesViewModel_SavesChanged(object? sender, EventArgs e)
    {
        RefreshFromLocalSaves();
    }

    private void RefreshFromLocalSaves()
    {
        var selectedFullPath = lastSingleSelectedSavePath ?? SelectedSave?.FullPath;
        var filteredSaves = localSavesViewModel.Saves.Where(MatchesSearch).ToArray();

        if (IsMultiSelectMode)
            selectedSavePaths.IntersectWith(filteredSaves.Select(save => save.FullPath));

        Saves.Clear();
        foreach (var save in filteredSaves)
        {
            var item = new SaveManagementSaveItemViewModel(save)
            {
                IsSelected = IsMultiSelectMode
                    && selectedSavePaths.Contains(save.FullPath)
            };
            Saves.Add(item);
        }

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

    private bool CanToggleSelectAllSaves()
    {
        return IsMultiSelectMode && HasSaves;
    }

    private void RaiseAvailabilityPropertyChanges()
    {
        OnPropertyChanged(nameof(CanShowSaveInfoSection));
        OnPropertyChanged(nameof(HasInstalledSaves));
        OnPropertyChanged(nameof(CanShowSaveListSection));
        OnPropertyChanged(nameof(CanShowNoSavesEmptyState));
        OnPropertyChanged(nameof(CanShowSaveEmptyState));
    }

    private void EnterMultiSelectMode()
    {
        lastSingleSelectedSavePath = SelectedSave?.FullPath ?? lastSingleSelectedSavePath;
        IsMultiSelectMode = true;
        SelectedSave = null;
        selectedSavePaths.Clear();
        ClearVisibleSelections();
        UpdateSelectedSaveState();
    }

    private void ExitMultiSelectMode()
    {
        IsMultiSelectMode = false;
        ClearVisibleSelections();
        selectedSavePaths.Clear();
        UpdateSelectedSaveState();

        var restoredSelection = Saves.FirstOrDefault(save =>
            string.Equals(save.FullPath, lastSingleSelectedSavePath, StringComparison.OrdinalIgnoreCase));
        SelectSave(restoredSelection ?? Saves.FirstOrDefault());
    }

    private void ResetSelectionState()
    {
        lastSingleSelectedSavePath = null;
        IsMultiSelectMode = false;
        SelectedSave = null;
        selectedSavePaths.Clear();
        SelectedSaveCount = 0;
    }

    private void ClearVisibleSelections()
    {
        foreach (var save in Saves)
            save.IsSelected = false;
    }

    private IReadOnlyList<SaveManagementSaveItemViewModel> GetSelectedVisibleSaves()
    {
        return Saves.Where(save => selectedSavePaths.Contains(save.FullPath)).ToArray();
    }

    private IReadOnlyList<LocalSave> ResolveLocalSaves(IEnumerable<string> fullPaths)
    {
        var pathSet = new HashSet<string>(fullPaths, StringComparer.OrdinalIgnoreCase);
        return localSavesViewModel.Saves
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
        SelectedSaveCount = Saves.Count(save => save.IsSelected);
    }
}
