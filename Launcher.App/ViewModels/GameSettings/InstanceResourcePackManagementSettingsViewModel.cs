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

/// <summary>
/// 管理实例资源包的目录监听、导入、筛选、多选与批量删除状态。
/// </summary>
public sealed partial class InstanceResourcePackManagementSettingsViewModel : GameSettingsDetailsSectionViewModelBase
{
    // LocalResourcePacksViewModel 是目录内容的唯一来源；本类只保存筛选、选择和页面生命周期状态。
    private readonly LocalResourcePacksViewModel localResourcePacksViewModel;
    private readonly IStatusService statusService;
    private readonly IInstanceFolderService instanceFolderService;
    private readonly IFilePickerService filePickerService;
    private readonly IInstanceContentImportPathValidator importPathValidator;
    private readonly IUiDispatcher uiDispatcher;
    private readonly ILogger<InstanceResourcePackManagementSettingsViewModel> logger;
    private readonly LocalContentSelectionState<ResourcePackManagementItemViewModel> selectionState;
    // 加载任务和刷新标记共同保证激活幂等，并把隐藏期间的多次目录事件压缩成一次刷新。
    private Task? loadTask;
    private GameInstance? selectedInstance;
    private bool hasPendingVisualRefresh;
    private bool isVisibleRefreshQueued;
    private bool isSectionActive;
    private bool isInitialProjectionReady;
    private bool suppressLocalCollectionEvents;

