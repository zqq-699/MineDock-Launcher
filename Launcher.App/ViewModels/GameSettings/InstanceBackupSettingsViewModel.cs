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
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.GameSettings;

/// <summary>
/// 管理实例备份的创建、筛选、多选删除、目录切换和带回滚的恢复确认流程。
/// </summary>
public sealed partial class InstanceBackupSettingsViewModel : GameSettingsDetailsSectionViewModelBase
{
    // 服务负责持久化和文件操作；本类只编排对话框、命令可用性和列表投影。
    private readonly IGameInstanceService instanceService;
    private readonly IInstanceBackupService backupService;
    private readonly IStatusService statusService;
    private readonly IInstanceFolderService instanceFolderService;
    private readonly IFilePickerService filePickerService;
    private readonly IFloatingMessageService floatingMessageService;
    private readonly ILogger<InstanceBackupSettingsViewModel> logger;
    // 使用路径保存多选身份，刷新后可将选择重新映射到新建的备份项 ViewModel。
    private readonly HashSet<string> selectedBackupPaths = new(StringComparer.OrdinalIgnoreCase);
    private GameInstance? selectedInstance;
    private IReadOnlyList<InstanceBackupItemViewModel> pendingDeleteBackups = Array.Empty<InstanceBackupItemViewModel>();
    private InstanceBackupItemViewModel? pendingRestoreBackup;
    private IReadOnlyList<InstanceBackupItemViewModel> allBackups = Array.Empty<InstanceBackupItemViewModel>();
    // 每次目录或实例变化都会推进 token，阻止较慢的旧目录读取覆盖当前列表。
    private int refreshToken;

    // 下面的属性共同描述页面状态机：加载、创建、恢复和三个互斥确认对话框。
    [ObservableProperty]
    private int backupCount;

    [ObservableProperty]
    private string backupDirectory = string.Empty;

    [ObservableProperty]
    private string backupSearchQuery = string.Empty;

    [ObservableProperty]
    private bool isMultiSelectMode;

    [ObservableProperty]
    private int selectedBackupCount;

    [ObservableProperty]
    private bool isLoadingBackups;

    [ObservableProperty]
    private bool hasLoadedBackups;

    [ObservableProperty]
    private bool isCreatingBackup;

    [ObservableProperty]
    private bool isRestoringBackup;

    [ObservableProperty]
    private bool isCreateBackupDialogOpen;

    [ObservableProperty]
    private string newBackupName = string.Empty;

    [ObservableProperty]
    private bool isBackupFailureDialogOpen;

    [ObservableProperty]
    private string backupFailureDialogMessage = string.Empty;

    [ObservableProperty]
    private bool isDeleteBackupDialogOpen;

    [ObservableProperty]
    private bool isRestoreBackupDialogOpen;

    [ObservableProperty]
    private int listEntranceAnimationToken;

    [ObservableProperty]
    private IReadOnlyList<InstanceBackupItemViewModel> visibleBackups = Array.Empty<InstanceBackupItemViewModel>();

    [ObservableProperty]
    private IReadOnlyList<object> visibleBackupListItems = Array.Empty<object>();

    public InstanceBackupSettingsViewModel(
        GameSettingsDetailsViewModel parent,
        IGameInstanceService instanceService,
        IInstanceBackupService backupService,
        IStatusService statusService,
        IInstanceFolderService instanceFolderService,
        IFilePickerService filePickerService,
        IFloatingMessageService floatingMessageService,
        ILogger<InstanceBackupSettingsViewModel>? logger = null)
        : base(parent)
    {
        this.instanceService = instanceService;
        this.backupService = backupService;
        this.statusService = statusService;
        this.instanceFolderService = instanceFolderService;
        this.filePickerService = filePickerService;
        this.floatingMessageService = floatingMessageService;
        this.logger = logger ?? NullLogger<InstanceBackupSettingsViewModel>.Instance;
    }

