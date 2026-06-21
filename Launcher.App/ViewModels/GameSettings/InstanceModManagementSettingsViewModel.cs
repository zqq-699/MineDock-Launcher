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

public sealed partial class InstanceModManagementSettingsViewModel : GameSettingsDetailsSectionViewModelBase
{
    private readonly LocalModsViewModel localModsViewModel;
    private readonly IStatusService statusService;
    private readonly IInstanceFolderService instanceFolderService;
    private readonly IFilePickerService filePickerService;
    private readonly ILogger<InstanceModManagementSettingsViewModel> logger;
    private readonly HashSet<string> selectedModPaths = new(StringComparer.OrdinalIgnoreCase);
    private GameInstance? selectedInstance;
    private string? lastSingleSelectedModPath;

    [ObservableProperty]
    private int installedModCount;

    [ObservableProperty]
    private int enabledModCount;

    [ObservableProperty]
    private ModManagementModItemViewModel? selectedMod;

    [ObservableProperty]
    private string modSearchQuery = string.Empty;

    [ObservableProperty]
    private bool isMultiSelectMode;

    [ObservableProperty]
    private int selectedModCount;

    public InstanceModManagementSettingsViewModel(
        GameSettingsDetailsViewModel parent,
        LocalModsViewModel localModsViewModel,
        IStatusService statusService,
        IInstanceFolderService instanceFolderService,
        IFilePickerService filePickerService,
        ILogger<InstanceModManagementSettingsViewModel>? logger = null)
        : base(parent)
    {
        this.localModsViewModel = localModsViewModel;
        this.statusService = statusService;
        this.instanceFolderService = instanceFolderService;
        this.filePickerService = filePickerService;
        this.logger = logger ?? NullLogger<InstanceModManagementSettingsViewModel>.Instance;
        this.localModsViewModel.ModsChanged += LocalModsViewModel_ModsChanged;
    }

    public event Action<ModDeleteRequest>? DeleteModsRequested;
    public event Action<ModImportConflictRequest>? ImportModConflictRequested;

    public ObservableCollection<ModManagementModItemViewModel> Mods { get; } = [];

    public bool IsModManagementSupported => selectedInstance?.Loader is not LoaderKind.Vanilla;

    public bool CanShowModInfoSection => IsModManagementSupported;

    public bool HasMods => Mods.Count > 0;

    public bool HasInstalledMods => InstalledModCount > 0;

    public bool CanShowModListSection => IsModManagementSupported && HasInstalledMods;

    public bool CanShowNoModsEmptyState => IsModManagementSupported && !HasInstalledMods;

    public bool CanShowModEmptyState => IsModManagementSupported && HasInstalledMods && !HasMods;

    public bool CanShowModUnavailableState => !IsModManagementSupported;

    public bool HasSelectedMods => SelectedModCount > 0;

    public bool AreAllVisibleModsSelected => HasMods && SelectedModCount == Mods.Count;

    public string SelectAllButtonText => AreAllVisibleModsSelected
        ? Strings.GameSettings_ModManagementCancelSelectAllButton
        : Strings.GameSettings_ModManagementSelectAllButton;

    public string InstalledSummaryText => string.Format(
        Strings.GameSettings_ModManagementInstalledSummaryFormat,
        InstalledModCount,
        EnabledModCount);

    public string ModEmptyMessage => !HasInstalledMods || string.IsNullOrWhiteSpace(ModSearchQuery)
        ? Strings.GameSettings_ModManagementEmptyMessage
        : Strings.GameSettings_ModManagementSearchEmptyMessage;

    public string ModUnavailableMessage => Strings.GameSettings_ModManagementUnavailableMessage;

    public async Task SetSelectedInstanceAsync(GameInstance? instance)
    {
        selectedInstance = instance;
        ResetSelectionState();
        RaiseAvailabilityPropertyChanges();
        try
        {
            await localModsViewModel.SetSelectedInstanceAsync(instance);
            RefreshFromLocalMods();
        }
        catch (Exception)
        {
            Mods.Clear();
            SelectedMod = null;
            RefreshSummary();
            OnPropertyChanged(nameof(HasMods));
            RaiseAvailabilityPropertyChanges();
            statusService.Report(Strings.Status_LoadLocalModsFailed);
        }
    }

