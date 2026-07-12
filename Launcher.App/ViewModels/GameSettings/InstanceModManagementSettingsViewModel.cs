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
/// 将本地 Mod 快照投影为可筛选、可批量选择的页面状态，并协调页面激活、文件监听和导入冲突。
/// </summary>
public sealed partial class InstanceModManagementSettingsViewModel : GameSettingsDetailsSectionViewModelBase
{
    // LocalModsViewModel 提供真实 Mod 快照和文件操作；本类负责页面筛选、选择及对话框编排。
    private readonly LocalModsViewModel localModsViewModel;
    private readonly IStatusService statusService;
    private readonly IInstanceFolderService instanceFolderService;
    private readonly IFilePickerService filePickerService;
    private readonly IInstanceContentImportPathValidator importPathValidator;
    private readonly IUiDispatcher uiDispatcher;
    private readonly ILogger<InstanceModManagementSettingsViewModel> logger;
    // 规范化路径索引用于跨刷新复用 Item ViewModel；多选集合也以路径而非对象引用保存身份。
    private readonly Dictionary<string, ModManagementModItemViewModel> allModsByStablePath = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> selectedModPaths = new(StringComparer.OrdinalIgnoreCase);
    // 冲突对话框通过 TaskCompletionSource 将事件驱动 UI 转换为可顺序 await 的导入步骤。
    private TaskCompletionSource<bool>? pendingImportConflictResolutionSource;
    // 生命周期字段用于合并重复加载、隐藏页面刷新和同一 Dispatcher 周期内的集合事件。
    private Task? loadTask;
    private GameInstance? selectedInstance;
    private string? lastSingleSelectedModPath;
    private bool hasPendingVisualRefresh;
    private bool isVisibleRefreshQueued;
    private bool isSectionActive;
    private bool isInitialProjectionReady;
    private bool suppressLocalCollectionEvents;

    // 可观察属性是本地快照的 UI 投影，不直接拥有任何文件系统状态。
    [ObservableProperty]
    private int installedModCount;

    [ObservableProperty]
    private int enabledModCount;

    [ObservableProperty]
    private ModManagementModItemViewModel? selectedMod;

    [ObservableProperty]
    private string modSearchQuery = string.Empty;

    [ObservableProperty]
    private ModManagementFilter modFilter = ModManagementFilter.All;

    [ObservableProperty]
    private bool isMultiSelectMode;

    [ObservableProperty]
    private int selectedModCount;

    [ObservableProperty]
    private bool isLoadingMods;

    [ObservableProperty]
    private bool hasLoadedMods;

    [ObservableProperty]
    private IReadOnlyList<ModManagementModItemViewModel> visibleMods = Array.Empty<ModManagementModItemViewModel>();

    [ObservableProperty]
    private IReadOnlyList<object> visibleModListItems = Array.Empty<object>();

    [ObservableProperty]
    private int listEntranceAnimationToken;

    public InstanceModManagementSettingsViewModel(
        GameSettingsDetailsViewModel parent,
        LocalModsViewModel localModsViewModel,
        IStatusService statusService,
        IInstanceFolderService instanceFolderService,
        IFilePickerService filePickerService,
        IInstanceContentImportPathValidator importPathValidator,
        IUiDispatcher? uiDispatcher = null,
        ILogger<InstanceModManagementSettingsViewModel>? logger = null)
        : base(parent)
    {
        this.localModsViewModel = localModsViewModel;
        this.statusService = statusService;
        this.instanceFolderService = instanceFolderService;
        this.filePickerService = filePickerService;
        this.importPathValidator = importPathValidator;
        this.uiDispatcher = uiDispatcher ?? ImmediateUiDispatcher.Instance;
        this.logger = logger ?? NullLogger<InstanceModManagementSettingsViewModel>.Instance;
        this.localModsViewModel.ModsChanged += LocalModsViewModel_ModsChanged;
    }

