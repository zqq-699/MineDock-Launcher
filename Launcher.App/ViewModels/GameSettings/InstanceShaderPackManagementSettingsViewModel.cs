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

public sealed partial class InstanceShaderPackManagementSettingsViewModel : GameSettingsDetailsSectionViewModelBase
{
    private readonly LocalShaderPacksViewModel localShaderPacksViewModel;
    private readonly IStatusService statusService;
    private readonly IInstanceFolderService instanceFolderService;
    private readonly IFilePickerService filePickerService;
    private readonly ILogger<InstanceShaderPackManagementSettingsViewModel> logger;
    private readonly HashSet<string> selectedShaderPackPaths = new(StringComparer.OrdinalIgnoreCase);
    private GameInstance? selectedInstance;
    private string? lastSingleSelectedShaderPackPath;

    [ObservableProperty]
    private int installedShaderPackCount;

    [ObservableProperty]
    private ShaderPackManagementItemViewModel? selectedShaderPack;

    [ObservableProperty]
    private string shaderPackSearchQuery = string.Empty;

    [ObservableProperty]
    private bool isMultiSelectMode;

    [ObservableProperty]
    private int selectedShaderPackCount;

    public InstanceShaderPackManagementSettingsViewModel(
        GameSettingsDetailsViewModel parent,
        LocalShaderPacksViewModel localShaderPacksViewModel,
        IStatusService statusService,
        IInstanceFolderService instanceFolderService,
        IFilePickerService filePickerService,
        ILogger<InstanceShaderPackManagementSettingsViewModel>? logger = null)
        : base(parent)
    {
        this.localShaderPacksViewModel = localShaderPacksViewModel;
        this.statusService = statusService;
        this.instanceFolderService = instanceFolderService;
        this.filePickerService = filePickerService;
        this.logger = logger ?? NullLogger<InstanceShaderPackManagementSettingsViewModel>.Instance;
        this.localShaderPacksViewModel.ShaderPacksChanged += LocalShaderPacksViewModel_ShaderPacksChanged;
    }

    public event Action<ShaderPackDeleteRequest>? DeleteShaderPacksRequested;

    public event Action<ShaderPackImportFailureRequest>? ShaderPackImportFailedRequested;

    public ObservableCollection<ShaderPackManagementItemViewModel> ShaderPacks { get; } = [];

    public bool CanShowShaderPackInfoSection => selectedInstance is not null;

    public bool HasShaderPacks => ShaderPacks.Count > 0;

    public bool HasInstalledShaderPacks => InstalledShaderPackCount > 0;

    public bool CanShowShaderPackListSection => selectedInstance is not null && HasInstalledShaderPacks;

    public bool CanShowNoShaderPacksEmptyState => selectedInstance is not null && !HasInstalledShaderPacks;

    public bool CanShowShaderPackEmptyState => selectedInstance is not null && HasInstalledShaderPacks && !HasShaderPacks;

    public bool HasSelectedShaderPacks => SelectedShaderPackCount > 0;

    public bool AreAllVisibleShaderPacksSelected => HasShaderPacks && SelectedShaderPackCount == ShaderPacks.Count;

    public bool CanImportLocalShaderPack => selectedInstance is not null;

    public string SelectAllButtonText => AreAllVisibleShaderPacksSelected
        ? Strings.GameSettings_ShaderPackManagementCancelSelectAllButton
        : Strings.GameSettings_ShaderPackManagementSelectAllButton;

    public string InstalledSummaryText => string.Format(
        Strings.GameSettings_ShaderPackManagementInstalledSummaryFormat,
        InstalledShaderPackCount);

    public string ShaderPackEmptyMessage => !HasInstalledShaderPacks || string.IsNullOrWhiteSpace(ShaderPackSearchQuery)
        ? Strings.GameSettings_ShaderPackManagementEmptyMessage
        : Strings.GameSettings_ShaderPackManagementSearchEmptyMessage;