    public string BackupInfoText => string.Format(Strings.GameSettings_BackupInfoSummaryFormat, BackupCount);

    public override bool UsesFullViewportLayout => true;

    public string BackupDirectoryText => string.IsNullOrWhiteSpace(BackupDirectory)
        ? Strings.GameSettings_BackupDirectoryNotSelected
        : BackupDirectory;

    public bool CanOpenBackupDirectory => !string.IsNullOrWhiteSpace(BackupDirectory);

    public bool CanCreateBackupNow => selectedInstance is not null
        && !IsCreatingBackup
        && !IsRestoringBackup
        && !string.IsNullOrWhiteSpace(BackupDirectory);

    public bool CanConfirmCreateBackupDialog => !IsCreatingBackup && !IsRestoringBackup && !string.IsNullOrWhiteSpace(NewBackupName);

    public bool CanRestoreBackup => selectedInstance is not null
        && !IsCreatingBackup
        && !IsRestoringBackup
        && !string.IsNullOrWhiteSpace(BackupDirectory);

    public bool CanShowBackupScrollableContent => selectedInstance is not null;

    public bool HasVisibleBackups => VisibleBackups.Count > 0;

    public bool HasSelectedBackups => SelectedBackupCount > 0;

    public bool AreAllVisibleBackupsSelected => HasVisibleBackups && SelectedBackupCount == VisibleBackups.Count;

    public bool CanShowBackupLoadingState => selectedInstance is not null && IsLoadingBackups && !HasLoadedBackups;

    public bool CanShowBackupEmptyState => selectedInstance is not null
        && HasLoadedBackups
        && !IsLoadingBackups
        && !HasVisibleBackups
        && !string.IsNullOrWhiteSpace(BackupDirectory);

    public string BackupEmptyMessage => string.IsNullOrWhiteSpace(BackupSearchQuery)
        ? Strings.GameSettings_BackupEmpty
        : Strings.GameSettings_BackupSearchEmpty;

    public string SelectAllButtonText => AreAllVisibleBackupsSelected
        ? Strings.GameSettings_BackupCancelSelectAllButton
        : Strings.GameSettings_BackupSelectAllButton;

    public string DeleteBackupDialogMessage => pendingDeleteBackups.Count switch
    {
        0 => string.Empty,
        1 => string.Format(Strings.Dialog_DeleteBackupMessageFormat, pendingDeleteBackups[0].Title),
        _ => string.Format(Strings.Dialog_DeleteMultipleBackupsMessageFormat, pendingDeleteBackups.Count)
    };

    public string RestoreBackupDialogMessage => pendingRestoreBackup is null
        ? string.Empty
        : string.Format(Strings.Dialog_RestoreBackupMessageFormat, pendingRestoreBackup.Title);

    /// <summary>
    /// 切换备份所属实例并清除所有对话框、选择和旧实例加载状态。
    /// </summary>
    public override void OnSelectedInstanceChanged(GameInstance? instance)
    {
        selectedInstance = instance;
        pendingDeleteBackups = Array.Empty<InstanceBackupItemViewModel>();
        pendingRestoreBackup = null;
        selectedBackupPaths.Clear();
        BackupDirectory = instance?.BackupDirectory ?? string.Empty;
        BackupCount = 0;
        SelectedBackupCount = 0;
        allBackups = Array.Empty<InstanceBackupItemViewModel>();
        VisibleBackups = Array.Empty<InstanceBackupItemViewModel>();
        HasLoadedBackups = false;
        IsLoadingBackups = false;
        IsCreateBackupDialogOpen = false;
        IsBackupFailureDialogOpen = false;
        IsDeleteBackupDialogOpen = false;
        IsRestoreBackupDialogOpen = false;
        NewBackupName = string.Empty;
        IsMultiSelectMode = false;
        ListEntranceAnimationToken = 0;
        RefreshVisibleBackupItems();
        OnPropertyChanged(nameof(CanShowBackupScrollableContent));
        OpenBackupFolderCommand.NotifyCanExecuteChanged();
        CreateBackupNowCommand.NotifyCanExecuteChanged();
        RequestRestoreBackupCommand.NotifyCanExecuteChanged();
        _ = RefreshBackupsAsync();
    }

