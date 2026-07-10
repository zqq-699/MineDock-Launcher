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
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.IO;

namespace Launcher.App.ViewModels.GameSettings;

public sealed partial class InstanceResourcePackManagementSettingsViewModel : GameSettingsDetailsSectionViewModelBase
{
    private readonly LocalResourcePacksViewModel localResourcePacksViewModel;
    private readonly IStatusService statusService;
    private readonly IInstanceFolderService instanceFolderService;
    private readonly IFilePickerService filePickerService;
    private readonly IUiDispatcher uiDispatcher;
    private readonly ILogger<InstanceResourcePackManagementSettingsViewModel> logger;
    private readonly LocalContentSelectionState<ResourcePackManagementItemViewModel> selectionState;
    private Task? loadTask;
    private GameInstance? selectedInstance;
    private bool hasPendingVisualRefresh;
    private bool isVisibleRefreshQueued;
    private bool isSectionActive;
    private bool suppressLocalCollectionEvents;

    [ObservableProperty]
    private int installedResourcePackCount;

    [ObservableProperty]
    private ResourcePackManagementItemViewModel? selectedResourcePack;

    [ObservableProperty]
    private string resourcePackSearchQuery = string.Empty;

    [ObservableProperty]
    private bool isMultiSelectMode;

    [ObservableProperty]
    private int selectedResourcePackCount;

    [ObservableProperty]
    private bool isLoadingResourcePacks;

    [ObservableProperty]
    private bool hasLoadedResourcePacks;

    [ObservableProperty]
    private IReadOnlyList<ResourcePackManagementItemViewModel> visibleResourcePacks = Array.Empty<ResourcePackManagementItemViewModel>();

    [ObservableProperty]
    private IReadOnlyList<object> visibleResourcePackListItems = Array.Empty<object>();

    [ObservableProperty]
    private int listEntranceAnimationToken;

    public InstanceResourcePackManagementSettingsViewModel(
        GameSettingsDetailsViewModel parent,
        LocalResourcePacksViewModel localResourcePacksViewModel,
        IStatusService statusService,
        IInstanceFolderService instanceFolderService,
        IFilePickerService filePickerService,
        IUiDispatcher? uiDispatcher = null,
        ILogger<InstanceResourcePackManagementSettingsViewModel>? logger = null)
        : base(parent)
    {
        this.localResourcePacksViewModel = localResourcePacksViewModel;
        this.statusService = statusService;
        this.instanceFolderService = instanceFolderService;
        this.filePickerService = filePickerService;
        this.uiDispatcher = uiDispatcher ?? ImmediateUiDispatcher.Instance;
        this.logger = logger ?? NullLogger<InstanceResourcePackManagementSettingsViewModel>.Instance;
        selectionState = new LocalContentSelectionState<ResourcePackManagementItemViewModel>(
            resourcePack => resourcePack.FullPath,
            resourcePack => resourcePack.IsSelected,
            static (resourcePack, isSelected) => resourcePack.IsSelected = isSelected);
        this.localResourcePacksViewModel.ResourcePacksChanged += LocalResourcePacksViewModel_ResourcePacksChanged;
    }

    public event Action<ResourcePackDeleteRequest>? DeleteResourcePacksRequested;
    public event Action<ResourcePackImportFailureRequest>? ResourcePackImportFailedRequested;

    public override bool UsesFullViewportLayout => true;

    public IReadOnlyList<ResourcePackManagementItemViewModel> ResourcePacks => VisibleResourcePacks;

    public bool CanShowResourcePackInfoSection => selectedInstance is not null;

    public bool HasResourcePacks => ResourcePacks.Count > 0;

    public bool CanShowResourcePackScrollableContent => selectedInstance is not null;

    public bool HasInstalledResourcePacks => InstalledResourcePackCount > 0;

    public bool CanShowResourcePackListSection => selectedInstance is not null && (IsLoadingResourcePacks || HasInstalledResourcePacks);

