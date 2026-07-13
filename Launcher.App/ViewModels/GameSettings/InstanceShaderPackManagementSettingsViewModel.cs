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
/// 管理实例光影包的目录监听、导入、筛选、多选与批量删除状态。
/// </summary>
public sealed partial class InstanceShaderPackManagementSettingsViewModel : GameSettingsDetailsSectionViewModelBase
{
    // LocalShaderPacksViewModel 是目录内容的唯一来源；本类只保存筛选、选择和页面生命周期状态。
    private readonly LocalShaderPacksViewModel localShaderPacksViewModel;
    private readonly IStatusService statusService;
    private readonly IInstanceFolderService instanceFolderService;
    private readonly IFilePickerService filePickerService;
    private readonly IInstanceContentImportPathValidator importPathValidator;
    private readonly IUiDispatcher uiDispatcher;
    private readonly ILogger<InstanceShaderPackManagementSettingsViewModel> logger;
    private readonly LocalContentSelectionState<ShaderPackManagementItemViewModel> selectionState;
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

    public bool CanShowShaderPackScrollableContent => selectedInstance is not null && isInitialProjectionReady;

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

    /// <summary>
    /// 切换光影包实例上下文，并使旧实例的加载、选择和视觉刷新失效。
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
        SetInitialProjectionReady(false);
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
        // 页面不可见时关闭 watcher，激活后再补做被合并的刷新并恢复动画。
        isSectionActive = true;
        localShaderPacksViewModel.SetWatcherEnabled(selectedInstance is not null);
        if (hasPendingVisualRefresh && HasLoadedShaderPacks)
            PublishReadyProjection();

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
}