    public override Task OnSectionActivatedAsync()
    {
        return RefreshBackupsAsync();
    }

    [RelayCommand(CanExecute = nameof(CanOpenBackupDirectory))]
    private async Task OpenBackupFolderAsync()
    {
        if (selectedInstance is null || string.IsNullOrWhiteSpace(BackupDirectory))
            return;

        try
        {
            var normalizedDirectory = await backupService.EnsureBackupDirectoryAsync(BackupDirectory);
            BackupDirectory = normalizedDirectory;
            selectedInstance.BackupDirectory = normalizedDirectory;

            logger.LogInformation(
                "Opening instance backup folder. InstanceId={InstanceId} BackupDirectory={BackupDirectory}",
                selectedInstance.Id,
                normalizedDirectory);

            if (!instanceFolderService.TryOpen(normalizedDirectory))
            {
                logger.LogWarning(
                    "Failed to open instance backup folder. InstanceId={InstanceId} BackupDirectory={BackupDirectory}",
                    selectedInstance.Id,
                    normalizedDirectory);
                statusService.Report(Strings.Status_OpenBackupDirectoryFailed);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to prepare instance backup folder for opening. InstanceId={InstanceId}",
                selectedInstance.Id);
            statusService.Report(Strings.Status_OpenBackupDirectoryFailed);
        }
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

    [RelayCommand(CanExecute = nameof(CanToggleSelectAllBackups))]
    private void SelectAllBackups()
    {
        if (AreAllVisibleBackupsSelected)
        {
            ClearVisibleSelections();
            selectedBackupPaths.Clear();
            UpdateSelectedBackupState();
            return;
        }

        foreach (var backup in VisibleBackups)
        {
            backup.IsSelected = true;
            selectedBackupPaths.Add(backup.FullPath);
        }

        UpdateSelectedBackupState();
    }

    [RelayCommand(CanExecute = nameof(CanCreateBackupNow))]
    private void CreateBackupNow()
    {
        if (!CanCreateBackupNow)
            return;

        NewBackupName = string.Format(
            Strings.GameSettings_BackupDefaultNameFormat,
            DateTimeOffset.Now.ToString("yyyy-MM-dd HH-mm"));
        IsCreateBackupDialogOpen = true;
        logger.LogInformation(
            "Instance backup naming dialog opened. InstanceId={InstanceId}",
            selectedInstance?.Id ?? "<none>");
    }

    /// <summary>
    /// 创建备份并在成功后刷新清单；失败时保留友好错误和诊断摘要。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanConfirmCreateBackupDialog))]
    private async Task ConfirmCreateBackupDialogAsync()
    {
        if (selectedInstance is null || string.IsNullOrWhiteSpace(BackupDirectory) || string.IsNullOrWhiteSpace(NewBackupName))
            return;

        // 先关闭命名对话框并锁定创建命令，防止用户重复提交同一备份。
        var backupName = NewBackupName.Trim();
        IsCreateBackupDialogOpen = false;
        IsCreatingBackup = true;
        floatingMessageService.Show(Strings.Status_BackupCreating);

        try
        {
            // 只有归档和清单都提交成功后才刷新页面；服务内部会清理临时文件。
            await backupService.CreateBackupAsync(selectedInstance, BackupDirectory, backupName);
            await RefreshBackupsAsync();
            floatingMessageService.Show(Strings.Status_BackupCreated);
        }
        catch (Exception exception)
        {
            // 用户看到本地化原因和简短诊断，完整异常仍保留在日志中。
            logger.LogError(
                exception,
                "Failed to create instance backup from backup settings page. InstanceId={InstanceId}",
                selectedInstance.Id);
            BackupFailureDialogMessage = string.Format(
                Strings.Dialog_BackupCreateFailedMessageFormat,
                GetFriendlyBackupFailureMessage(exception),
                GetExceptionSummary(exception));
            IsBackupFailureDialogOpen = true;
        }
        finally
        {
            IsCreatingBackup = false;
        }
    }

    [RelayCommand]
    private void CancelCreateBackupDialog()
    {
        IsCreateBackupDialogOpen = false;
    }

    [RelayCommand]
    private void CloseBackupFailureDialog()
    {
        IsBackupFailureDialogOpen = false;
    }

    [RelayCommand]
    private void OpenBackupLocation(InstanceBackupItemViewModel? backup)
    {
        if (backup is null)
            return;

        try
        {
            if (!instanceFolderService.TryRevealFile(backup.FullPath))
            {
                logger.LogWarning(
                    "Failed to reveal instance backup file. InstanceId={InstanceId} BackupFile={BackupFile}",
                    selectedInstance?.Id ?? "<none>",
                    backup.FullPath);
                statusService.Report(Strings.Status_OpenBackupLocationFailed);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to reveal instance backup file. InstanceId={InstanceId} BackupFile={BackupFile}",
                selectedInstance?.Id ?? "<none>",
                backup.FullPath);
            statusService.Report(Strings.Status_OpenBackupLocationFailed);
        }
    }

    [RelayCommand]
    private void RequestDeleteBackup(InstanceBackupItemViewModel? backup)
    {
        if (backup is null)
            return;

        pendingDeleteBackups = [backup];
        OnPropertyChanged(nameof(DeleteBackupDialogMessage));
        IsDeleteBackupDialogOpen = true;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedBackups))]
    private void RequestDeleteSelectedBackups()
    {
        var selectedBackups = GetSelectedVisibleBackups();
        if (selectedBackups.Count == 0)
            return;

        pendingDeleteBackups = selectedBackups;
        OnPropertyChanged(nameof(DeleteBackupDialogMessage));
        IsDeleteBackupDialogOpen = true;
    }

    [RelayCommand]
    private void SelectBackup(InstanceBackupItemViewModel? backup)
    {
        if (backup is null || !IsMultiSelectMode)
            return;

        var isSelected = !backup.IsSelected;
        backup.IsSelected = isSelected;
        if (isSelected)
            selectedBackupPaths.Add(backup.FullPath);
        else
            selectedBackupPaths.Remove(backup.FullPath);

        UpdateSelectedBackupState();
    }

    [RelayCommand(CanExecute = nameof(CanRestoreBackup))]
    private void RequestRestoreBackup(InstanceBackupItemViewModel? backup)
    {
        if (backup is null)
            return;

        pendingRestoreBackup = backup;
        OnPropertyChanged(nameof(RestoreBackupDialogMessage));
        IsRestoreBackupDialogOpen = true;
    }

    [RelayCommand]
    private void CancelRestoreBackup()
    {
        pendingRestoreBackup = null;
        IsRestoreBackupDialogOpen = false;
        OnPropertyChanged(nameof(RestoreBackupDialogMessage));
    }

    /// <summary>
    /// 恢复已确认的备份，并在操作期间锁定其他会改变备份或实例内容的命令。
    /// </summary>
    [RelayCommand]
    private async Task ConfirmRestoreBackupAsync()
    {
        if (selectedInstance is null || pendingRestoreBackup is null || string.IsNullOrWhiteSpace(BackupDirectory))
            return;

        // 快照待恢复项并立即清空对话框状态，后续异步流程不再依赖可变字段。
        var backup = pendingRestoreBackup;
        pendingRestoreBackup = null;
        IsRestoreBackupDialogOpen = false;
        OnPropertyChanged(nameof(RestoreBackupDialogMessage));
        IsRestoringBackup = true;
        floatingMessageService.Show(Strings.Status_BackupRestoring);

        try
        {
            // 恢复前强制创建保护备份，即使目标备份损坏也能保留用户当前实例。
            var protectionBackupName = string.Format(
                Strings.GameSettings_BackupPreRestoreNameFormat,
                DateTimeOffset.Now.ToString("yyyy-MM-dd HH-mm"));
            await backupService.CreateBackupAsync(selectedInstance, BackupDirectory, protectionBackupName);
            await backupService.RestoreBackupAsync(selectedInstance, BackupDirectory, backup.FullPath);
            await RefreshBackupsAsync();
            floatingMessageService.Show(Strings.Status_BackupRestored);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to restore instance backup from backup settings page. InstanceId={InstanceId} BackupFile={BackupFile}",
                selectedInstance.Id,
                backup.FullPath);
            statusService.Report(Strings.Status_BackupRestoreFailed);
        }
        finally
        {
            IsRestoringBackup = false;
        }
    }

    [RelayCommand]
    private void CancelDeleteBackup()
    {
        pendingDeleteBackups = Array.Empty<InstanceBackupItemViewModel>();
        IsDeleteBackupDialogOpen = false;
        OnPropertyChanged(nameof(DeleteBackupDialogMessage));
    }

    /// <summary>
    /// 删除单个或多选备份，允许部分失败并在结束后重建可见选择状态。
    /// </summary>
    [RelayCommand]
    private async Task ConfirmDeleteBackupAsync()
    {
        if (pendingDeleteBackups.Count == 0 || string.IsNullOrWhiteSpace(BackupDirectory))
            return;

        // 冻结本次删除集合；关闭对话框后新的选择不会混入正在执行的批次。
        var backups = pendingDeleteBackups;
        pendingDeleteBackups = Array.Empty<InstanceBackupItemViewModel>();
        IsDeleteBackupDialogOpen = false;
        OnPropertyChanged(nameof(DeleteBackupDialogMessage));

        try
        {
            // 清单与文件由服务逐项保持一致；全部完成后再重读目录作为最终事实来源。
            foreach (var backup in backups)
                await backupService.DeleteBackupAsync(BackupDirectory, backup.FullPath);

            if (backups.Count > 1)
                ExitMultiSelectMode();

            await RefreshBackupsAsync();
            statusService.Report(backups.Count == 1
                ? Strings.Status_BackupDeleted
                : string.Format(Strings.Status_SelectedBackupsDeletedFormat, backups.Count));
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to delete instance backup from backup settings page. InstanceId={InstanceId} BackupCount={BackupCount}",
                selectedInstance?.Id ?? "<none>",
                backups.Count);
            statusService.Report(Strings.Status_BackupDeleteFailed);
        }
    }

    [RelayCommand]
    private async Task ChangeBackupDirectoryAsync()
    {
        if (selectedInstance is null)
            return;

        // 文件选择器只负责取得候选路径，规范化和设置持久化在确认后完成。
        var selectedDirectory = filePickerService.PickFolder(
            Strings.FilePicker_BackupDirectoryTitle,
            string.IsNullOrWhiteSpace(BackupDirectory) ? null : BackupDirectory);
        if (string.IsNullOrWhiteSpace(selectedDirectory))
            return;

        // GetFullPath 提前拒绝无效输入，避免保存一个后续每次访问都会失败的路径。
        string normalizedDirectory;
        try
        {
            normalizedDirectory = await backupService.EnsureBackupDirectoryAsync(selectedDirectory);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to change instance backup directory. InstanceId={InstanceId}",
                selectedInstance.Id);
            statusService.Report(Strings.Status_BackupDirectoryChangeFailed);
            return;
        }

        if (string.Equals(BackupDirectory, normalizedDirectory, StringComparison.OrdinalIgnoreCase))
            return;

        var originalDirectory = selectedInstance.BackupDirectory;
        try
        {
            // 先持久化实例设置，再切换页面目录；保存失败时继续显示原目录。
            selectedInstance.BackupDirectory = normalizedDirectory;
            await instanceService.SaveInstanceAsync(selectedInstance);
            BackupDirectory = normalizedDirectory;
            Parent.NotifyInstanceSettingsSaved(selectedInstance);
        }
        catch (Exception exception)
        {
            selectedInstance.BackupDirectory = originalDirectory;
            BackupDirectory = originalDirectory;
            logger.LogError(
                exception,
                "Failed to save instance backup directory. InstanceId={InstanceId}",
                selectedInstance.Id);
            statusService.Report(Strings.Status_BackupDirectoryChangeFailed);
            return;
        }

        statusService.Report(Strings.Status_BackupDirectoryChanged);
        await RefreshBackupsAsync();
    }

    /// <summary>
    /// 从当前备份目录重新读取有效记录，并忽略已切换实例产生的陈旧结果。
    /// </summary>
    private async Task RefreshBackupsAsync()
    {
        // token 与目录字符串双重校验：即使多个目录恰好使用同一 token 流程，也不能跨目录发布。
        var token = ++refreshToken;
        var directory = BackupDirectory;
        if (string.IsNullOrWhiteSpace(directory))
        {
            // 空目录是合法的“尚未配置”状态，清空投影但标记加载完成，避免无限 Loading。
            allBackups = Array.Empty<InstanceBackupItemViewModel>();
            VisibleBackups = Array.Empty<InstanceBackupItemViewModel>();
            selectedBackupPaths.Clear();
            SelectedBackupCount = 0;
            BackupCount = 0;
            HasLoadedBackups = true;
            IsLoadingBackups = false;
            RefreshVisibleBackupItems();
            NotifyBackupListStateChanged();
            return;
        }

        IsLoadingBackups = true;
        try
        {
            var backups = await backupService.GetBackupsAsync(directory);
            // 等待 I/O 期间用户可能更换目录或实例，过期结果必须静默丢弃。
            if (token != refreshToken || !string.Equals(directory, BackupDirectory, StringComparison.OrdinalIgnoreCase))
                return;

            allBackups = backups.Select(backup => new InstanceBackupItemViewModel(backup)).ToArray();
            BackupCount = allBackups.Count;
            HasLoadedBackups = true;
            RefreshVisibleBackupItems();
        }
        catch (Exception exception)
        {
            // 只有当前请求可以展示错误；过期请求失败不应污染新目录页面。
            if (token != refreshToken)
                return;

            logger.LogWarning(
                exception,
                "Failed to refresh instance backups. InstanceId={InstanceId} BackupDirectory={BackupDirectory}",
                selectedInstance?.Id ?? "<none>",
                directory);
            allBackups = Array.Empty<InstanceBackupItemViewModel>();
            VisibleBackups = Array.Empty<InstanceBackupItemViewModel>();
            selectedBackupPaths.Clear();
            SelectedBackupCount = 0;
            BackupCount = 0;
            HasLoadedBackups = true;
            RefreshVisibleBackupItems();
            statusService.Report(Strings.Status_LoadBackupsFailed);
        }
        finally
        {
            if (token == refreshToken)
            {
                IsLoadingBackups = false;
                NotifyBackupListStateChanged();
            }
        }
    }

    /// <summary>
    /// 应用搜索过滤并复用备份项状态，同时维护信息面板和列表分区项。
    /// </summary>
    private void RefreshVisibleBackupItems()
    {
        if (selectedInstance is null)
        {
            VisibleBackups = Array.Empty<InstanceBackupItemViewModel>();
            VisibleBackupListItems = Array.Empty<object>();
            selectedBackupPaths.Clear();
            UpdateSelectedBackupState();
            NotifyBackupListStateChanged();
            return;
        }

        // 搜索只作用于内存快照，不重新读取清单，因此输入响应保持即时。
        var query = BackupSearchQuery.Trim();
        VisibleBackups = string.IsNullOrWhiteSpace(query)
            ? allBackups
            : allBackups
                .Where(backup => backup.Matches(query))
                .ToArray();

        if (IsMultiSelectMode)
            // 不可见项从选择集合移除，确保“删除所选”与当前屏幕一致。
            selectedBackupPaths.IntersectWith(VisibleBackups.Select(backup => backup.FullPath));

        foreach (var backup in allBackups)
            backup.IsSelected = IsMultiSelectMode && selectedBackupPaths.Contains(backup.FullPath);

        UpdateSelectedBackupState();
        // 列表以信息面板、可选分区标题、数据项的固定结构提供给单一 ItemsControl。
        var hasListSection = VisibleBackups.Count > 0;
        var listItems = new object[VisibleBackups.Count + (hasListSection ? 2 : 1)];
        listItems[0] = BackupManagementInfoPanelItem.Instance;
        if (hasListSection)
            listItems[1] = BackupManagementListSectionItem.Instance;

        for (var index = 0; index < VisibleBackups.Count; index++)
            listItems[index + (hasListSection ? 2 : 1)] = VisibleBackups[index];

        VisibleBackupListItems = listItems;
        if (selectedInstance is not null)
            ListEntranceAnimationToken++;
        NotifyBackupListStateChanged();
    }

    private void NotifyBackupListStateChanged()
    {
        OnPropertyChanged(nameof(HasVisibleBackups));
        OnPropertyChanged(nameof(AreAllVisibleBackupsSelected));
        OnPropertyChanged(nameof(SelectAllButtonText));
        OnPropertyChanged(nameof(CanShowBackupLoadingState));
        OnPropertyChanged(nameof(CanShowBackupEmptyState));
        OnPropertyChanged(nameof(BackupEmptyMessage));
        SelectAllBackupsCommand.NotifyCanExecuteChanged();
    }

    private bool CanToggleSelectAllBackups()
    {
        return IsMultiSelectMode && HasVisibleBackups;
    }

    private void EnterMultiSelectMode()
    {
        IsMultiSelectMode = true;
        selectedBackupPaths.Clear();
        ClearVisibleSelections();
        UpdateSelectedBackupState();
    }

    private void ExitMultiSelectMode()
    {
        IsMultiSelectMode = false;
        selectedBackupPaths.Clear();
        ClearVisibleSelections();
        UpdateSelectedBackupState();
    }

    private void ClearVisibleSelections()
    {
        foreach (var backup in VisibleBackups)
            backup.IsSelected = false;
    }

    private IReadOnlyList<InstanceBackupItemViewModel> GetSelectedVisibleBackups()
    {
        return VisibleBackups.Where(backup => selectedBackupPaths.Contains(backup.FullPath)).ToArray();
    }

    private void UpdateSelectedBackupState()
    {
        SelectedBackupCount = VisibleBackups.Count(backup => backup.IsSelected);
    }

    private static string GetFriendlyBackupFailureMessage(Exception exception)
    {
        return exception is InstanceBackupException backupException
            ? backupException.Reason switch
            {
                InstanceBackupFailureReason.BackupDirectoryInsideInstance => Strings.BackupFailure_BackupDirectoryInsideInstance,
                InstanceBackupFailureReason.InstanceDirectoryNotFound => Strings.BackupFailure_InstanceDirectoryMissing,
                _ => Strings.BackupFailure_Generic
            }
            : Strings.BackupFailure_Generic;
    }

    private static string GetExceptionSummary(Exception exception)
    {
        return $"{exception.GetType().Name}: {exception.Message}";
    }

    partial void OnBackupCountChanged(int value)
    {
        OnPropertyChanged(nameof(BackupInfoText));
    }

    partial void OnBackupDirectoryChanged(string value)
    {
        OnPropertyChanged(nameof(BackupDirectoryText));
        OnPropertyChanged(nameof(CanOpenBackupDirectory));
        OnPropertyChanged(nameof(CanCreateBackupNow));
        OnPropertyChanged(nameof(CanRestoreBackup));
        OpenBackupFolderCommand.NotifyCanExecuteChanged();
        CreateBackupNowCommand.NotifyCanExecuteChanged();
        RequestRestoreBackupCommand.NotifyCanExecuteChanged();
    }

    partial void OnBackupSearchQueryChanged(string value)
    {
        RefreshVisibleBackupItems();
    }

    partial void OnVisibleBackupsChanged(IReadOnlyList<InstanceBackupItemViewModel> value)
    {
        NotifyBackupListStateChanged();
    }

    partial void OnSelectedBackupCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasSelectedBackups));
        OnPropertyChanged(nameof(AreAllVisibleBackupsSelected));
        OnPropertyChanged(nameof(SelectAllButtonText));
        SelectAllBackupsCommand.NotifyCanExecuteChanged();
        RequestDeleteSelectedBackupsCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsMultiSelectModeChanged(bool value)
    {
        OnPropertyChanged(nameof(AreAllVisibleBackupsSelected));
        OnPropertyChanged(nameof(SelectAllButtonText));
        SelectAllBackupsCommand.NotifyCanExecuteChanged();
        RequestDeleteSelectedBackupsCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsLoadingBackupsChanged(bool value)
    {
        NotifyBackupListStateChanged();
    }

    partial void OnHasLoadedBackupsChanged(bool value)
    {
        NotifyBackupListStateChanged();
    }

    partial void OnIsCreatingBackupChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCreateBackupNow));
        OnPropertyChanged(nameof(CanConfirmCreateBackupDialog));
        OnPropertyChanged(nameof(CanRestoreBackup));
        CreateBackupNowCommand.NotifyCanExecuteChanged();
        ConfirmCreateBackupDialogCommand.NotifyCanExecuteChanged();
        RequestRestoreBackupCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsRestoringBackupChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCreateBackupNow));
        OnPropertyChanged(nameof(CanConfirmCreateBackupDialog));
        OnPropertyChanged(nameof(CanRestoreBackup));
        CreateBackupNowCommand.NotifyCanExecuteChanged();
        ConfirmCreateBackupDialogCommand.NotifyCanExecuteChanged();
        RequestRestoreBackupCommand.NotifyCanExecuteChanged();
    }

    partial void OnNewBackupNameChanged(string value)
    {
        OnPropertyChanged(nameof(CanConfirmCreateBackupDialog));
        ConfirmCreateBackupDialogCommand.NotifyCanExecuteChanged();
    }
}