    public bool CanShowNoResourcePacksEmptyState => selectedInstance is not null && HasLoadedResourcePacks && !IsLoadingResourcePacks && !HasInstalledResourcePacks;

    public bool CanShowResourcePackEmptyState => selectedInstance is not null && HasLoadedResourcePacks && !IsLoadingResourcePacks && HasInstalledResourcePacks && !HasResourcePacks;

    public bool CanShowResourcePackLoadingState => selectedInstance is not null && IsLoadingResourcePacks && !HasLoadedResourcePacks;

    public bool HasSelectedResourcePacks => SelectedResourcePackCount > 0;

    public bool AreAllVisibleResourcePacksSelected => HasResourcePacks && SelectedResourcePackCount == ResourcePacks.Count;

    public bool CanImportLocalResourcePack => selectedInstance is not null;

    public string SelectAllButtonText => AreAllVisibleResourcePacksSelected
        ? Strings.GameSettings_ResourcePackManagementCancelSelectAllButton
        : Strings.GameSettings_ResourcePackManagementSelectAllButton;

    public string InstalledSummaryText => IsLoadingResourcePacks && !HasLoadedResourcePacks
        ? Strings.GameSettings_ResourcePackManagementLoading
        : string.Format(
            Strings.GameSettings_ResourcePackManagementInstalledSummaryFormat,
            InstalledResourcePackCount);

