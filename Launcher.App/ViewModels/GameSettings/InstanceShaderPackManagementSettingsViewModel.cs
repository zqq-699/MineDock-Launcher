/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.ViewModels.Shared;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.IO;

namespace Launcher.App.ViewModels.GameSettings;

public sealed partial class InstanceShaderPackManagementSettingsViewModel : GameSettingsDetailsSectionViewModelBase
{
    private readonly LocalShaderPacksViewModel localShaderPacksViewModel;
    private readonly IStatusService statusService;
    private readonly IInstanceFolderService instanceFolderService;
    private readonly IFilePickerService filePickerService;
    private readonly IInstanceContentImportPathValidator importPathValidator;
    private readonly IUiDispatcher uiDispatcher;
    private readonly ILogger<InstanceShaderPackManagementSettingsViewModel> logger;
    private readonly LocalContentSelectionState<ShaderPackManagementItemViewModel> selectionState;
    private Task? loadTask;
    private GameInstance? selectedInstance;
    private bool hasPendingVisualRefresh;
    private bool isVisibleRefreshQueued;
    private bool isSectionActive;
    private bool suppressLocalCollectionEvents;

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

    [ObservableProperty]
    private bool isLoadingShaderPacks;

    [ObservableProperty]
    private bool hasLoadedShaderPacks;

    [ObservableProperty]
    private IReadOnlyList<ShaderPackManagementItemViewModel> visibleShaderPacks = Array.Empty<ShaderPackManagementItemViewModel>();

    [ObservableProperty]
    private IReadOnlyList<object> visibleShaderPackListItems = Array.Empty<object>();

    [ObservableProperty]
    private int listEntranceAnimationToken;

    public InstanceShaderPackManagementSettingsViewModel(
        GameSettingsDetailsViewModel parent,
        LocalShaderPacksViewModel localShaderPacksViewModel,
        IStatusService statusService,
        IInstanceFolderService instanceFolderService,
        IFilePickerService filePickerService,
        IInstanceContentImportPathValidator importPathValidator,
        IUiDispatcher? uiDispatcher = null,
        ILogger<InstanceShaderPackManagementSettingsViewModel>? logger = null)
        : base(parent)
    {
        this.localShaderPacksViewModel = localShaderPacksViewModel;
        this.statusService = statusService;
        this.instanceFolderService = instanceFolderService;
        this.filePickerService = filePickerService;
        this.importPathValidator = importPathValidator;
        this.uiDispatcher = uiDispatcher ?? ImmediateUiDispatcher.Instance;
        this.logger = logger ?? NullLogger<InstanceShaderPackManagementSettingsViewModel>.Instance;
        selectionState = new LocalContentSelectionState<ShaderPackManagementItemViewModel>(
            shaderPack => shaderPack.FullPath,
            shaderPack => shaderPack.IsSelected,
            static (shaderPack, isSelected) => shaderPack.IsSelected = isSelected);
        this.localShaderPacksViewModel.ShaderPacksChanged += LocalShaderPacksViewModel_ShaderPacksChanged;
    }

    public event Action<ShaderPackDeleteRequest>? DeleteShaderPacksRequested;
    public event Action<ShaderPackImportFailureRequest>? ShaderPackImportFailedRequested;

    public override bool UsesFullViewportLayout => true;

    public IReadOnlyList<ShaderPackManagementItemViewModel> ShaderPacks => VisibleShaderPacks;

    public bool CanShowShaderPackInfoSection => selectedInstance is not null;

    public bool HasShaderPacks => ShaderPacks.Count > 0;

    public bool CanShowShaderPackScrollableContent => selectedInstance is not null;

    public bool HasInstalledShaderPacks => InstalledShaderPackCount > 0;

    public bool CanShowShaderPackListSection => selectedInstance is not null && (IsLoadingShaderPacks || HasInstalledShaderPacks);

    public bool CanShowNoShaderPacksEmptyState => selectedInstance is not null && HasLoadedShaderPacks && !IsLoadingShaderPacks && !HasInstalledShaderPacks;