    public async Task SetSelectedInstanceAsync(GameInstance? instance)
    {
        selectedInstance = instance;
        ResetSelectionState();
        RaiseAvailabilityPropertyChanges();
        ImportLocalShaderPackCommand.NotifyCanExecuteChanged();
        try
        {
            await localShaderPacksViewModel.SetSelectedInstanceAsync(instance);
            RefreshFromLocalShaderPacks();
        }
        catch (Exception)
        {
            ShaderPacks.Clear();
            SelectedShaderPack = null;
            RefreshSummary();
            OnPropertyChanged(nameof(HasShaderPacks));
            RaiseAvailabilityPropertyChanges();
            statusService.Report(Strings.Status_LoadLocalShaderPacksFailed);
        }
    }

    [RelayCommand]
    private void OpenShaderPackFolder()
    {
        if (selectedInstance is null)
            return;

        try
        {
            var shaderPacksDirectory = instanceFolderService.EnsureDirectoryExists(
                Path.Combine(selectedInstance.InstanceDirectory, "shaderpacks"));
            logger.LogInformation(
                "Opening shader pack folder. InstanceId={InstanceId} ShaderPacksDirectory={ShaderPacksDirectory}",
                selectedInstance.Id,
                shaderPacksDirectory);

            if (!instanceFolderService.TryOpen(shaderPacksDirectory))
            {
                logger.LogWarning(
                    "Failed to open shader pack folder. InstanceId={InstanceId} ShaderPacksDirectory={ShaderPacksDirectory}",
                    selectedInstance.Id,
                    shaderPacksDirectory);
                statusService.Report(Strings.Status_OpenLocalShaderPackFolderFailed);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to prepare shader pack folder for opening. InstanceId={InstanceId}",
                selectedInstance.Id);
            statusService.Report(Strings.Status_OpenLocalShaderPackFolderFailed);
        }
    }

    [RelayCommand(CanExecute = nameof(CanImportLocalShaderPack))]
    private async Task ImportLocalShaderPackAsync()
    {
        if (selectedInstance is null)
            return;

        var archivePath = filePickerService.PickShaderPackArchive();
        if (string.IsNullOrWhiteSpace(archivePath))
            return;

        await ImportShaderPackArchivesAsync([archivePath], ImportTriggerSource.FilePicker);
    }

    public GameSettingsFileDropEvaluation EvaluateDroppedFiles(IReadOnlyList<string> paths)
    {
        return TryValidateImportPaths(paths, Strings.GameSettings_DropShaderPackArchivesOnlyMessage, out var failureMessage)
            ? GameSettingsFileDropEvaluation.Accept(Strings.GameSettings_DropImportShaderPacksMessage)
            : GameSettingsFileDropEvaluation.Reject(failureMessage);
    }