public sealed class BackupManagementInfoPanelItem
{
    public static BackupManagementInfoPanelItem Instance { get; } = new();

    private BackupManagementInfoPanelItem()
    {
    }
}

public sealed class BackupManagementListSectionItem
{
    public static BackupManagementListSectionItem Instance { get; } = new();

    private BackupManagementListSectionItem()
    {
    }
}

public sealed partial class InstanceBackupItemViewModel : ObservableObject
{
    public InstanceBackupItemViewModel(InstanceBackupRecord backup)
    {
        Title = backup.Name;
        FileName = backup.FileName;
        FullPath = backup.FullPath;
        SizeBytes = backup.SizeBytes;
        CreatedAt = backup.CreatedAt;
    }

    public string TrailingText => CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public string Subtitle => string.Format(Strings.GameSettings_BackupItemSubtitleFormat, FileName, SizeBytes / 1024d / 1024d);

    public string IconKey => "instance_setting_page/backup";

    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private string fileName = string.Empty;

    [ObservableProperty]
    private string fullPath = string.Empty;

    [ObservableProperty]
    private long sizeBytes;

    [ObservableProperty]
    private DateTimeOffset createdAt;

    [ObservableProperty]
    private bool isSelected;

    public bool Matches(string query)
    {
        return Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || FileName.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    partial void OnFileNameChanged(string value)
    {
        OnPropertyChanged(nameof(Subtitle));
    }

    partial void OnSizeBytesChanged(long value)
    {
        OnPropertyChanged(nameof(Subtitle));
    }

    partial void OnCreatedAtChanged(DateTimeOffset value)
    {
        OnPropertyChanged(nameof(TrailingText));
    }
}