    public event Action<ModDeleteRequest>? DeleteModsRequested;
    public event Action<ModImportConflictRequest>? ImportModConflictRequested;
    public event Action<GameInstance>? OnlineModInstallRequested;

    public override bool UsesFullViewportLayout => true;

    public IReadOnlyList<ModManagementModItemViewModel> Mods => VisibleMods;

    public bool IsModManagementSupported => selectedInstance?.Loader is not LoaderKind.Vanilla;

    public bool CanShowModInfoSection => IsModManagementSupported;

    public bool HasMods => Mods.Count > 0;

    public bool CanShowModScrollableContent => IsModManagementSupported && isInitialProjectionReady;

    public bool HasInstalledMods => InstalledModCount > 0;

    public bool CanShowModListSection => IsModManagementSupported && (IsLoadingMods || HasInstalledMods);

    public bool CanShowNoModsEmptyState => IsModManagementSupported && HasLoadedMods && !IsLoadingMods && !HasInstalledMods;

    public bool CanShowModEmptyState => IsModManagementSupported && HasLoadedMods && !IsLoadingMods && HasInstalledMods && !HasMods;

    public bool CanShowModUnavailableState => !IsModManagementSupported;

    public bool CanShowModLoadingState => IsModManagementSupported && IsLoadingMods && !HasLoadedMods;

    public bool HasSelectedMods => SelectedModCount > 0;

    public bool AreAllVisibleModsSelected => HasMods && SelectedModCount == Mods.Count;

    public string SelectAllButtonText => AreAllVisibleModsSelected
        ? Strings.GameSettings_ModManagementCancelSelectAllButton
        : Strings.GameSettings_ModManagementSelectAllButton;

    public string InstalledSummaryText => IsLoadingMods && !HasLoadedMods
        ? Strings.GameSettings_ModManagementLoading
        : string.Format(
            Strings.GameSettings_ModManagementInstalledSummaryFormat,
            InstalledModCount,
            EnabledModCount);

    public string ModEmptyMessage => !HasInstalledMods || string.IsNullOrWhiteSpace(ModSearchQuery)
        ? Strings.GameSettings_ModManagementEmptyMessage
        : Strings.GameSettings_ModManagementSearchEmptyMessage;

    public string ModUnavailableMessage => Strings.GameSettings_ModManagementUnavailableMessage;

    public bool IsAllModsFilterSelected => ModFilter is ModManagementFilter.All;

    public bool IsEnabledModsFilterSelected => ModFilter is ModManagementFilter.Enabled;

    public bool IsDisabledModsFilterSelected => ModFilter is ModManagementFilter.Disabled;

    public Task SetSelectedInstanceAsync(GameInstance? instance)
    {
        OnSelectedInstanceChanged(instance);
        return EnsureLoadedForSelectedInstanceAsync();
    }

    /// <summary>
    /// 将页面完整切换到新实例上下文，清理旧选择、冲突请求和可见项状态。
    /// </summary>
    public override void OnSelectedInstanceChanged(GameInstance? instance)
    {
        // 实例引用变化意味着现有选择、冲突对话框和可见项都不再有效，必须作为一个状态边界整体重置。
        selectedInstance = instance;
        ResolvePendingImportConflict(false);
        loadTask = null;
        hasPendingVisualRefresh = false;
        isVisibleRefreshQueued = false;
        suppressLocalCollectionEvents = true;
        try
        {
            localModsViewModel.SetSelectedInstance(instance);
            localModsViewModel.SetWatcherEnabled(isSectionActive && IsModManagementSupported);
        }
        finally
        {
            suppressLocalCollectionEvents = false;
        }

        IsLoadingMods = false;
        HasLoadedMods = false;
        allModsByStablePath.Clear();
        SetInitialProjectionReady(false);
        ResetSelectionState();
        ClearDisplayedMods();
    }

    public bool RefreshSelectedInstanceReference(GameInstance? instance)
    {
        if (ShouldResetForInstanceReference(instance))
        {
            OnSelectedInstanceChanged(instance);
            return true;
        }

        selectedInstance = instance;
        return false;
    }