    [RelayCommand]
    private void OpenModFolder()
    {
        if (selectedInstance is null)
            return;

        try
        {
            var modsDirectory = instanceFolderService.EnsureDirectoryExists(
                Path.Combine(selectedInstance.InstanceDirectory, "mods"));
            logger.LogInformation(
                "Opening mod folder. InstanceId={InstanceId} ModsDirectory={ModsDirectory}",
                selectedInstance.Id,
                modsDirectory);

            if (!instanceFolderService.TryOpen(modsDirectory))
            {
                logger.LogWarning(
                    "Failed to open mod folder. InstanceId={InstanceId} ModsDirectory={ModsDirectory}",
                    selectedInstance.Id,
                    modsDirectory);
                statusService.Report(Strings.Status_OpenInstanceFolderFailed);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to prepare mod folder for opening. InstanceId={InstanceId}",
                selectedInstance.Id);
            statusService.Report(Strings.Status_OpenInstanceFolderFailed);
        }
    }

    [RelayCommand]
    private async Task ImportLocalModAsync()
    {
        if (selectedInstance is null)
            return;

        var modPath = filePickerService.PickModFile();
        if (string.IsNullOrWhiteSpace(modPath))
            return;

        var fileName = Path.GetFileName(modPath);
        if (localModsViewModel.Mods.Any(mod => string.Equals(mod.FileName, fileName, StringComparison.OrdinalIgnoreCase)))
        {
            ImportModConflictRequested?.Invoke(new ModImportConflictRequest(modPath, fileName));
            return;
        }

        await ImportLocalModCoreAsync(modPath, overwriteExisting: false);
    }

    public Task ReplaceImportedModAsync(string sourcePath)
    {
        return ImportLocalModCoreAsync(sourcePath, overwriteExisting: true);
    }