    public bool CanShowShaderPackEmptyState => selectedInstance is not null && HasLoadedShaderPacks && !IsLoadingShaderPacks && HasInstalledShaderPacks && !HasShaderPacks;

    public bool CanShowShaderPackLoadingState => selectedInstance is not null && IsLoadingShaderPacks && !HasLoadedShaderPacks;

    public bool HasSelectedShaderPacks => SelectedShaderPackCount > 0;

    public bool AreAllVisibleShaderPacksSelected => HasShaderPacks && SelectedShaderPackCount == ShaderPacks.Count;

    public bool CanImportLocalShaderPack => selectedInstance is not null;

    public string SelectAllButtonText => AreAllVisibleShaderPacksSelected
        ? Strings.GameSettings_ShaderPackManagementCancelSelectAllButton
        : Strings.GameSettings_ShaderPackManagementSelectAllButton;

    public string InstalledSummaryText => IsLoadingShaderPacks && !HasLoadedShaderPacks
        ? Strings.GameSettings_ShaderPackManagementLoading
        : string.Format(
            Strings.GameSettings_ShaderPackManagementInstalledSummaryFormat,
            InstalledShaderPackCount);

    public string ShaderPackEmptyMessage => !HasInstalledShaderPacks || string.IsNullOrWhiteSpace(ShaderPackSearchQuery)
        ? Strings.GameSettings_ShaderPackManagementEmptyMessage
        : Strings.GameSettings_ShaderPackManagementSearchEmptyMessage;

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
            localShaderPacksViewModel.SetSelectedInstance(instance);
            localShaderPacksViewModel.SetWatcherEnabled(isSectionActive && selectedInstance is not null);
        }
        finally
        {
            suppressLocalCollectionEvents = false;
        }

        IsLoadingShaderPacks = false;
        HasLoadedShaderPacks = false;
        selectionState.ClearCache();
        ListEntranceAnimationToken = 0;
        ResetSelectionState();
        ClearDisplayedShaderPacks();
        ImportLocalShaderPackCommand.NotifyCanExecuteChanged();
    }

    public bool RefreshSelectedInstanceReference(GameInstance? instance)
    {
        if (ShouldResetForInstanceReference(instance))
        {
            OnSelectedInstanceChanged(instance);
            return true;
        }

        selectedInstance = instance;
        ImportLocalShaderPackCommand.NotifyCanExecuteChanged();
        return false;
    }

    public override void OnSectionDeactivated()
    {
        isSectionActive = false;
        localShaderPacksViewModel.SetWatcherEnabled(false);
    }

    public void SuspendLocalWatchersForInstanceRename()
    {
        localShaderPacksViewModel.SuspendWatcherForInstanceRename();
    }

    public void ResumeLocalWatchersAfterInstanceRename()
    {
        localShaderPacksViewModel.ResumeWatcherAfterInstanceRename();
    }

    public override Task OnSectionActivatedAsync()
    {
        isSectionActive = true;
        localShaderPacksViewModel.SetWatcherEnabled(selectedInstance is not null);
        if (hasPendingVisualRefresh && HasLoadedShaderPacks)
            QueueVisibleRefresh(playEntranceAnimation: true);

        return EnsureLoadedForSelectedInstanceAsync();
    }

    public Task EnsureLoadedForSelectedInstanceAsync()
    {
        if (selectedInstance is null)
            return Task.CompletedTask;

        if (loadTask is { IsCompleted: false })
            return loadTask;

        if (HasLoadedShaderPacks)
            return Task.CompletedTask;

        loadTask = LoadShaderPacksAsync();
        return loadTask;
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
            selectionState.ClearVisibleSelections(ShaderPacks);
            selectionState.ClearSelectedPaths();
            SelectedShaderPack = null;
            UpdateSelectedShaderPackState();
            return;
        }

        selectionState.SelectAll(ShaderPacks);
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
                selectionState.ClearSelectedPaths();
            selectionState.ClearVisibleSelections(ShaderPacks);
            UpdateSelectedShaderPackState();
            return;
        }

        if (IsMultiSelectMode)
        {
            selectionState.ToggleSelection(shaderPack);
            SelectedShaderPack = null;
            UpdateSelectedShaderPackState();
            return;
        }

        SelectedShaderPack = shaderPack;
        selectionState.SelectSingle(shaderPack, ShaderPacks);
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

    private async Task LoadShaderPacksAsync()
    {
        if (selectedInstance is null)
            return;

        IsLoadingShaderPacks = true;
        OnPropertyChanged(nameof(InstalledSummaryText));
        RaiseAvailabilityPropertyChanges();

        try
        {
            await localShaderPacksViewModel.RefreshShaderPacksAsync();
            HasLoadedShaderPacks = true;
            if (isSectionActive)
                ListEntranceAnimationToken++;
            else
                hasPendingVisualRefresh = true;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to load shader packs for section activation. InstanceId={InstanceId}",
                selectedInstance.Id);
            HasLoadedShaderPacks = false;
            ClearDisplayedShaderPacks();
            statusService.Report(Strings.Status_LoadLocalShaderPacksFailed);
        }
        finally
        {
            IsLoadingShaderPacks = false;
            loadTask = null;
            OnPropertyChanged(nameof(InstalledSummaryText));
            RaiseAvailabilityPropertyChanges();
            OnPropertyChanged(nameof(ShaderPackEmptyMessage));
        }
    }

    private void RefreshSummary()
    {
        InstalledShaderPackCount = localShaderPacksViewModel.CurrentShaderPacks.Count;
    }

    private void LocalShaderPacksViewModel_ShaderPacksChanged(object? sender, EventArgs e)
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

    private void RefreshFromLocalShaderPacks()
    {
        var selectedFullPath = selectionState.LastSingleSelectedPath ?? SelectedShaderPack?.FullPath;
        var filteredShaderPacks = StableFilteredItemProjection.Synchronize(
            localShaderPacksViewModel.CurrentShaderPacks,
            selectionState.ItemsByPath,
            shaderPack => shaderPack.FullPath,
            shaderPack => new ShaderPackManagementItemViewModel(shaderPack),
            static (item, shaderPack) => item.SyncFrom(shaderPack),
            MatchesSearch);

        selectionState.SyncSelectionToItems(filteredShaderPacks, IsMultiSelectMode);
        SetVisibleShaderPacks(filteredShaderPacks);

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
            RefreshFromLocalShaderPacks();
            if (playEntranceAnimation && HasShaderPacks)
                ListEntranceAnimationToken++;
        });
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

    private bool ShouldResetForInstanceReference(GameInstance? instance)
    {
        if (selectedInstance is null || instance is null)
            return selectedInstance is not null || instance is not null;

        return !string.Equals(selectedInstance.Id, instance.Id, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(selectedInstance.InstanceDirectory, instance.InstanceDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private bool CanToggleSelectAllShaderPacks()
    {
        return IsMultiSelectMode && HasShaderPacks;
    }

    private void RaiseAvailabilityPropertyChanges()
    {
        OnPropertyChanged(nameof(CanShowShaderPackInfoSection));
        OnPropertyChanged(nameof(CanShowShaderPackScrollableContent));
        OnPropertyChanged(nameof(HasInstalledShaderPacks));
        OnPropertyChanged(nameof(CanShowShaderPackListSection));
        OnPropertyChanged(nameof(CanShowNoShaderPacksEmptyState));
        OnPropertyChanged(nameof(CanShowShaderPackEmptyState));
        OnPropertyChanged(nameof(CanShowShaderPackLoadingState));
    }

    private void EnterMultiSelectMode()
    {
        var selectedShaderPack = SelectedShaderPack;
        IsMultiSelectMode = true;
        SelectedShaderPack = null;
        selectionState.BeginMultiSelect(selectedShaderPack, ShaderPacks);
        UpdateSelectedShaderPackState();
    }

    private void ExitMultiSelectMode()
    {
        IsMultiSelectMode = false;
        selectionState.ClearVisibleSelections(ShaderPacks);
        selectionState.ClearSelectedPaths();
        UpdateSelectedShaderPackState();

        var restoredSelection = ShaderPacks.FirstOrDefault(shaderPack =>
            string.Equals(shaderPack.FullPath, selectionState.LastSingleSelectedPath, StringComparison.OrdinalIgnoreCase));
        SelectShaderPack(restoredSelection ?? ShaderPacks.FirstOrDefault());
    }

    private void ResetSelectionState()
    {
        selectionState.Reset();
        IsMultiSelectMode = false;
        SelectedShaderPack = null;
        SelectedShaderPackCount = 0;
    }

    private void ClearDisplayedShaderPacks()
    {
        selectionState.ClearCache();
        SetVisibleShaderPacks(Array.Empty<ShaderPackManagementItemViewModel>());
        RefreshVisibleShaderPackListItems();
        SelectedShaderPack = null;
        RefreshSummary();
        OnPropertyChanged(nameof(HasShaderPacks));
        RaiseAvailabilityPropertyChanges();
        OnPropertyChanged(nameof(ShaderPackEmptyMessage));
        OnPropertyChanged(nameof(AreAllVisibleShaderPacksSelected));
        OnPropertyChanged(nameof(SelectAllButtonText));
        SelectAllShaderPacksCommand.NotifyCanExecuteChanged();
        UpdateSelectedShaderPackState();
    }

    private void ClearVisibleSelections()
    {
        selectionState.ClearVisibleSelections(ShaderPacks);
    }

    private IReadOnlyList<ShaderPackManagementItemViewModel> GetSelectedVisibleShaderPacks()
    {
        return selectionState.GetSelectedVisibleItems(ShaderPacks);
    }

    private IReadOnlyList<LocalShaderPack> ResolveLocalShaderPacks(IEnumerable<string> fullPaths)
    {
        var pathSet = new HashSet<string>(fullPaths, StringComparer.OrdinalIgnoreCase);
        return localShaderPacksViewModel.CurrentShaderPacks
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
        SelectedShaderPackCount = selectionState.CountSelectedVisibleItems(ShaderPacks);
    }

    partial void OnVisibleShaderPacksChanged(IReadOnlyList<ShaderPackManagementItemViewModel> value)
    {
        OnPropertyChanged(nameof(ShaderPacks));
        RefreshVisibleShaderPackListItems();
    }

    private void SetVisibleShaderPacks(IReadOnlyList<ShaderPackManagementItemViewModel> shaderPacks)
    {
        if (LocalContentListPresentation.HasSameReferences(VisibleShaderPacks, shaderPacks))
            return;

        VisibleShaderPacks = shaderPacks;
    }

    private void RefreshVisibleShaderPackListItems()
    {
        var items = LocalContentListPresentation.CreateSectionedItems(
            VisibleShaderPacks,
            ShaderPackManagementInfoPanelItem.Instance,
            ShaderPackManagementListSectionItem.Instance,
            CanShowShaderPackInfoSection);
        if (!LocalContentListPresentation.HasSameReferences(VisibleShaderPackListItems, items))
            VisibleShaderPackListItems = items;
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
        return LocalContentImportPathEvaluator.TryValidate(
            importPathValidator,
            paths,
            InstanceContentImportKind.ShaderPack,
            invalidTypeMessage,
            out failureMessage);
    }
}

public sealed class ShaderPackManagementInfoPanelItem
{
    public static ShaderPackManagementInfoPanelItem Instance { get; } = new();

    private ShaderPackManagementInfoPanelItem()
    {
    }
}

public sealed class ShaderPackManagementListSectionItem
{
    public static ShaderPackManagementListSectionItem Instance { get; } = new();

    private ShaderPackManagementListSectionItem()
    {
    }
}
