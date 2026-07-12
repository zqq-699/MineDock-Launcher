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
/// 管理实例存档的监听、压缩包导入、筛选、多选和批量删除页面状态。
/// </summary>
public sealed partial class InstanceSaveManagementSettingsViewModel : GameSettingsDetailsSectionViewModelBase
{
    // 本地数据由 LocalSavesViewModel 负责；本类只维护页面投影、交互选择和 UI 服务协调。
    private readonly LocalSavesViewModel localSavesViewModel;
    private readonly IStatusService statusService;
    private readonly IInstanceFolderService instanceFolderService;
    private readonly IFilePickerService filePickerService;
    private readonly IInstanceContentImportPathValidator importPathValidator;
    private readonly IUiDispatcher uiDispatcher;
    private readonly ILogger<InstanceSaveManagementSettingsViewModel> logger;
    private readonly LocalContentSelectionState<SaveManagementSaveItemViewModel> selectionState;
    // 这组字段描述页面生命周期。loadTask 合并激活请求，两个刷新标记合并隐藏期间的文件事件。
    private Task? loadTask;
    private GameInstance? selectedInstance;
    private bool hasPendingVisualRefresh;
    private bool isVisibleRefreshQueued;
    private bool isSectionActive;
    private bool isInitialProjectionReady;
    private bool suppressLocalCollectionEvents;