    // 可观察属性均为界面投影，任何磁盘变化都必须先进入共享本地内容 ViewModel。
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
        IInstanceContentImportPathValidator importPathValidator,
        IUiDispatcher? uiDispatcher = null,
        ILogger<InstanceResourcePackManagementSettingsViewModel>? logger = null)
        : base(parent)
    {
        this.localResourcePacksViewModel = localResourcePacksViewModel;
        this.statusService = statusService;
        this.instanceFolderService = instanceFolderService;
        this.filePickerService = filePickerService;
        this.importPathValidator = importPathValidator;
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

    public bool CanShowResourcePackScrollableContent => selectedInstance is not null && isInitialProjectionReady;

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

    /// <summary>
    /// 切换资源包实例上下文，并使旧实例的加载、选择和视觉刷新失效。
    /// </summary>
    public override void OnSelectedInstanceChanged(GameInstance? instance)
    {
        // 切换底层集合时会同步触发事件，抑制回调以避免在半重置状态下重建列表。
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
        SetInitialProjectionReady(false);
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
        // 页面不可见时关闭 watcher，激活后再补做被合并的刷新并恢复动画。
        isSectionActive = true;
        localResourcePacksViewModel.SetWatcherEnabled(selectedInstance is not null);
        if (hasPendingVisualRefresh && HasLoadedResourcePacks)
            PublishReadyProjection();

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

    /// <summary>
    /// 批量删除资源包，允许部分失败并在完成后统一同步本地快照。
    /// </summary>
    public async Task DeleteResourcePacksAsync(IReadOnlyList<string> fullPaths)
    {
        ArgumentNullException.ThrowIfNull(fullPaths);

        // 路径必须重新匹配当前快照，刷新后已不存在的资源包不会进入删除服务。
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
            // 底层逐项处理并返回失败数，使批量删除能够保留成功项而不是整体回滚。
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

    /// <summary>
    /// 加载当前实例资源包，并复用尚未结束的同一加载任务。
    /// </summary>
    private async Task LoadResourcePacksAsync()
    {
        if (selectedInstance is null)
            return;

        // Loading 与 HasLoaded 分离，首次进入和刷新失败可以呈现不同的空状态。
        SetInitialProjectionReady(false);
        IsLoadingResourcePacks = true;
        OnPropertyChanged(nameof(InstalledSummaryText));
        RaiseAvailabilityPropertyChanges();

        try
        {
            if (!await localResourcePacksViewModel.RefreshResourcePacksAsync())
                return;
            HasLoadedResourcePacks = true;
            // 只有可见页面立即播放列表动画，隐藏页面仅设置待刷新标志。
            if (isSectionActive)
                PublishReadyProjection();
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
            hasPendingVisualRefresh = false;
            SetInitialProjectionReady(true);
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

        if (!HasLoadedResourcePacks)
        {
            hasPendingVisualRefresh = true;
            return;
        }

        if (!isSectionActive)
        {
            hasPendingVisualRefresh = true;
            return;
        }

        QueueVisibleRefresh();
    }

    /// <summary>
    /// 增量更新筛选后的资源包列表，并保持单选或多选状态稳定。
    /// </summary>
    private void RefreshFromLocalResourcePacks()
    {
        // 用完整路径作为稳定身份复用 Item ViewModel，保持选择和图片等 UI 状态。
        var selectedFullPath = selectionState.LastSingleSelectedPath ?? SelectedResourcePack?.FullPath;
        var filteredResourcePacks = StableFilteredItemProjection.Synchronize(
            localResourcePacksViewModel.CurrentResourcePacks,
            selectionState.ItemsByPath,
            resourcePack => resourcePack.FullPath,
            resourcePack => new ResourcePackManagementItemViewModel(resourcePack),
            static (item, resourcePack) => item.SyncFrom(resourcePack),
            MatchesSearch);

        // 多选仅覆盖当前可见集合，搜索隐藏的项目不会被批量操作误处理。
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

        // 单选刷新后按路径恢复；原项消失时使用第一项保证详情面板有确定状态。
        var restoredSelection = ResourcePacks.FirstOrDefault(resourcePack =>
            string.Equals(resourcePack.FullPath, selectedFullPath, StringComparison.OrdinalIgnoreCase));
        SelectResourcePack(restoredSelection ?? ResourcePacks.FirstOrDefault());
    }

    private void QueueVisibleRefresh()
    {
        if (isVisibleRefreshQueued)
            return;

        isVisibleRefreshQueued = true;
        uiDispatcher.Post(() =>
        {
            isVisibleRefreshQueued = false;
            // UI 回调执行时重新判断激活状态，防止页面离开后的排队任务修改隐藏页面。
            if (!isSectionActive)
            {
                hasPendingVisualRefresh = true;
                return;
            }

            hasPendingVisualRefresh = false;
            RefreshFromLocalResourcePacks();
        });
    }

    private void PublishReadyProjection()
    {
        hasPendingVisualRefresh = false;
        RefreshFromLocalResourcePacks();
        SetInitialProjectionReady(true);
        ListEntranceAnimationToken++;
    }

    private void SetInitialProjectionReady(bool value)
    {
        if (isInitialProjectionReady == value)
            return;

        isInitialProjectionReady = value;
        OnPropertyChanged(nameof(CanShowResourcePackScrollableContent));
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
        if (LocalContentListPresentation.HasSameReferences(VisibleResourcePacks, resourcePacks))
            return;

        VisibleResourcePacks = resourcePacks;
    }

    private void RefreshVisibleResourcePackListItems()
    {
        var items = LocalContentListPresentation.CreateSectionedItems(
            VisibleResourcePacks,
            ResourcePackManagementInfoPanelItem.Instance,
            ResourcePackManagementListSectionItem.Instance,
            CanShowResourcePackInfoSection);
        if (!LocalContentListPresentation.HasSameReferences(VisibleResourcePackListItems, items))
            VisibleResourcePackListItems = items;
    }

    /// <summary>
    /// 执行资源包批量导入并汇总结果，避免每个文件完成时重复刷新整列表。
    /// </summary>
    private async Task ImportResourcePackArchivesAsync(IReadOnlyList<string> archivePaths, ImportTriggerSource source)
    {
        if (selectedInstance is null)
            return;

        // 拖放和文件选择器复用同一校验规则，仅错误反馈载体不同。
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

        // 顺序执行避免同名目标和目录 watcher 互相竞争，并统一汇总成功数量。
        var batch = await LocalContentImportBatchCoordinator.ExecuteAsync(
            archivePaths,
            async archivePath =>
            {
                logger.LogInformation(
                    "Importing local resource pack archive. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                    selectedInstance.Id,
                    archivePath);
                return await localResourcePacksViewModel.ImportResourcePackAsync(archivePath, reportStatus: false);
            },
            result => result.IsSuccess);

        if (batch.Failure is not null)
        {
            // 保留服务返回的失败类型，在页面边界映射为资源化且可操作的提示。
            switch (batch.Failure.FailureReason)
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
                        batch.FailedPath);
                    statusService.Report(Strings.Status_LocalResourcePackImportFailed);
                    break;
            }
            return;
        }

        // 导入过程的集合变化由 watcher/共享 ViewModel 刷新，这里不重复操作集合。
        if (batch.SuccessCount > 0)
        {
            statusService.Report(batch.SuccessCount == 1
                ? Strings.Status_LocalResourcePackImported
                : string.Format(Strings.Status_LocalResourcePacksImportedFormat, batch.SuccessCount));
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
            InstanceContentImportKind.ResourcePack,
            invalidTypeMessage,
            out failureMessage);
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