    public string ResourcePackEmptyMessage => !HasInstalledResourcePacks || string.IsNullOrWhiteSpace(ResourcePackSearchQuery)
        ? Strings.GameSettings_ResourcePackManagementEmptyMessage
        : Strings.GameSettings_ResourcePackManagementSearchEmptyMessage;

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
            localResourcePacksViewModel.SetSelectedInstance(instance);
            localResourcePacksViewModel.SetWatcherEnabled(isSectionActive && selectedInstance is not null);
        }
        finally
        {
            suppressLocalCollectionEvents = false;
        }

        IsLoadingResourcePacks = false;
        HasLoadedResourcePacks = false;
        selectionState.ClearCache();
        ListEntranceAnimationToken = 0;
        ResetSelectionState();
        ClearDisplayedResourcePacks();
        ImportLocalResourcePackCommand.NotifyCanExecuteChanged();
    }

    public bool RefreshSelectedInstanceReference(GameInstance? instance)
    {
        if (ShouldResetForInstanceReference(instance))
        {
            OnSelectedInstanceChanged(instance);
            return true;
        }

        selectedInstance = instance;
        ImportLocalResourcePackCommand.NotifyCanExecuteChanged();
        return false;
    }

    public override void OnSectionDeactivated()
    {
        isSectionActive = false;
        localResourcePacksViewModel.SetWatcherEnabled(false);
    }

    public void SuspendLocalWatchersForInstanceRename()
    {
        localResourcePacksViewModel.SuspendWatcherForInstanceRename();
    }

    public void ResumeLocalWatchersAfterInstanceRename()
    {
        localResourcePacksViewModel.ResumeWatcherAfterInstanceRename();
    }

    public override Task OnSectionActivatedAsync()
    {
        isSectionActive = true;
        localResourcePacksViewModel.SetWatcherEnabled(selectedInstance is not null);
        if (hasPendingVisualRefresh && HasLoadedResourcePacks)
            QueueVisibleRefresh(playEntranceAnimation: true);

        return EnsureLoadedForSelectedInstanceAsync();
    }

    public Task EnsureLoadedForSelectedInstanceAsync()
    {
        if (selectedInstance is null)
            return Task.CompletedTask;

        if (loadTask is { IsCompleted: false })
            return loadTask;

        if (HasLoadedResourcePacks)
            return Task.CompletedTask;

        loadTask = LoadResourcePacksAsync();
        return loadTask;
    }

    [RelayCommand]
    private void OpenResourcePackFolder()
    {
        if (selectedInstance is null)
            return;

        try
        {
            var resourcePacksDirectory = instanceFolderService.EnsureDirectoryExists(
                Path.Combine(selectedInstance.InstanceDirectory, "resourcepacks"));
            logger.LogInformation(
                "Opening resource pack folder. InstanceId={InstanceId} ResourcePacksDirectory={ResourcePacksDirectory}",
                selectedInstance.Id,
                resourcePacksDirectory);

            if (!instanceFolderService.TryOpen(resourcePacksDirectory))
            {
                logger.LogWarning(
                    "Failed to open resource pack folder. InstanceId={InstanceId} ResourcePacksDirectory={ResourcePacksDirectory}",
                    selectedInstance.Id,
                    resourcePacksDirectory);
                statusService.Report(Strings.Status_OpenLocalResourcePackFolderFailed);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to prepare resource pack folder for opening. InstanceId={InstanceId}",
                selectedInstance.Id);
            statusService.Report(Strings.Status_OpenLocalResourcePackFolderFailed);
        }
    }

    [RelayCommand(CanExecute = nameof(CanImportLocalResourcePack))]
    private async Task ImportLocalResourcePackAsync()
    {
        if (selectedInstance is null)
            return;

        var archivePath = filePickerService.PickResourcePackArchive();
        if (string.IsNullOrWhiteSpace(archivePath))
            return;

        await ImportResourcePackArchivesAsync([archivePath], ImportTriggerSource.FilePicker);
    }

    public GameSettingsFileDropEvaluation EvaluateDroppedFiles(IReadOnlyList<string> paths)
    {
        return TryValidateImportPaths(paths, Strings.GameSettings_DropResourcePackArchivesOnlyMessage, out var failureMessage)
            ? GameSettingsFileDropEvaluation.Accept(Strings.GameSettings_DropImportResourcePacksMessage)
            : GameSettingsFileDropEvaluation.Reject(failureMessage);
    }

    public Task ImportDroppedResourcePackArchivesAsync(IReadOnlyList<string> paths)
    {
        return ImportResourcePackArchivesAsync(paths, ImportTriggerSource.DragDrop);
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

    [RelayCommand(CanExecute = nameof(CanToggleSelectAllResourcePacks))]
    private void SelectAllResourcePacks()
    {
        if (AreAllVisibleResourcePacksSelected)
        {
            selectionState.ClearVisibleSelections(ResourcePacks);
            selectionState.ClearSelectedPaths();
            SelectedResourcePack = null;
            UpdateSelectedResourcePackState();
            return;
        }

        selectionState.SelectAll(ResourcePacks);
        SelectedResourcePack = null;
        UpdateSelectedResourcePackState();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedResourcePacks))]
    private void RequestDeleteSelectedResourcePacks()
    {
        var selectedResourcePacks = GetSelectedVisibleResourcePacks();
        if (selectedResourcePacks.Count == 0)
            return;

        DeleteResourcePacksRequested?.Invoke(new ResourcePackDeleteRequest(
            selectedResourcePacks.Select(resourcePack => resourcePack.FullPath).ToArray(),
            selectedResourcePacks.Select(resourcePack => resourcePack.Title).ToArray()));
    }

    [RelayCommand]
    private void OpenResourcePackLocation(ResourcePackManagementItemViewModel? resourcePack)
    {
        if (resourcePack is null)
            return;

        try
        {
            if (!instanceFolderService.TryRevealFile(resourcePack.FullPath))
            {
                logger.LogWarning(
                    "Failed to reveal local resource pack file. InstanceId={InstanceId} Path={Path}",
                    selectedInstance?.Id ?? "<none>",
                    resourcePack.FullPath);
                statusService.Report(Strings.Status_OpenLocalResourcePackLocationFailed);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to reveal local resource pack file. InstanceId={InstanceId} Path={Path}",
                selectedInstance?.Id ?? "<none>",
                resourcePack.FullPath);
            statusService.Report(Strings.Status_OpenLocalResourcePackLocationFailed);
        }
    }

    [RelayCommand]
    private void RequestDeleteResourcePack(ResourcePackManagementItemViewModel? resourcePack)
    {
        if (resourcePack is null)
            return;

        DeleteResourcePacksRequested?.Invoke(new ResourcePackDeleteRequest(
            [resourcePack.FullPath],
            [resourcePack.Title]));
    }

    [RelayCommand]
    private void SelectResourcePack(ResourcePackManagementItemViewModel? resourcePack)
    {
        if (resourcePack is null)
        {
            SelectedResourcePack = null;
            if (IsMultiSelectMode)
                selectionState.ClearSelectedPaths();
            selectionState.ClearVisibleSelections(ResourcePacks);
            UpdateSelectedResourcePackState();
            return;
        }

        if (IsMultiSelectMode)
        {
            selectionState.ToggleSelection(resourcePack);
            SelectedResourcePack = null;
            UpdateSelectedResourcePackState();
            return;
        }

        SelectedResourcePack = resourcePack;
        selectionState.SelectSingle(resourcePack, ResourcePacks);
    }

    public async Task DeleteResourcePacksAsync(IReadOnlyList<string> fullPaths)
    {
        ArgumentNullException.ThrowIfNull(fullPaths);

        var resourcePacksToDelete = ResolveLocalResourcePacks(fullPaths);
        if (resourcePacksToDelete.Count == 0)
        {
            ExitMultiSelectMode();
            return;
        }

        logger.LogInformation(
            "Deleting selected resource packs. InstanceId={InstanceId} Count={Count}",
            selectedInstance?.Id ?? "<none>",
            resourcePacksToDelete.Count);
        try
        {
            var failedCount = await localResourcePacksViewModel.DeleteResourcePacksAsync(resourcePacksToDelete);
            ExitMultiSelectMode();
            ReportBatchOperationResult(
                resourcePacksToDelete.Count,
                failedCount,
                Strings.Status_SelectedResourcePacksDeletedFormat,
                Strings.Status_SelectedResourcePacksDeletePartialFailedFormat);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to delete selected resource packs. InstanceId={InstanceId}",
                selectedInstance?.Id ?? "<none>");
            statusService.Report(Strings.Status_SelectedResourcePacksDeleteFailed);
        }
    }

    partial void OnInstalledResourcePackCountChanged(int value)
    {
        OnPropertyChanged(nameof(InstalledSummaryText));
        RaiseAvailabilityPropertyChanges();
        OnPropertyChanged(nameof(ResourcePackEmptyMessage));
    }

    partial void OnResourcePackSearchQueryChanged(string value)
    {
        RefreshFromLocalResourcePacks();
        OnPropertyChanged(nameof(ResourcePackEmptyMessage));
    }

    partial void OnSelectedResourcePackCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasSelectedResourcePacks));
        OnPropertyChanged(nameof(AreAllVisibleResourcePacksSelected));
        OnPropertyChanged(nameof(SelectAllButtonText));
        SelectAllResourcePacksCommand.NotifyCanExecuteChanged();
        RequestDeleteSelectedResourcePacksCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsMultiSelectModeChanged(bool value)
    {
        OnPropertyChanged(nameof(AreAllVisibleResourcePacksSelected));
        OnPropertyChanged(nameof(SelectAllButtonText));
        SelectAllResourcePacksCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadResourcePacksAsync()
    {
        if (selectedInstance is null)
            return;

        IsLoadingResourcePacks = true;
        OnPropertyChanged(nameof(InstalledSummaryText));
        RaiseAvailabilityPropertyChanges();

        try
        {
            await localResourcePacksViewModel.RefreshResourcePacksAsync();
            HasLoadedResourcePacks = true;
            if (isSectionActive)
                ListEntranceAnimationToken++;
            else
                hasPendingVisualRefresh = true;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to load resource packs for section activation. InstanceId={InstanceId}",
                selectedInstance.Id);
            HasLoadedResourcePacks = false;
            ClearDisplayedResourcePacks();
            statusService.Report(Strings.Status_LoadLocalResourcePacksFailed);
        }
        finally
        {
            IsLoadingResourcePacks = false;
            loadTask = null;
            OnPropertyChanged(nameof(InstalledSummaryText));
            RaiseAvailabilityPropertyChanges();
            OnPropertyChanged(nameof(ResourcePackEmptyMessage));
        }
    }

    private void RefreshSummary()
    {
        InstalledResourcePackCount = localResourcePacksViewModel.CurrentResourcePacks.Count;
    }

    private void LocalResourcePacksViewModel_ResourcePacksChanged(object? sender, EventArgs e)
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

    private void RefreshFromLocalResourcePacks()
    {
        var selectedFullPath = selectionState.LastSingleSelectedPath ?? SelectedResourcePack?.FullPath;
        var filteredResourcePacks = StableFilteredItemProjection.Synchronize(
            localResourcePacksViewModel.CurrentResourcePacks,
            selectionState.ItemsByPath,
            resourcePack => resourcePack.FullPath,
            resourcePack => new ResourcePackManagementItemViewModel(resourcePack),
            static (item, resourcePack) => item.SyncFrom(resourcePack),
            MatchesSearch);

        selectionState.SyncSelectionToItems(filteredResourcePacks, IsMultiSelectMode);
        SetVisibleResourcePacks(filteredResourcePacks);

        RefreshSummary();
        OnPropertyChanged(nameof(HasResourcePacks));
        RaiseAvailabilityPropertyChanges();
        OnPropertyChanged(nameof(ResourcePackEmptyMessage));
        OnPropertyChanged(nameof(AreAllVisibleResourcePacksSelected));
        OnPropertyChanged(nameof(SelectAllButtonText));
        SelectAllResourcePacksCommand.NotifyCanExecuteChanged();

        if (IsMultiSelectMode)
        {
            SelectedResourcePack = null;
            UpdateSelectedResourcePackState();
            return;
        }

        var restoredSelection = ResourcePacks.FirstOrDefault(resourcePack =>
            string.Equals(resourcePack.FullPath, selectedFullPath, StringComparison.OrdinalIgnoreCase));
        SelectResourcePack(restoredSelection ?? ResourcePacks.FirstOrDefault());
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
            RefreshFromLocalResourcePacks();
            if (playEntranceAnimation && HasResourcePacks)
                ListEntranceAnimationToken++;
        });
    }

    private bool MatchesSearch(LocalResourcePack resourcePack)
    {
        if (string.IsNullOrWhiteSpace(ResourcePackSearchQuery))
            return true;

        var query = ResourcePackSearchQuery.Trim();
        return Contains(resourcePack.Name, query)
            || Contains(resourcePack.FileName, query);
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

    private bool CanToggleSelectAllResourcePacks()
    {
        return IsMultiSelectMode && HasResourcePacks;
    }

    private void RaiseAvailabilityPropertyChanges()
    {
        OnPropertyChanged(nameof(CanShowResourcePackInfoSection));
        OnPropertyChanged(nameof(CanShowResourcePackScrollableContent));
        OnPropertyChanged(nameof(HasInstalledResourcePacks));
        OnPropertyChanged(nameof(CanShowResourcePackListSection));
        OnPropertyChanged(nameof(CanShowNoResourcePacksEmptyState));
        OnPropertyChanged(nameof(CanShowResourcePackEmptyState));
        OnPropertyChanged(nameof(CanShowResourcePackLoadingState));
    }

    private void EnterMultiSelectMode()
    {
        var selectedResourcePack = SelectedResourcePack;
        IsMultiSelectMode = true;
        SelectedResourcePack = null;
        selectionState.BeginMultiSelect(selectedResourcePack, ResourcePacks);
        UpdateSelectedResourcePackState();
    }

    private void ExitMultiSelectMode()
    {
        IsMultiSelectMode = false;
        selectionState.ClearVisibleSelections(ResourcePacks);
        selectionState.ClearSelectedPaths();
        UpdateSelectedResourcePackState();

        var restoredSelection = ResourcePacks.FirstOrDefault(resourcePack =>
            string.Equals(resourcePack.FullPath, selectionState.LastSingleSelectedPath, StringComparison.OrdinalIgnoreCase));
        SelectResourcePack(restoredSelection ?? ResourcePacks.FirstOrDefault());
    }

    private void ResetSelectionState()
    {
        selectionState.Reset();
        IsMultiSelectMode = false;
        SelectedResourcePack = null;
        SelectedResourcePackCount = 0;
    }

    private void ClearDisplayedResourcePacks()
    {
        selectionState.ClearCache();
        SetVisibleResourcePacks(Array.Empty<ResourcePackManagementItemViewModel>());
        RefreshVisibleResourcePackListItems();
        SelectedResourcePack = null;
        RefreshSummary();
        OnPropertyChanged(nameof(HasResourcePacks));
        RaiseAvailabilityPropertyChanges();
        OnPropertyChanged(nameof(ResourcePackEmptyMessage));
        OnPropertyChanged(nameof(AreAllVisibleResourcePacksSelected));
        OnPropertyChanged(nameof(SelectAllButtonText));
        SelectAllResourcePacksCommand.NotifyCanExecuteChanged();
        UpdateSelectedResourcePackState();
    }

    private void ClearVisibleSelections()
    {
        selectionState.ClearVisibleSelections(ResourcePacks);
    }

    private IReadOnlyList<ResourcePackManagementItemViewModel> GetSelectedVisibleResourcePacks()
    {
        return selectionState.GetSelectedVisibleItems(ResourcePacks);
    }

    private IReadOnlyList<LocalResourcePack> ResolveLocalResourcePacks(IEnumerable<string> fullPaths)
    {
        var pathSet = new HashSet<string>(fullPaths, StringComparer.OrdinalIgnoreCase);
        return localResourcePacksViewModel.CurrentResourcePacks
            .Where(resourcePack => pathSet.Contains(resourcePack.FullPath))
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

    private void UpdateSelectedResourcePackState()
    {
        SelectedResourcePackCount = selectionState.CountSelectedVisibleItems(ResourcePacks);
    }

    partial void OnVisibleResourcePacksChanged(IReadOnlyList<ResourcePackManagementItemViewModel> value)
    {
        OnPropertyChanged(nameof(ResourcePacks));
        RefreshVisibleResourcePackListItems();
    }

    private void SetVisibleResourcePacks(IReadOnlyList<ResourcePackManagementItemViewModel> resourcePacks)
    {
        if (IsSameVisibleResourcePacks(resourcePacks))
            return;

        VisibleResourcePacks = resourcePacks;
    }

    private bool IsSameVisibleResourcePacks(IReadOnlyList<ResourcePackManagementItemViewModel> resourcePacks)
    {
        if (VisibleResourcePacks.Count != resourcePacks.Count)
            return false;

        for (var index = 0; index < resourcePacks.Count; index++)
        {
            if (!ReferenceEquals(VisibleResourcePacks[index], resourcePacks[index]))
                return false;
        }

        return true;
    }

    private void RefreshVisibleResourcePackListItems()
    {
        if (!CanShowResourcePackInfoSection)
        {
            if (VisibleResourcePackListItems.Count > 0)
                VisibleResourcePackListItems = Array.Empty<object>();
            return;
        }

        if (IsSameVisibleResourcePackListItems())
            return;

        var hasListSection = VisibleResourcePacks.Count > 0;
        var items = new object[VisibleResourcePacks.Count + (hasListSection ? 2 : 1)];
        items[0] = ResourcePackManagementInfoPanelItem.Instance;
        if (hasListSection)
            items[1] = ResourcePackManagementListSectionItem.Instance;

        for (var index = 0; index < VisibleResourcePacks.Count; index++)
            items[index + (hasListSection ? 2 : 1)] = VisibleResourcePacks[index];

        VisibleResourcePackListItems = items;
    }

    private bool IsSameVisibleResourcePackListItems()
    {
        var hasListSection = VisibleResourcePacks.Count > 0;
        if (VisibleResourcePackListItems.Count != VisibleResourcePacks.Count + (hasListSection ? 2 : 1))
            return false;

        if (!ReferenceEquals(VisibleResourcePackListItems[0], ResourcePackManagementInfoPanelItem.Instance))
            return false;

        if (!hasListSection)
            return true;

        if (!ReferenceEquals(VisibleResourcePackListItems[1], ResourcePackManagementListSectionItem.Instance))
            return false;

        for (var index = 0; index < VisibleResourcePacks.Count; index++)
        {
            if (!ReferenceEquals(VisibleResourcePackListItems[index + 2], VisibleResourcePacks[index]))
                return false;
        }

        return true;
    }

    private async Task ImportResourcePackArchivesAsync(IReadOnlyList<string> archivePaths, ImportTriggerSource source)
    {
        if (selectedInstance is null)
            return;

        if (!TryValidateImportPaths(archivePaths, Strings.GameSettings_DropResourcePackArchivesOnlyMessage, out var validationMessage))
        {
            if (source is ImportTriggerSource.DragDrop)
            {
                statusService.Report(validationMessage);
            }
            else
            {
                ResourcePackImportFailedRequested?.Invoke(
                    new ResourcePackImportFailureRequest(Strings.Dialog_UnsupportedResourcePackArchiveMessage));
            }

            return;
        }

        logger.LogInformation(
            "Starting local resource pack import batch. InstanceId={InstanceId} Source={Source} FileCount={FileCount}",
            selectedInstance.Id,
            source,
            archivePaths.Count);

        var successCount = 0;
        foreach (var archivePath in archivePaths)
        {
            logger.LogInformation(
                "Importing local resource pack archive. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                selectedInstance.Id,
                archivePath);

            var result = await localResourcePacksViewModel.ImportResourcePackAsync(archivePath, reportStatus: false);
            if (result.IsSuccess)
            {
                successCount++;
                continue;
            }

            switch (result.FailureReason)
            {
                case LocalResourcePackImportFailureReason.UnsupportedArchive:
                    ResourcePackImportFailedRequested?.Invoke(
                        new ResourcePackImportFailureRequest(Strings.Dialog_UnsupportedResourcePackArchiveMessage));
                    break;
                case LocalResourcePackImportFailureReason.FileNotFound:
                    statusService.Report(Strings.Status_LocalResourcePackImportFileNotFound);
                    break;
                case LocalResourcePackImportFailureReason.UnexpectedError:
                    logger.LogWarning(
                        "Local resource pack import failed unexpectedly after service call. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                        selectedInstance.Id,
                        archivePath);
                    statusService.Report(Strings.Status_LocalResourcePackImportFailed);
                    break;
            }

            return;
        }

        if (successCount > 0)
        {
            statusService.Report(successCount == 1
                ? Strings.Status_LocalResourcePackImported
                : string.Format(Strings.Status_LocalResourcePacksImportedFormat, successCount));
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

public sealed class ResourcePackManagementInfoPanelItem
{
    public static ResourcePackManagementInfoPanelItem Instance { get; } = new();

    private ResourcePackManagementInfoPanelItem()
    {
    }
}

public sealed class ResourcePackManagementListSectionItem
{
    public static ResourcePackManagementListSectionItem Instance { get; } = new();

    private ResourcePackManagementListSectionItem()
    {
    }
}