    public override void OnSectionDeactivated()
    {
        isSectionActive = false;
        localModsViewModel.SetWatcherEnabled(false);
    }

    public void SuspendLocalWatchersForInstanceRename()
    {
        localModsViewModel.SuspendWatcherForInstanceRename();
    }

    public void ResumeLocalWatchersAfterInstanceRename()
    {
        localModsViewModel.ResumeWatcherAfterInstanceRename();
    }

    /// <summary>
    /// 激活页面监听，并补做页面隐藏期间合并掉的视觉刷新。
    /// </summary>
    public override Task OnSectionActivatedAsync()
    {
        isSectionActive = true;
        localModsViewModel.SetWatcherEnabled(IsModManagementSupported);
        if (hasPendingVisualRefresh && HasLoadedMods)
            PublishReadyProjection();

        return EnsureLoadedForSelectedInstanceAsync();
    }

    public Task EnsureLoadedForSelectedInstanceAsync()
    {
        if (selectedInstance is null || !IsModManagementSupported)
            return Task.CompletedTask;

        if (loadTask is { IsCompleted: false })
            return loadTask;

        if (HasLoadedMods)
            return Task.CompletedTask;

        loadTask = LoadModsAsync();
        return loadTask;
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

        await ImportModFilesAsync([modPath], ImportTriggerSource.FilePicker);
    }

    public Task ReplaceImportedModAsync(string sourcePath)
    {
        ResolvePendingImportConflict(true);
        return Task.CompletedTask;
    }

    public void SkipPendingImportedModReplacement()
    {
        ResolvePendingImportConflict(false);
    }

    public GameSettingsFileDropEvaluation EvaluateDroppedFiles(IReadOnlyList<string> paths)
    {
        if (!IsModManagementSupported)
            return GameSettingsFileDropEvaluation.Reject(ModUnavailableMessage);

        return TryValidateImportPaths(paths, Strings.GameSettings_DropModsOnlyMessage, out var failureMessage)
            ? GameSettingsFileDropEvaluation.Accept(Strings.GameSettings_DropImportModsMessage)
            : GameSettingsFileDropEvaluation.Reject(failureMessage);
    }

    public Task ImportDroppedModFilesAsync(IReadOnlyList<string> paths)
    {
        return ImportModFilesAsync(paths, ImportTriggerSource.DragDrop);
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

    /// <summary>
    /// 校验并顺序导入一组 Mod 文件，逐项处理重名冲突并汇总部分失败结果。
    /// </summary>
    private async Task ImportModFilesAsync(IReadOnlyList<string> paths, ImportTriggerSource source)
    {
        if (selectedInstance is null)
            return;

        // 先对整个批次做类型/目录校验，避免导入一半后才发现输入中混有非法路径。
        if (!TryValidateImportPaths(paths, Strings.GameSettings_DropModsOnlyMessage, out var validationMessage))
        {
            statusService.Report(source is ImportTriggerSource.DragDrop
                ? validationMessage
                : Strings.Status_LocalModImportFailed);
            return;
        }

        logger.LogInformation(
            "Starting local mod import batch. InstanceId={InstanceId} Source={Source} FileCount={FileCount}",
            selectedInstance.Id,
            source,
            paths.Count);

        // 顺序处理是有意的：每个重名文件都可能需要等待独立的用户确认。
        var successCount = 0;
        foreach (var modPath in paths)
        {
            var fileName = Path.GetFileName(modPath);
            var overwriteExisting = false;
            if (localModsViewModel.Mods.Any(mod => string.Equals(mod.FileName, fileName, StringComparison.OrdinalIgnoreCase)))
            {
                // 冲突判断基于当前快照；确认替换后由服务再次负责安全覆盖。
                var replace = await RequestModImportConflictResolutionAsync(modPath, fileName);
                if (!replace)
                {
                    logger.LogInformation(
                        "Skipping local mod replacement after user canceled conflict dialog. InstanceId={InstanceId} SourcePath={SourcePath}",
                        selectedInstance.Id,
                        modPath);
                    continue;
                }

                overwriteExisting = true;
            }

            try
            {
                var imported = await localModsViewModel.ImportModFromPathAsync(modPath, overwriteExisting, reportStatus: false);
                // 返回 false 是可预期业务失败；停止批次以免连续产生相同失败和提示。
                if (!imported)
                {
                    statusService.Report(Strings.Status_LocalModImportFailed);
                    return;
                }

                successCount++;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Failed to import local mod during batch import. InstanceId={InstanceId} SourcePath={SourcePath} OverwriteExisting={OverwriteExisting}",
                    selectedInstance.Id,
                    modPath,
                    overwriteExisting);
                statusService.Report(Strings.Status_LocalModImportFailed);
                return;
            }
        }

        // 单个文件状态在导入期间不重复上报，只在批次结束时给出汇总。
        if (successCount > 0)
        {
            statusService.Report(successCount == 1
                ? Strings.Status_LocalModImported
                : string.Format(Strings.Status_LocalModsImportedFormat, successCount));
        }
    }