    private async Task ImportLocalModCoreAsync(string modPath, bool overwriteExisting)
    {
        if (selectedInstance is null || string.IsNullOrWhiteSpace(modPath))
            return;

        logger.LogInformation(
            "Importing local mod from file picker. InstanceId={InstanceId} SourcePath={SourcePath} OverwriteExisting={OverwriteExisting}",
            selectedInstance.Id,
            modPath,
            overwriteExisting);

        try
        {
            await localModsViewModel.ImportModFromPathAsync(modPath, overwriteExisting);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to import local mod. InstanceId={InstanceId} SourcePath={SourcePath} OverwriteExisting={OverwriteExisting}",
                selectedInstance.Id,
                modPath,
                overwriteExisting);
            statusService.Report(Strings.Status_LocalModImportFailed);
        }
    }

    [RelayCommand]
    private void InstallOnlineMod()
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

    [RelayCommand(CanExecute = nameof(CanToggleSelectAllMods))]
    private void SelectAllMods()
    {
        if (AreAllVisibleModsSelected)
        {
            foreach (var mod in Mods)
                mod.IsSelected = false;

            selectedModPaths.Clear();
            SelectedMod = null;
            UpdateSelectedModState();
            return;
        }

        foreach (var mod in Mods)
        {
            mod.IsSelected = true;
            selectedModPaths.Add(mod.FullPath);
        }

        SelectedMod = null;
        UpdateSelectedModState();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedMods))]
    private async Task EnableSelectedModsAsync()
    {
        await SetSelectedModsEnabledAsync(enabled: true);
    }

    [RelayCommand(CanExecute = nameof(HasSelectedMods))]
    private async Task DisableSelectedModsAsync()
    {
        await SetSelectedModsEnabledAsync(enabled: false);
    }

    [RelayCommand(CanExecute = nameof(HasSelectedMods))]
    private void RequestDeleteSelectedMods()
    {
        var selectedMods = GetSelectedVisibleMods();
        if (selectedMods.Count == 0)
            return;

        DeleteModsRequested?.Invoke(new ModDeleteRequest(
            selectedMods.Select(mod => mod.FullPath).ToArray(),
            selectedMods.Select(mod => mod.Title).ToArray()));
    }

    [RelayCommand]
    private async Task ToggleModEnabledAsync(ModManagementModItemViewModel? mod)
    {
        if (mod is null)
            return;

        var localMod = ResolveLocalMod(mod.FullPath);
        if (localMod is null)
            return;

        var nextPath = GetPathForEnabledState(localMod.FullPath, !localMod.IsEnabled);
        var previousSelectedPath = lastSingleSelectedModPath;
        var wasSelectedInMultiSelect = selectedModPaths.Contains(localMod.FullPath);

        if (IsMultiSelectMode)
        {
            selectedModPaths.Remove(localMod.FullPath);
            if (wasSelectedInMultiSelect)
                selectedModPaths.Add(nextPath);
        }
        else
        {
            lastSingleSelectedModPath = nextPath;
        }

        logger.LogInformation(
            "Toggling local mod enabled state. InstanceId={InstanceId} Path={Path} Enabled={Enabled}",
            selectedInstance?.Id ?? "<none>",
            localMod.FullPath,
            !localMod.IsEnabled);

        try
        {
            await localModsViewModel.ToggleModAsync(localMod);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to toggle local mod enabled state. InstanceId={InstanceId} Path={Path}",
                selectedInstance?.Id ?? "<none>",
                localMod.FullPath);
            statusService.Report(localMod.IsEnabled
                ? Strings.Status_SelectedModsDisableFailed
                : Strings.Status_SelectedModsEnableFailed);

            if (IsMultiSelectMode)
            {
                selectedModPaths.Remove(nextPath);
                if (wasSelectedInMultiSelect)
                    selectedModPaths.Add(localMod.FullPath);
            }
            else
            {
                lastSingleSelectedModPath = previousSelectedPath;
            }

            RefreshFromLocalMods();
        }
    }

    [RelayCommand]
    private void OpenModFileLocation(ModManagementModItemViewModel? mod)
    {
        if (mod is null)
            return;

        try
        {
            if (!instanceFolderService.TryRevealFile(mod.FullPath))
            {
                logger.LogWarning(
                    "Failed to reveal local mod file. InstanceId={InstanceId} Path={Path}",
                    selectedInstance?.Id ?? "<none>",
                    mod.FullPath);
                statusService.Report(Strings.Status_OpenModFileLocationFailed);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to reveal local mod file. InstanceId={InstanceId} Path={Path}",
                selectedInstance?.Id ?? "<none>",
                mod.FullPath);
            statusService.Report(Strings.Status_OpenModFileLocationFailed);
        }
    }

    [RelayCommand]
    private void RequestDeleteMod(ModManagementModItemViewModel? mod)
    {
        if (mod is null)
            return;

        DeleteModsRequested?.Invoke(new ModDeleteRequest(
            [mod.FullPath],
            [mod.Title]));
    }

    [RelayCommand]
    private void SelectMod(ModManagementModItemViewModel? mod)
    {
        if (mod is null)
        {
            SelectedMod = null;
            if (IsMultiSelectMode)
                selectedModPaths.Clear();
            foreach (var item in Mods)
                item.IsSelected = false;
            UpdateSelectedModState();
            return;
        }

        if (IsMultiSelectMode)
        {
            var isSelected = !mod.IsSelected;
            mod.IsSelected = isSelected;
            if (isSelected)
                selectedModPaths.Add(mod.FullPath);
            else
                selectedModPaths.Remove(mod.FullPath);

            SelectedMod = null;
            UpdateSelectedModState();
            return;
        }

        SelectedMod = mod;
        lastSingleSelectedModPath = mod.FullPath;
        foreach (var item in Mods)
            item.IsSelected = ReferenceEquals(item, mod);
    }

    public async Task DeleteModsAsync(IReadOnlyList<string> fullPaths)
    {
        ArgumentNullException.ThrowIfNull(fullPaths);

        var modsToDelete = ResolveLocalMods(fullPaths);
        if (modsToDelete.Count == 0)
        {
            ExitMultiSelectMode();
            return;
        }

        logger.LogInformation(
            "Deleting selected mods. InstanceId={InstanceId} Count={Count}",
            selectedInstance?.Id ?? "<none>",
            modsToDelete.Count);
        try
        {
            var failedCount = await localModsViewModel.DeleteModsAsync(modsToDelete);
            ExitMultiSelectMode();
            ReportBatchOperationResult(
                modsToDelete.Count,
                failedCount,
                Strings.Status_SelectedModsDeletedFormat,
                Strings.Status_SelectedModsDeletePartialFailedFormat);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to delete selected mods. InstanceId={InstanceId}",
                selectedInstance?.Id ?? "<none>");
            statusService.Report(Strings.Status_SelectedModsDeleteFailed);
        }
    }

    partial void OnInstalledModCountChanged(int value)
    {
        OnPropertyChanged(nameof(InstalledSummaryText));
        RaiseAvailabilityPropertyChanges();
        OnPropertyChanged(nameof(ModEmptyMessage));
    }

    partial void OnEnabledModCountChanged(int value)
    {
        OnPropertyChanged(nameof(InstalledSummaryText));
    }

    partial void OnModSearchQueryChanged(string value)
    {
        RefreshFromLocalMods();
        OnPropertyChanged(nameof(ModEmptyMessage));
    }

    partial void OnSelectedModCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasSelectedMods));
        OnPropertyChanged(nameof(AreAllVisibleModsSelected));
        OnPropertyChanged(nameof(SelectAllButtonText));
        SelectAllModsCommand.NotifyCanExecuteChanged();
        EnableSelectedModsCommand.NotifyCanExecuteChanged();
        DisableSelectedModsCommand.NotifyCanExecuteChanged();
        RequestDeleteSelectedModsCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsMultiSelectModeChanged(bool value)
    {
        OnPropertyChanged(nameof(AreAllVisibleModsSelected));
        OnPropertyChanged(nameof(SelectAllButtonText));
        SelectAllModsCommand.NotifyCanExecuteChanged();
    }

    private void RefreshSummary()
    {
        InstalledModCount = localModsViewModel.Mods.Count;
        EnabledModCount = localModsViewModel.Mods.Count(mod => mod.IsEnabled);
    }

    private void LocalModsViewModel_ModsChanged(object? sender, EventArgs e)
    {
        RefreshFromLocalMods();
    }

    private void RefreshFromLocalMods()
    {
        var selectedFullPath = lastSingleSelectedModPath ?? SelectedMod?.FullPath;
        var filteredMods = localModsViewModel.Mods.Where(MatchesSearch).ToArray();

        if (IsMultiSelectMode)
            selectedModPaths.IntersectWith(filteredMods.Select(mod => mod.FullPath));

        Mods.Clear();
        foreach (var mod in filteredMods)
        {
            var item = new ModManagementModItemViewModel(mod)
            {
                IsSelected = IsMultiSelectMode
                    && selectedModPaths.Contains(mod.FullPath)
            };
            Mods.Add(item);
        }

        RefreshSummary();
        OnPropertyChanged(nameof(HasMods));
        RaiseAvailabilityPropertyChanges();
        OnPropertyChanged(nameof(ModEmptyMessage));
        OnPropertyChanged(nameof(AreAllVisibleModsSelected));
        OnPropertyChanged(nameof(SelectAllButtonText));
        SelectAllModsCommand.NotifyCanExecuteChanged();

        if (IsMultiSelectMode)
        {
            SelectedMod = null;
            UpdateSelectedModState();
            return;
        }

        var restoredSelection = Mods.FirstOrDefault(mod =>
            string.Equals(mod.FullPath, selectedFullPath, StringComparison.OrdinalIgnoreCase));
        SelectMod(restoredSelection ?? Mods.FirstOrDefault());
    }

    private bool MatchesSearch(LocalMod mod)
    {
        if (string.IsNullOrWhiteSpace(ModSearchQuery))
            return true;

        var query = ModSearchQuery.Trim();
        return Contains(mod.Name, query)
            || Contains(mod.Loader, query)
            || Contains(mod.ModId, query)
            || Contains(mod.Version, query)
            || Contains(mod.FileName, query);
    }

    private static bool Contains(string? source, string query)
    {
        return !string.IsNullOrWhiteSpace(source)
            && source.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private bool CanToggleSelectAllMods()
    {
        return IsMultiSelectMode && HasMods;
    }

    private void RaiseAvailabilityPropertyChanges()
    {
        OnPropertyChanged(nameof(IsModManagementSupported));
        OnPropertyChanged(nameof(CanShowModInfoSection));
        OnPropertyChanged(nameof(HasInstalledMods));
        OnPropertyChanged(nameof(CanShowModListSection));
        OnPropertyChanged(nameof(CanShowNoModsEmptyState));
        OnPropertyChanged(nameof(CanShowModEmptyState));
        OnPropertyChanged(nameof(CanShowModUnavailableState));
        OnPropertyChanged(nameof(ModUnavailableMessage));
    }

    private void EnterMultiSelectMode()
    {
        lastSingleSelectedModPath = SelectedMod?.FullPath ?? lastSingleSelectedModPath;
        IsMultiSelectMode = true;
        SelectedMod = null;
        selectedModPaths.Clear();
        ClearVisibleSelections();
        UpdateSelectedModState();
    }

    private void ExitMultiSelectMode()
    {
        IsMultiSelectMode = false;
        ClearVisibleSelections();
        selectedModPaths.Clear();
        UpdateSelectedModState();

        var restoredSelection = Mods.FirstOrDefault(mod =>
            string.Equals(mod.FullPath, lastSingleSelectedModPath, StringComparison.OrdinalIgnoreCase));
        SelectMod(restoredSelection ?? Mods.FirstOrDefault());
    }

    private void ResetSelectionState()
    {
        lastSingleSelectedModPath = null;
        IsMultiSelectMode = false;
        SelectedMod = null;
        selectedModPaths.Clear();
        SelectedModCount = 0;
    }

    private void ClearVisibleSelections()
    {
        foreach (var mod in Mods)
            mod.IsSelected = false;
    }

    private IReadOnlyList<ModManagementModItemViewModel> GetSelectedVisibleMods()
    {
        return Mods.Where(mod => selectedModPaths.Contains(mod.FullPath)).ToArray();
    }

    private IReadOnlyList<LocalMod> ResolveLocalMods(IEnumerable<string> fullPaths)
    {
        var pathSet = new HashSet<string>(fullPaths, StringComparer.OrdinalIgnoreCase);
        return localModsViewModel.Mods
            .Where(mod => pathSet.Contains(mod.FullPath))
            .ToArray();
    }

    private LocalMod? ResolveLocalMod(string fullPath)
    {
        return localModsViewModel.Mods.FirstOrDefault(mod =>
            string.Equals(mod.FullPath, fullPath, StringComparison.OrdinalIgnoreCase));
    }

    private async Task SetSelectedModsEnabledAsync(bool enabled)
    {
        var selectedMods = ResolveLocalMods(selectedModPaths);
        if (selectedMods.Count == 0)
        {
            UpdateSelectedModState();
            return;
        }

        var nextSelectedPaths = selectedMods
            .Select(mod => GetPathForEnabledState(mod.FullPath, enabled))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        logger.LogInformation(
            "Changing selected mods enabled state. InstanceId={InstanceId} Count={Count} Enabled={Enabled}",
            selectedInstance?.Id ?? "<none>",
            selectedMods.Count,
            enabled);
        try
        {
            var failedCount = await localModsViewModel.SetModsEnabledAsync(selectedMods, enabled);
            selectedModPaths.Clear();
            selectedModPaths.UnionWith(nextSelectedPaths);
            if (failedCount > 0)
                selectedModPaths.UnionWith(selectedMods.Select(mod => mod.FullPath));

            RefreshFromLocalMods();
            ReportBatchOperationResult(
                selectedMods.Count,
                failedCount,
                enabled
                    ? Strings.Status_SelectedModsEnabledFormat
                    : Strings.Status_SelectedModsDisabledFormat,
                enabled
                    ? Strings.Status_SelectedModsEnablePartialFailedFormat
                    : Strings.Status_SelectedModsDisablePartialFailedFormat);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to change selected mods enabled state. InstanceId={InstanceId} Enabled={Enabled}",
                selectedInstance?.Id ?? "<none>",
                enabled);
            statusService.Report(enabled
                ? Strings.Status_SelectedModsEnableFailed
                : Strings.Status_SelectedModsDisableFailed);
            RefreshFromLocalMods();
        }
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

    private void UpdateSelectedModState()
    {
        SelectedModCount = Mods.Count(mod => mod.IsSelected);
    }

    private static string GetPathForEnabledState(string path, bool enabled)
    {
        return enabled
            ? path.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)
                ? path[..^".disabled".Length]
                : path
            : path.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)
                ? path
                : path + ".disabled";
    }
}