    // 以下属性是 XAML 所需的派生页面状态，不是第二份业务数据源。
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
        IInstanceContentImportPathValidator importPathValidator,
        IUiDispatcher? uiDispatcher = null,
        ILogger<InstanceSaveManagementSettingsViewModel>? logger = null)
        : base(parent)
    {
        this.localSavesViewModel = localSavesViewModel;
        this.statusService = statusService;
        this.instanceFolderService = instanceFolderService;
        this.filePickerService = filePickerService;
        this.importPathValidator = importPathValidator;
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

    public bool CanShowSaveScrollableContent => selectedInstance is not null && isInitialProjectionReady;

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

    /// <summary>
    /// 切换存档实例上下文，并整体重置监听、选择和延迟刷新状态。
    /// </summary>
    public override void OnSelectedInstanceChanged(GameInstance? instance)
    {
        // 切换监听目标时会触发集合清空事件；暂时抑制回调，最后由本方法一次性重置页面。
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
        SetInitialProjectionReady(false);
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
        // 只在页面可见时监听目录，避免后台页面持续刷新和播放动画。
        isSectionActive = true;
        localSavesViewModel.SetWatcherEnabled(selectedInstance is not null);
        if (hasPendingVisualRefresh && HasLoadedSaves)
            PublishReadyProjection();

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

    /// <summary>
    /// 批量删除解析出的本地存档，汇总部分失败并统一刷新页面快照。
    /// </summary>
    public async Task DeleteSavesAsync(IReadOnlyList<string> fullPaths)
    {
        ArgumentNullException.ThrowIfNull(fullPaths);

        // 将 UI 路径重新解析为当前快照对象，自动忽略刷新后已不存在的选择。
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
            // 底层批处理会继续处理剩余项并返回失败数，因此页面可以给出准确的部分成功提示。
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

    /// <summary>
    /// 首次加载当前实例存档，并合并页面激活期间的重复加载请求。
    /// </summary>
    private async Task LoadSavesAsync()
    {
        if (selectedInstance is null)
            return;

        // 先发布 Loading 状态，让空列表能够显示骨架/加载提示而不是误报“没有存档”。
        SetInitialProjectionReady(false);
        IsLoadingSaves = true;
        OnPropertyChanged(nameof(InstalledSummaryText));
        RaiseAvailabilityPropertyChanges();

        try
        {
            if (!await localSavesViewModel.RefreshSavesAsync())
                return;
            HasLoadedSaves = true;
            // 隐藏页面不播放动画，只记住下次激活需要一次完整视觉刷新。
            if (isSectionActive)
                PublishReadyProjection();
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
            hasPendingVisualRefresh = false;
            SetInitialProjectionReady(true);
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

        if (!HasLoadedSaves)
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
    /// 将共享存档快照增量投影到可见列表，并恢复稳定路径对应的选择。
    /// </summary>
    private void RefreshFromLocalSaves()
    {
        // 先保存稳定路径；投影同步可能更换可见集合，但相同存档项会继续复用原 ViewModel。
        var selectedFullPath = selectionState.LastSingleSelectedPath ?? SelectedSave?.FullPath;
        var filteredSaves = StableFilteredItemProjection.Synchronize(
            localSavesViewModel.CurrentSaves,
            selectionState.ItemsByPath,
            save => save.FullPath,
            save => new SaveManagementSaveItemViewModel(save),
            static (item, save) => item.SyncFrom(save),
            MatchesSearch);

        // 搜索过滤后裁剪不可见选择，避免批量命令作用于用户当前看不到的项目。
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

        // 单选模式优先恢复刷新前项目；项目消失时再选择新的第一项。
        var restoredSelection = Saves.FirstOrDefault(save =>
            string.Equals(save.FullPath, selectedFullPath, StringComparison.OrdinalIgnoreCase));
        SelectSave(restoredSelection ?? Saves.FirstOrDefault());
    }

    private void QueueVisibleRefresh()
    {
        if (isVisibleRefreshQueued)
            return;

        isVisibleRefreshQueued = true;
        uiDispatcher.Post(() =>
        {
            isVisibleRefreshQueued = false;
            // 调度执行时页面可能已经离开，不能在后台重建列表或消费动画令牌。
            if (!isSectionActive)
            {
                hasPendingVisualRefresh = true;
                return;
            }

            hasPendingVisualRefresh = false;
            RefreshFromLocalSaves();
        });
    }

    private void PublishReadyProjection()
    {
        hasPendingVisualRefresh = false;
        RefreshFromLocalSaves();
        SetInitialProjectionReady(true);
        ListEntranceAnimationToken++;
    }

    private void SetInitialProjectionReady(bool value)
    {
        if (isInitialProjectionReady == value)
            return;

        isInitialProjectionReady = value;
        OnPropertyChanged(nameof(CanShowSaveScrollableContent));
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
        if (LocalContentListPresentation.HasSameReferences(VisibleSaves, saves))
            return;

        VisibleSaves = saves;
    }

    private void RefreshVisibleSaveListItems()
    {
        var items = LocalContentListPresentation.CreateSectionedItems(
            VisibleSaves,
            SaveManagementInfoPanelItem.Instance,
            SaveManagementListSectionItem.Instance,
            CanShowSaveInfoSection);
        if (!LocalContentListPresentation.HasSameReferences(VisibleSaveListItems, items))
            VisibleSaveListItems = items;
    }

    /// <summary>
    /// 顺序导入多个存档压缩包，逐项记录结果并在批次结束后只刷新一次列表。
    /// </summary>
    private async Task ImportSaveArchivesAsync(IReadOnlyList<string> archivePaths, ImportTriggerSource source)
    {
        if (selectedInstance is null)
            return;

        // 文件选择器和拖放共享校验，但错误呈现方式不同：拖放用状态栏，选择器用对话框。
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

        // 协调器顺序执行并在首个业务失败时停止，防止连续弹出多个相同错误。
        var batch = await LocalContentImportBatchCoordinator.ExecuteAsync(
            archivePaths,
            async archivePath =>
            {
                logger.LogInformation(
                    "Importing local save archive. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                    selectedInstance.Id,
                    archivePath);
                return await localSavesViewModel.ImportSaveFromArchiveAsync(archivePath, reportStatus: false);
            },
            result => result.IsSuccess);

        if (batch.Failure is not null)
        {
            // 保留领域失败原因，在 App 层映射为资源化且可操作的提示。
            switch (batch.Failure.FailureReason)
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
                        batch.FailedPath);
                    statusService.Report(Strings.Status_LocalSaveImportFailed);
                    break;
            }
            return;
        }

        // 批次内集合变化由共享 ViewModel 合并处理，这里只负责最终用户反馈。
        if (batch.SuccessCount > 0)
        {
            statusService.Report(batch.SuccessCount == 1
                ? Strings.Status_LocalSaveImported
                : string.Format(Strings.Status_LocalSavesImportedFormat, batch.SuccessCount));
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
            InstanceContentImportKind.SaveArchive,
            invalidTypeMessage,
            out failureMessage);
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