    [RelayCommand]
    private void InstallOnlineMod()
    {
        if (selectedInstance is null || !IsModManagementSupported)
            return;

        logger.LogInformation(
            "Online mod install requested from instance mod management. InstanceId={InstanceId}, MinecraftVersion={MinecraftVersion}, Loader={Loader}",
            selectedInstance.Id,
            selectedInstance.MinecraftVersion,
            selectedInstance.Loader);
        OnlineModInstallRequested?.Invoke(selectedInstance);
    }

    [RelayCommand]
    private void SetModFilter(ModManagementFilter filter)
    {
        ModFilter = filter;
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
            suppressLocalCollectionEvents = true;
            try
            {
                await localModsViewModel.ToggleModAsync(localMod);
            }
            finally
            {
                suppressLocalCollectionEvents = false;
            }

            RefreshFromLocalMods();
        }
        catch (Exception exception)
        {
            suppressLocalCollectionEvents = false;
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
            item.IsSelected = false;
    }

    /// <summary>
    /// 删除路径对应的 Mod；批量操作允许部分失败，并在结束后统一刷新快照和选择状态。
    /// </summary>
    public async Task DeleteModsAsync(IReadOnlyList<string> fullPaths)
    {
        ArgumentNullException.ThrowIfNull(fullPaths);

        // 根据当前快照解析路径，自动忽略筛选/刷新后已失效的选择。
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
            // 底层逐项删除并返回失败数量，允许用户保留已成功删除的结果。
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

    partial void OnModFilterChanged(ModManagementFilter value)
    {
        RefreshFromLocalMods();
        OnPropertyChanged(nameof(IsAllModsFilterSelected));
        OnPropertyChanged(nameof(IsEnabledModsFilterSelected));
        OnPropertyChanged(nameof(IsDisabledModsFilterSelected));
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

    /// <summary>
    /// 首次加载当前实例 Mod，并把并发调用合并到同一个加载任务。
    /// </summary>
    private async Task LoadModsAsync()
    {
        if (selectedInstance is null || !IsModManagementSupported)
            return;

        // Loading 和 HasLoaded 分开表示“首次加载中”“已有结果”和“加载失败”三种状态。
        SetInitialProjectionReady(false);
        IsLoadingMods = true;
        OnPropertyChanged(nameof(InstalledSummaryText));
        RaiseAvailabilityPropertyChanges();

        try
        {
            if (!await localModsViewModel.RefreshModsAsync())
                return;
            HasLoadedMods = true;
            // 隐藏页面不消费入场动画；下次激活时才触发一次完整视觉更新。
            if (isSectionActive)
                PublishReadyProjection();
            else
                hasPendingVisualRefresh = true;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to load mods for section activation. InstanceId={InstanceId}",
                selectedInstance.Id);
            HasLoadedMods = false;
            ClearDisplayedMods();
            hasPendingVisualRefresh = false;
            SetInitialProjectionReady(true);
            statusService.Report(Strings.Status_LoadLocalModsFailed);
        }
        finally
        {
            IsLoadingMods = false;
            loadTask = null;
            OnPropertyChanged(nameof(InstalledSummaryText));
            RaiseAvailabilityPropertyChanges();
            OnPropertyChanged(nameof(ModEmptyMessage));
        }
    }

    private void RefreshSummary()
    {
        InstalledModCount = localModsViewModel.CurrentMods.Count;
        EnabledModCount = localModsViewModel.CurrentMods.Count(mod => mod.IsEnabled);
    }

    private void LocalModsViewModel_ModsChanged(object? sender, EventArgs e)
    {
        if (suppressLocalCollectionEvents)
            return;

        if (!HasLoadedMods)
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
    /// 增量同步本地 Mod 快照到筛选列表，同时恢复单选或裁剪多选集合。
    /// </summary>
    private void RefreshFromLocalMods()
    {
        // 使用规范化路径复用现有 Item ViewModel，既保持选中状态，也避免刷新时重建整棵可见列表。
        var selectedStablePath = GetStableModPath(lastSingleSelectedModPath ?? SelectedMod?.FullPath);
        var filteredMods = StableFilteredItemProjection.Synchronize(
            localModsViewModel.CurrentMods,
            allModsByStablePath,
            mod => GetStableModPath(mod.FullPath),
            mod => new ModManagementModItemViewModel(mod),
            static (item, mod) => item.SyncFrom(mod),
            MatchesSearch);

        // 搜索或筛选隐藏的项目不应继续留在批量选择中。
        if (IsMultiSelectMode)
            selectedModPaths.IntersectWith(filteredMods.Select(mod => mod.FullPath));

        // 同步所有缓存项的选择标志，防止离开筛选后看到过期勾选状态。
        foreach (var item in allModsByStablePath.Values)
            item.IsSelected = IsMultiSelectMode && selectedModPaths.Contains(item.FullPath);

        SetVisibleMods(filteredMods);

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

        // 单选模式按稳定路径恢复；原 Mod 消失时回退到当前第一项。
        var restoredSelection = Mods.FirstOrDefault(mod =>
            string.Equals(GetStableModPath(mod.FullPath), selectedStablePath, StringComparison.OrdinalIgnoreCase));
        SelectMod(restoredSelection ?? Mods.FirstOrDefault());
    }

    /// <summary>
    /// 将密集集合事件合并为一次 UI 调度，并按页面可见性决定立即刷新或延后。
    /// </summary>
    private void QueueVisibleRefresh()
    {
        // 合并同一 UI 循环内的多次集合事件；页面不可见时只记录一次待刷新标记。
        if (isVisibleRefreshQueued)
            return;

        isVisibleRefreshQueued = true;
        uiDispatcher.Post(() =>
        {
            isVisibleRefreshQueued = false;
            // 回调执行前页面可能已离开，隐藏页面只保留待刷新标志。
            if (!isSectionActive)
            {
                hasPendingVisualRefresh = true;
                return;
            }

            hasPendingVisualRefresh = false;
            RefreshFromLocalMods();
        });
    }

    private void PublishReadyProjection()
    {
        hasPendingVisualRefresh = false;
        RefreshFromLocalMods();
        SetInitialProjectionReady(true);
        ListEntranceAnimationToken++;
    }

    private void SetInitialProjectionReady(bool value)
    {
        if (isInitialProjectionReady == value)
            return;

        isInitialProjectionReady = value;
        OnPropertyChanged(nameof(CanShowModScrollableContent));
    }

    private bool MatchesSearch(LocalMod mod)
    {
        if (ModFilter is ModManagementFilter.Enabled && !mod.IsEnabled)
            return false;

        if (ModFilter is ModManagementFilter.Disabled && mod.IsEnabled)
            return false;

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

    private bool ShouldResetForInstanceReference(GameInstance? instance)
    {
        if (selectedInstance is null || instance is null)
            return selectedInstance is not null || instance is not null;

        return !string.Equals(selectedInstance.Id, instance.Id, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(selectedInstance.InstanceDirectory, instance.InstanceDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private bool CanToggleSelectAllMods()
    {
        return IsMultiSelectMode && HasMods;
    }

    private void RaiseAvailabilityPropertyChanges()
    {
        OnPropertyChanged(nameof(IsModManagementSupported));
        OnPropertyChanged(nameof(CanShowModInfoSection));
        OnPropertyChanged(nameof(CanShowModScrollableContent));
        OnPropertyChanged(nameof(HasInstalledMods));
        OnPropertyChanged(nameof(CanShowModListSection));
        OnPropertyChanged(nameof(CanShowNoModsEmptyState));
        OnPropertyChanged(nameof(CanShowModEmptyState));
        OnPropertyChanged(nameof(CanShowModUnavailableState));
        OnPropertyChanged(nameof(CanShowModLoadingState));
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

        var lastSingleSelectedStablePath = GetStableModPath(lastSingleSelectedModPath);
        var restoredSelection = Mods.FirstOrDefault(mod =>
            string.Equals(GetStableModPath(mod.FullPath), lastSingleSelectedStablePath, StringComparison.OrdinalIgnoreCase));
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

    private void ClearDisplayedMods()
    {
        allModsByStablePath.Clear();
        SetVisibleMods(Array.Empty<ModManagementModItemViewModel>());
        RefreshVisibleModListItems();
        SelectedMod = null;
        RefreshSummary();
        OnPropertyChanged(nameof(HasMods));
        RaiseAvailabilityPropertyChanges();
        OnPropertyChanged(nameof(ModEmptyMessage));
        OnPropertyChanged(nameof(AreAllVisibleModsSelected));
        OnPropertyChanged(nameof(SelectAllButtonText));
        SelectAllModsCommand.NotifyCanExecuteChanged();
        UpdateSelectedModState();
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
        return localModsViewModel.CurrentMods
            .Where(mod => pathSet.Contains(mod.FullPath))
            .ToArray();
    }

    private LocalMod? ResolveLocalMod(string fullPath)
    {
        var stablePath = GetStableModPath(fullPath);
        return localModsViewModel.CurrentMods.FirstOrDefault(mod =>
            string.Equals(mod.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
            ?? localModsViewModel.CurrentMods.FirstOrDefault(mod =>
                string.Equals(GetStableModPath(mod.FullPath), stablePath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 执行多选批量启停并保持选择集合与重命名后的稳定路径一致。
    /// </summary>
    private async Task SetSelectedModsEnabledAsync(bool enabled)
    {
        var selectedMods = ResolveLocalMods(selectedModPaths);
        if (selectedMods.Count == 0)
        {
            UpdateSelectedModState();
            return;
        }

        logger.LogInformation(
            "Changing selected mods enabled state. InstanceId={InstanceId} Count={Count} Enabled={Enabled}",
            selectedInstance?.Id ?? "<none>",
            selectedMods.Count,
            enabled);
        try
        {
            // 批量重命名会产生 watcher 事件；暂时抑制页面回调，最后主动同步一次。
            suppressLocalCollectionEvents = true;
            int failedCount;
            try
            {
                failedCount = await localModsViewModel.SetModsEnabledAsync(selectedMods, enabled);
            }
            finally
            {
                suppressLocalCollectionEvents = false;
            }

            // 文件启停会改变 .disabled 后缀，因此用服务返回后的新路径重建选择集合。
            selectedModPaths.Clear();
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
            suppressLocalCollectionEvents = false;
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

    partial void OnVisibleModsChanged(IReadOnlyList<ModManagementModItemViewModel> value)
    {
        OnPropertyChanged(nameof(Mods));
        RefreshVisibleModListItems();
    }

    private void SetVisibleMods(IReadOnlyList<ModManagementModItemViewModel> mods)
    {
        if (IsSameVisibleMods(mods))
            return;

        VisibleMods = mods;
    }

    private bool IsSameVisibleMods(IReadOnlyList<ModManagementModItemViewModel> mods)
    {
        if (VisibleMods.Count != mods.Count)
            return false;

        for (var index = 0; index < mods.Count; index++)
        {
            if (!ReferenceEquals(VisibleMods[index], mods[index]))
                return false;
        }

        return true;
    }

    private void RefreshVisibleModListItems()
    {
        if (!CanShowModInfoSection)
        {
            if (VisibleModListItems.Count > 0)
                VisibleModListItems = Array.Empty<object>();
            return;
        }

        if (IsSameVisibleModListItems())
            return;

        // ItemsControl 使用“信息面板 + 可选分区标题 + 数据项”的稳定异构结构。
        var hasListSection = localModsViewModel.CurrentMods.Count > 0;
        var items = new object[VisibleMods.Count + (hasListSection ? 2 : 1)];
        items[0] = ModManagementInfoPanelItem.Instance;
        if (hasListSection)
            items[1] = ModManagementListSectionItem.Instance;

        for (var index = 0; index < VisibleMods.Count; index++)
            items[index + (hasListSection ? 2 : 1)] = VisibleMods[index];

        VisibleModListItems = items;
    }

    private bool IsSameVisibleModListItems()
    {
        var hasListSection = localModsViewModel.CurrentMods.Count > 0;
        if (VisibleModListItems.Count != VisibleMods.Count + (hasListSection ? 2 : 1))
            return false;

        if (!ReferenceEquals(VisibleModListItems[0], ModManagementInfoPanelItem.Instance))
            return false;

        if (!hasListSection)
            return true;

        if (!ReferenceEquals(VisibleModListItems[1], ModManagementListSectionItem.Instance))
            return false;

        for (var index = 0; index < VisibleMods.Count; index++)
        {
            if (!ReferenceEquals(VisibleModListItems[index + 2], VisibleMods[index]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// 将导入重名冲突转换为可绑定对话框状态，并异步等待用户选择。
    /// </summary>
    private async Task<bool> RequestModImportConflictResolutionAsync(string sourcePath, string fileName)
    {
        // RunContinuationsAsynchronously 防止按钮事件处理器同步恢复导入循环并形成 UI 重入。
        pendingImportConflictResolutionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        ImportModConflictRequested?.Invoke(new ModImportConflictRequest(sourcePath, fileName));
        return await pendingImportConflictResolutionSource.Task;
    }

    private void ResolvePendingImportConflict(bool shouldReplace)
    {
        pendingImportConflictResolutionSource?.TrySetResult(shouldReplace);
        pendingImportConflictResolutionSource = null;
    }

    private bool TryValidateImportPaths(
        IReadOnlyList<string> paths,
        string invalidTypeMessage,
        out string failureMessage)
    {
        var validation = importPathValidator.Validate(paths, InstanceContentImportKind.Mod);
        failureMessage = validation.Failure is InstanceContentImportPathFailure.DirectoryNotSupported
            ? Strings.GameSettings_DropFoldersUnsupportedMessage
            : validation.IsValid ? string.Empty : invalidTypeMessage;
        return validation.IsValid;
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

    private static string GetStableModPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        return path.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)
            ? path[..^".disabled".Length]
            : path;
    }
}

public sealed class ModManagementInfoPanelItem
{
    public static ModManagementInfoPanelItem Instance { get; } = new();

    private ModManagementInfoPanelItem()
    {
    }
}

public sealed class ModManagementListSectionItem
{
    public static ModManagementListSectionItem Instance { get; } = new();

    private ModManagementListSectionItem()
    {
    }
}

public enum ModManagementFilter
{
    All,
    Enabled,
    Disabled
}