    public Task ImportDroppedShaderPackArchivesAsync(IReadOnlyList<string> paths)
    {
        return ImportShaderPackArchivesAsync(paths, ImportTriggerSource.DragDrop);
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

    [RelayCommand(CanExecute = nameof(CanToggleSelectAllShaderPacks))]
    private void SelectAllShaderPacks()
    {
        if (AreAllVisibleShaderPacksSelected)
        {
            foreach (var shaderPack in ShaderPacks)
                shaderPack.IsSelected = false;

            selectedShaderPackPaths.Clear();
            SelectedShaderPack = null;
            UpdateSelectedShaderPackState();
            return;
        }

        foreach (var shaderPack in ShaderPacks)
        {
            shaderPack.IsSelected = true;
            selectedShaderPackPaths.Add(shaderPack.FullPath);
        }

        SelectedShaderPack = null;
        UpdateSelectedShaderPackState();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedShaderPacks))]
    private void RequestDeleteSelectedShaderPacks()
    {
        var selectedShaderPacks = GetSelectedVisibleShaderPacks();
        if (selectedShaderPacks.Count == 0)
            return;

        DeleteShaderPacksRequested?.Invoke(new ShaderPackDeleteRequest(
            selectedShaderPacks.Select(shaderPack => shaderPack.FullPath).ToArray(),
            selectedShaderPacks.Select(shaderPack => shaderPack.Title).ToArray()));
    }

    [RelayCommand]
    private void OpenShaderPackLocation(ShaderPackManagementItemViewModel? shaderPack)
    {
        if (shaderPack is null)
            return;

        try
        {
            if (!instanceFolderService.TryRevealFile(shaderPack.FullPath))
            {
                logger.LogWarning(
                    "Failed to reveal local shader pack file. InstanceId={InstanceId} Path={Path}",
                    selectedInstance?.Id ?? "<none>",
                    shaderPack.FullPath);
                statusService.Report(Strings.Status_OpenLocalShaderPackLocationFailed);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to reveal local shader pack file. InstanceId={InstanceId} Path={Path}",
                selectedInstance?.Id ?? "<none>",
                shaderPack.FullPath);
            statusService.Report(Strings.Status_OpenLocalShaderPackLocationFailed);
        }
    }

    [RelayCommand]
    private void RequestDeleteShaderPack(ShaderPackManagementItemViewModel? shaderPack)
    {
        if (shaderPack is null)
            return;

        DeleteShaderPacksRequested?.Invoke(new ShaderPackDeleteRequest(
            [shaderPack.FullPath],
            [shaderPack.Title]));
    }

    [RelayCommand]
    private void SelectShaderPack(ShaderPackManagementItemViewModel? shaderPack)
    {
        if (shaderPack is null)
        {
            SelectedShaderPack = null;
            if (IsMultiSelectMode)
                selectedShaderPackPaths.Clear();
            foreach (var item in ShaderPacks)
                item.IsSelected = false;
            UpdateSelectedShaderPackState();
            return;
        }

        if (IsMultiSelectMode)
        {
            var isSelected = !shaderPack.IsSelected;
            shaderPack.IsSelected = isSelected;
            if (isSelected)
                selectedShaderPackPaths.Add(shaderPack.FullPath);
            else
                selectedShaderPackPaths.Remove(shaderPack.FullPath);

            SelectedShaderPack = null;
            UpdateSelectedShaderPackState();
            return;
        }

        SelectedShaderPack = shaderPack;
        lastSingleSelectedShaderPackPath = shaderPack.FullPath;
        foreach (var item in ShaderPacks)
            item.IsSelected = false;
    }

    public async Task DeleteShaderPacksAsync(IReadOnlyList<string> fullPaths)
    {
        ArgumentNullException.ThrowIfNull(fullPaths);

        var shaderPacksToDelete = ResolveLocalShaderPacks(fullPaths);
        if (shaderPacksToDelete.Count == 0)
        {
            ExitMultiSelectMode();
            return;
        }

        logger.LogInformation(
            "Deleting selected shader packs. InstanceId={InstanceId} Count={Count}",
            selectedInstance?.Id ?? "<none>",
            shaderPacksToDelete.Count);
        try
        {
            var failedCount = await localShaderPacksViewModel.DeleteShaderPacksAsync(shaderPacksToDelete);
            ExitMultiSelectMode();
            ReportBatchOperationResult(
                shaderPacksToDelete.Count,
                failedCount,
                Strings.Status_SelectedShaderPacksDeletedFormat,
                Strings.Status_SelectedShaderPacksDeletePartialFailedFormat);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to delete selected shader packs. InstanceId={InstanceId}",
                selectedInstance?.Id ?? "<none>");
            statusService.Report(Strings.Status_SelectedShaderPacksDeleteFailed);
        }
    }

    partial void OnInstalledShaderPackCountChanged(int value)
    {
        OnPropertyChanged(nameof(InstalledSummaryText));
        RaiseAvailabilityPropertyChanges();
        OnPropertyChanged(nameof(ShaderPackEmptyMessage));
    }

    partial void OnShaderPackSearchQueryChanged(string value)
    {
        RefreshFromLocalShaderPacks();
        OnPropertyChanged(nameof(ShaderPackEmptyMessage));
    }

    partial void OnSelectedShaderPackCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasSelectedShaderPacks));
        OnPropertyChanged(nameof(AreAllVisibleShaderPacksSelected));
        OnPropertyChanged(nameof(SelectAllButtonText));
        SelectAllShaderPacksCommand.NotifyCanExecuteChanged();
        RequestDeleteSelectedShaderPacksCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsMultiSelectModeChanged(bool value)
    {
        OnPropertyChanged(nameof(AreAllVisibleShaderPacksSelected));
        OnPropertyChanged(nameof(SelectAllButtonText));
        SelectAllShaderPacksCommand.NotifyCanExecuteChanged();
    }

    private void RefreshSummary()
    {
        InstalledShaderPackCount = localShaderPacksViewModel.ShaderPacks.Count;
    }

    private void LocalShaderPacksViewModel_ShaderPacksChanged(object? sender, EventArgs e)
    {
        RefreshFromLocalShaderPacks();
    }

    private void RefreshFromLocalShaderPacks()
    {
        var selectedFullPath = lastSingleSelectedShaderPackPath ?? SelectedShaderPack?.FullPath;
        var filteredShaderPacks = localShaderPacksViewModel.ShaderPacks.Where(MatchesSearch).ToArray();

        if (IsMultiSelectMode)
            selectedShaderPackPaths.IntersectWith(filteredShaderPacks.Select(shaderPack => shaderPack.FullPath));

        ShaderPacks.Clear();
        foreach (var shaderPack in filteredShaderPacks)
        {
            var item = new ShaderPackManagementItemViewModel(shaderPack)
            {
                IsSelected = IsMultiSelectMode
                    && selectedShaderPackPaths.Contains(shaderPack.FullPath)
            };
            ShaderPacks.Add(item);
        }

        RefreshSummary();
        OnPropertyChanged(nameof(HasShaderPacks));
        RaiseAvailabilityPropertyChanges();
        OnPropertyChanged(nameof(ShaderPackEmptyMessage));
        OnPropertyChanged(nameof(AreAllVisibleShaderPacksSelected));
        OnPropertyChanged(nameof(SelectAllButtonText));
        SelectAllShaderPacksCommand.NotifyCanExecuteChanged();

        if (IsMultiSelectMode)
        {
            SelectedShaderPack = null;
            UpdateSelectedShaderPackState();
            return;
        }

        var restoredSelection = ShaderPacks.FirstOrDefault(shaderPack =>
            string.Equals(shaderPack.FullPath, selectedFullPath, StringComparison.OrdinalIgnoreCase));
        SelectShaderPack(restoredSelection ?? ShaderPacks.FirstOrDefault());
    }

    private bool MatchesSearch(LocalShaderPack shaderPack)
    {
        if (string.IsNullOrWhiteSpace(ShaderPackSearchQuery))
            return true;

        var query = ShaderPackSearchQuery.Trim();
        return Contains(shaderPack.Name, query)
            || Contains(shaderPack.FileName, query);
    }

    private static bool Contains(string? source, string query)
    {
        return !string.IsNullOrWhiteSpace(source)
            && source.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private bool CanToggleSelectAllShaderPacks()
    {
        return IsMultiSelectMode && HasShaderPacks;
    }

    private void RaiseAvailabilityPropertyChanges()
    {
        OnPropertyChanged(nameof(CanShowShaderPackInfoSection));
        OnPropertyChanged(nameof(HasInstalledShaderPacks));
        OnPropertyChanged(nameof(CanShowShaderPackListSection));
        OnPropertyChanged(nameof(CanShowNoShaderPacksEmptyState));
        OnPropertyChanged(nameof(CanShowShaderPackEmptyState));
    }

    private void EnterMultiSelectMode()
    {
        lastSingleSelectedShaderPackPath = SelectedShaderPack?.FullPath ?? lastSingleSelectedShaderPackPath;
        IsMultiSelectMode = true;
        SelectedShaderPack = null;
        selectedShaderPackPaths.Clear();
        ClearVisibleSelections();
        UpdateSelectedShaderPackState();
    }

    private void ExitMultiSelectMode()
    {
        IsMultiSelectMode = false;
        ClearVisibleSelections();
        selectedShaderPackPaths.Clear();
        UpdateSelectedShaderPackState();

        var restoredSelection = ShaderPacks.FirstOrDefault(shaderPack =>
            string.Equals(shaderPack.FullPath, lastSingleSelectedShaderPackPath, StringComparison.OrdinalIgnoreCase));
        SelectShaderPack(restoredSelection ?? ShaderPacks.FirstOrDefault());
    }

    private void ResetSelectionState()
    {
        lastSingleSelectedShaderPackPath = null;
        IsMultiSelectMode = false;
        SelectedShaderPack = null;
        selectedShaderPackPaths.Clear();
        SelectedShaderPackCount = 0;
    }

    private void ClearVisibleSelections()
    {
        foreach (var shaderPack in ShaderPacks)
            shaderPack.IsSelected = false;
    }

    private IReadOnlyList<ShaderPackManagementItemViewModel> GetSelectedVisibleShaderPacks()
    {
        return ShaderPacks.Where(shaderPack => selectedShaderPackPaths.Contains(shaderPack.FullPath)).ToArray();
    }

    private IReadOnlyList<LocalShaderPack> ResolveLocalShaderPacks(IEnumerable<string> fullPaths)
    {
        var pathSet = new HashSet<string>(fullPaths, StringComparer.OrdinalIgnoreCase);
        return localShaderPacksViewModel.ShaderPacks
            .Where(shaderPack => pathSet.Contains(shaderPack.FullPath))
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

    private void UpdateSelectedShaderPackState()
    {
        SelectedShaderPackCount = ShaderPacks.Count(shaderPack => shaderPack.IsSelected);
    }

    private async Task ImportShaderPackArchivesAsync(IReadOnlyList<string> archivePaths, ImportTriggerSource source)
    {
        if (selectedInstance is null)
            return;

        if (!TryValidateImportPaths(archivePaths, Strings.GameSettings_DropShaderPackArchivesOnlyMessage, out var validationMessage))
        {
            if (source is ImportTriggerSource.DragDrop)
            {
                statusService.Report(validationMessage);
            }
            else
            {
                ShaderPackImportFailedRequested?.Invoke(
                    new ShaderPackImportFailureRequest(Strings.Dialog_UnsupportedShaderPackArchiveMessage));
            }

            return;
        }

        logger.LogInformation(
            "Starting local shader pack import batch. InstanceId={InstanceId} Source={Source} FileCount={FileCount}",
            selectedInstance.Id,
            source,
            archivePaths.Count);

        var successCount = 0;
        foreach (var archivePath in archivePaths)
        {
            logger.LogInformation(
                "Importing local shader pack archive. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                selectedInstance.Id,
                archivePath);

            var result = await localShaderPacksViewModel.ImportShaderPackAsync(archivePath, reportStatus: false);
            if (result.IsSuccess)
            {
                successCount++;
                continue;
            }

            switch (result.FailureReason)
            {
                case LocalShaderPackImportFailureReason.UnsupportedArchive:
                    ShaderPackImportFailedRequested?.Invoke(
                        new ShaderPackImportFailureRequest(Strings.Dialog_UnsupportedShaderPackArchiveMessage));
                    break;
                case LocalShaderPackImportFailureReason.FileNotFound:
                    statusService.Report(Strings.Status_LocalShaderPackImportFileNotFound);
                    break;
                case LocalShaderPackImportFailureReason.UnexpectedError:
                    logger.LogWarning(
                        "Local shader pack import failed unexpectedly after service call. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                        selectedInstance.Id,
                        archivePath);
                    statusService.Report(Strings.Status_LocalShaderPackImportFailed);
                    break;
            }

            return;
        }

        if (successCount > 0)
        {
            statusService.Report(successCount == 1
                ? Strings.Status_LocalShaderPackImported
                : string.Format(Strings.Status_LocalShaderPacksImportedFormat, successCount));
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

            if (!File.Exists(path) || !path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                failureMessage = invalidTypeMessage;
                return false;
            }
        }

        return true;
    }
}
