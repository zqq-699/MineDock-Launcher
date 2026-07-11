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
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.GameSettings;

/// <summary>
/// 组合实例设置各分区，维护统一实例选择，并路由导入、删除和在线安装等跨分区事件。
/// </summary>
public sealed partial class GameSettingsDetailsViewModel : ObservableObject, IDisposable
{
    // 每个分区拥有独立状态和服务依赖；聚合层只同步实例引用与当前分区生命周期。
    private readonly InstanceSettingsPersistenceCoordinator persistence;
    private readonly ILogger logger;

    [ObservableProperty]
    private GameSettingsInstanceItem? selectedInstance;

    [ObservableProperty]
    private GameSettingsDetailSectionItem? selectedSection;

    [ObservableProperty]
    private GameSettingsDetailsSectionViewModelBase? currentSectionViewModel;

    public GameSettingsDetailsViewModel(
        GameSettingsEditDialogViewModel editDialog,
        IGameInstanceService instanceService,
        IStatusService statusService,
        IInstanceFolderService instanceFolderService,
        ISystemMemoryService systemMemoryService,
        IModService modService,
        IInstanceBackupService backupService,
        LocalModsViewModel localModsViewModel,
        LocalSavesViewModel localSavesViewModel,
        LocalResourcePacksViewModel localResourcePacksViewModel,
        LocalShaderPacksViewModel localShaderPacksViewModel,
        IJavaRuntimeDiscoveryService javaRuntimeDiscoveryService,
        IFilePickerService filePickerService,
        IInstanceContentImportPathValidator importPathValidator,
        IFloatingMessageService floatingMessageService,
        IUiDispatcher uiDispatcher,
        ILogger<GameSettingsDetailsViewModel>? logger = null,
        ILoggerFactory? loggerFactory = null,
        IModpackExportService? modpackExportService = null)
    {
        var resolvedLogger = logger ?? NullLogger<GameSettingsDetailsViewModel>.Instance;
        this.logger = resolvedLogger;
        persistence = new InstanceSettingsPersistenceCoordinator(
            instanceService,
            statusService,
            uiDispatcher,
            resolvedLogger);
        persistence.InstanceSaved += Persistence_InstanceSaved;

        General = new InstanceGeneralSettingsViewModel(
            editDialog,
            instanceFolderService,
            statusService,
            persistence);
        General.DeleteInstanceRequested += General_DeleteInstanceRequested;
        Launch = new InstanceLaunchSettingsViewModel(systemMemoryService, modService, persistence);
        Java = new InstanceJavaSettingsViewModel(
            persistence,
            javaRuntimeDiscoveryService,
            statusService,
            filePickerService,
            floatingMessageService);
        ModManagement = new InstanceModManagementSettingsViewModel(
            this,
            localModsViewModel,
            statusService,
            instanceFolderService,
            filePickerService,
            importPathValidator,
            logger: loggerFactory?.CreateLogger<InstanceModManagementSettingsViewModel>());
        ModManagement.DeleteModsRequested += ModManagement_DeleteModsRequested;
        ModManagement.ImportModConflictRequested += ModManagement_ImportModConflictRequested;
        ModManagement.OnlineModInstallRequested += ModManagement_OnlineModInstallRequested;
        SaveManagement = new InstanceSaveManagementSettingsViewModel(
            this,
            localSavesViewModel,
            statusService,
            instanceFolderService,
            filePickerService,
            importPathValidator,
            logger: loggerFactory?.CreateLogger<InstanceSaveManagementSettingsViewModel>());
        SaveManagement.DeleteSavesRequested += SaveManagement_DeleteSavesRequested;
        SaveManagement.SaveImportFailedRequested += SaveManagement_SaveImportFailedRequested;
        ResourcePackManagement = new InstanceResourcePackManagementSettingsViewModel(
            this,
            localResourcePacksViewModel,
            statusService,
            instanceFolderService,
            filePickerService,
            importPathValidator,
            logger: loggerFactory?.CreateLogger<InstanceResourcePackManagementSettingsViewModel>());
        ResourcePackManagement.DeleteResourcePacksRequested += ResourcePackManagement_DeleteResourcePacksRequested;
        ResourcePackManagement.ResourcePackImportFailedRequested += ResourcePackManagement_ResourcePackImportFailedRequested;
        ShaderPackManagement = new InstanceShaderPackManagementSettingsViewModel(
            this,
            localShaderPacksViewModel,
            statusService,
            instanceFolderService,
            filePickerService,
            importPathValidator,
            logger: loggerFactory?.CreateLogger<InstanceShaderPackManagementSettingsViewModel>());
        ShaderPackManagement.DeleteShaderPacksRequested += ShaderPackManagement_DeleteShaderPacksRequested;
        ShaderPackManagement.ShaderPackImportFailedRequested += ShaderPackManagement_ShaderPackImportFailedRequested;
        Backup = new InstanceBackupSettingsViewModel(
            this,
            instanceService,
            backupService,
            statusService,
            instanceFolderService,
            filePickerService,
            floatingMessageService,
            logger: loggerFactory?.CreateLogger<InstanceBackupSettingsViewModel>());
        Export = new InstanceExportSettingsViewModel(
            this,
            filePickerService,
            statusService,
            floatingMessageService,
            modpackExportService);
        Placeholder = new InstancePlaceholderSettingsViewModel(this);
        CurrentSectionViewModel = General;
    }

    public event Action<GameInstance>? InstanceSettingsSaved;

    public event Action<GameSettingsInstanceItem>? DeleteInstanceRequested;

    public event Action<ModDeleteRequest>? DeleteModsRequested;
    public event Action<SaveDeleteRequest>? DeleteSavesRequested;
    public event Action<ModImportConflictRequest>? ImportModConflictRequested;
    public event Action<GameInstance>? OnlineModInstallRequested;
    public event Action<SaveImportFailureRequest>? SaveImportFailedRequested;
    public event Action<ResourcePackDeleteRequest>? DeleteResourcePacksRequested;
    public event Action<ResourcePackImportFailureRequest>? ResourcePackImportFailedRequested;
    public event Action<ShaderPackDeleteRequest>? DeleteShaderPacksRequested;
    public event Action<ShaderPackImportFailureRequest>? ShaderPackImportFailedRequested;

    public bool HasSelectedInstance => SelectedInstance is not null;

    public string SectionTitle => SelectedSection?.Title ?? Strings.GameSettings_DetailGeneral;

    public string SectionPlaceholderBody => string.Format(
        Strings.GameSettings_DetailPlaceholderBodyFormat,
        SectionTitle);

    public GameSettingsDetailsSectionViewModelBase? ScrollSectionViewModel =>
        CurrentSectionViewModel?.UsesFullViewportLayout is true ? null : CurrentSectionViewModel;

    public GameSettingsDetailsSectionViewModelBase? FullViewportSectionViewModel =>
        CurrentSectionViewModel?.UsesFullViewportLayout is true ? CurrentSectionViewModel : null;

    public bool IsGeneralSection => string.Equals(SelectedSection?.Id, "general", StringComparison.OrdinalIgnoreCase);

    public bool IsLaunchSection => string.Equals(SelectedSection?.Id, "launch", StringComparison.OrdinalIgnoreCase);

    public bool IsJavaSection => string.Equals(SelectedSection?.Id, "java", StringComparison.OrdinalIgnoreCase);

    public InstanceGeneralSettingsViewModel General { get; }

    public InstanceLaunchSettingsViewModel Launch { get; }

    public InstanceJavaSettingsViewModel Java { get; }

    public InstanceModManagementSettingsViewModel ModManagement { get; }

    public InstanceSaveManagementSettingsViewModel SaveManagement { get; }

    public InstanceResourcePackManagementSettingsViewModel ResourcePackManagement { get; }

    public InstanceShaderPackManagementSettingsViewModel ShaderPackManagement { get; }

    public InstanceBackupSettingsViewModel Backup { get; }

    public InstanceExportSettingsViewModel Export { get; }

    public InstancePlaceholderSettingsViewModel Placeholder { get; }

    public void PrimeFromSettings(LauncherSettings launcherSettings)
    {
        Launch.PrimeFromSettings(launcherSettings);
        Java.PrimeFromSettings(launcherSettings);
    }

    public void SetSelectedInstance(GameSettingsInstanceItem? instance)
    {
        // 选择变更集中应用给所有分区，避免隐藏分区仍观察上一实例目录。
        var previousInstance = SelectedInstance;
        SelectedInstance = instance;
        if (ReferenceEquals(previousInstance, instance))
            RefreshSelectedInstanceReference(instance);
    }

    public void SetSelectedSection(GameSettingsDetailSectionItem? section)
    {
        SelectedSection = section;
    }

    public void NotifyInstanceSettingsSaved(GameInstance instance)
    {
        InstanceSettingsSaved?.Invoke(instance);
    }

    public void SuspendLocalWatchersForInstanceRename()
    {
        // 重命名会短暂移动实例目录，暂停 watcher 可避免把事务中间态解释为内容删除。
        ModManagement.SuspendLocalWatchersForInstanceRename();
        SaveManagement.SuspendLocalWatchersForInstanceRename();
        ResourcePackManagement.SuspendLocalWatchersForInstanceRename();
        ShaderPackManagement.SuspendLocalWatchersForInstanceRename();
    }

    public void ResumeLocalWatchersAfterInstanceRename()
    {
        ModManagement.ResumeLocalWatchersAfterInstanceRename();
        SaveManagement.ResumeLocalWatchersAfterInstanceRename();
        ResourcePackManagement.ResumeLocalWatchersAfterInstanceRename();
        ShaderPackManagement.ResumeLocalWatchersAfterInstanceRename();
    }

    public Task DeleteModsAsync(IReadOnlyList<string> fullPaths) => ModManagement.DeleteModsAsync(fullPaths);

    public Task DeleteSavesAsync(IReadOnlyList<string> fullPaths) => SaveManagement.DeleteSavesAsync(fullPaths);

    public Task DeleteResourcePacksAsync(IReadOnlyList<string> fullPaths) => ResourcePackManagement.DeleteResourcePacksAsync(fullPaths);

    public Task DeleteShaderPacksAsync(IReadOnlyList<string> fullPaths) => ShaderPackManagement.DeleteShaderPacksAsync(fullPaths);

    public GameSettingsFileDropEvaluation EvaluateImportDrop(IReadOnlyList<string> paths)
    {
        // 由当前分区决定可接受类型，聚合层保证拖放不会路由到隐藏页面。
        if (SelectedInstance is null)
            return GameSettingsFileDropEvaluation.Hidden;

        return SelectedSection?.Id?.ToLowerInvariant() switch
        {
            "mod_management" => ModManagement.EvaluateDroppedFiles(paths),
            "saves" => SaveManagement.EvaluateDroppedFiles(paths),
            "resource_packs" => ResourcePackManagement.EvaluateDroppedFiles(paths),
            "shaders" => ShaderPackManagement.EvaluateDroppedFiles(paths),
            _ => GameSettingsFileDropEvaluation.Hidden
        };
    }

    public Task HandleImportDropAsync(IReadOnlyList<string> paths)
    {
        // 实际文件操作仍由对应分区服务完成，此处只选择目标流程。
        if (SelectedInstance is null)
            return Task.CompletedTask;

        return SelectedSection?.Id?.ToLowerInvariant() switch
        {
            "mod_management" => ModManagement.ImportDroppedModFilesAsync(paths),
            "saves" => SaveManagement.ImportDroppedSaveArchivesAsync(paths),
            "resource_packs" => ResourcePackManagement.ImportDroppedResourcePackArchivesAsync(paths),
            "shaders" => ShaderPackManagement.ImportDroppedShaderPackArchivesAsync(paths),
            _ => Task.CompletedTask
        };
    }

    public void ResolvePendingModImportConflict(bool shouldReplace)
    {
        if (shouldReplace)
            ModManagement.ReplaceImportedModAsync(string.Empty);
        else
            ModManagement.SkipPendingImportedModReplacement();
    }

    public Task ReplaceImportedModAsync(string sourcePath)
    {
        return ModManagement.ReplaceImportedModAsync(sourcePath);
    }

    public void Dispose()
    {
        persistence.InstanceSaved -= Persistence_InstanceSaved;
        General.DeleteInstanceRequested -= General_DeleteInstanceRequested;
        General.Dispose();
        Launch.Dispose();
        Java.Dispose();
        persistence.Dispose();
    }

    partial void OnSelectedInstanceChanged(GameSettingsInstanceItem? value)
    {
        ApplySelectedInstanceChanged(value);
    }

    partial void OnSelectedSectionChanged(GameSettingsDetailSectionItem? value)
    {
        var previousSectionViewModel = CurrentSectionViewModel;
        OnPropertyChanged(nameof(IsGeneralSection));
        OnPropertyChanged(nameof(IsLaunchSection));
        OnPropertyChanged(nameof(IsJavaSection));
        OnPropertyChanged(nameof(SectionTitle));
        OnPropertyChanged(nameof(SectionPlaceholderBody));
        previousSectionViewModel?.OnSectionDeactivated();
        CurrentSectionViewModel = value?.Id?.ToLowerInvariant() switch
        {
            "general" => General,
            "launch" => Launch,
            "java" => Java,
            "mod_management" => ModManagement,
            "saves" => SaveManagement,
            "resource_packs" => ResourcePackManagement,
            "shaders" => ShaderPackManagement,
            "backup" => Backup,
            "export" => Export,
            _ => Placeholder
        };
        ActivateCurrentSection();
    }

    partial void OnCurrentSectionViewModelChanged(GameSettingsDetailsSectionViewModelBase? value)
    {
        OnPropertyChanged(nameof(ScrollSectionViewModel));
        OnPropertyChanged(nameof(FullViewportSectionViewModel));
    }

    private void ApplySelectedInstanceChanged(GameSettingsInstanceItem? value)
    {
        // 先停止旧实例观察，再切换所有子 ViewModel，最后激活当前可见分区。
        var instance = value?.Instance;
        persistence.SetInstance(instance);
        General.SetSelectedInstance(value);
        Launch.SetSelectedInstance(instance);
        Java.SetSelectedInstance(instance);
        ModManagement.OnSelectedInstanceChanged(instance);
        SaveManagement.OnSelectedInstanceChanged(instance);
        ResourcePackManagement.OnSelectedInstanceChanged(instance);
        ShaderPackManagement.OnSelectedInstanceChanged(instance);
        Backup.OnSelectedInstanceChanged(instance);
        Export.OnSelectedInstanceChanged(instance);
        OnPropertyChanged(nameof(HasSelectedInstance));

        ActivateCurrentSection();
    }

    private void RefreshSelectedInstanceReference(GameSettingsInstanceItem? value)
    {
        // 保存后仓储可能返回新对象，按稳定 Id 刷新引用而不是依赖旧对象身份。
        var instance = value?.Instance;
        persistence.SetInstance(instance);
        General.SetSelectedInstance(value);
        Launch.SetSelectedInstance(instance);
        Java.SetSelectedInstance(instance);
        var shouldReactivateCurrentSection =
            ModManagement.RefreshSelectedInstanceReference(instance)
            | SaveManagement.RefreshSelectedInstanceReference(instance)
            | ResourcePackManagement.RefreshSelectedInstanceReference(instance)
            | ShaderPackManagement.RefreshSelectedInstanceReference(instance);
        Backup.OnSelectedInstanceChanged(instance);
        Export.OnSelectedInstanceChanged(instance);

        if (shouldReactivateCurrentSection)
            ActivateCurrentSection();
    }

    private void ActivateCurrentSection()
    {
        if (CurrentSectionViewModel is { } section)
            _ = ObserveSectionActivationAsync(section);
    }

    private async Task ObserveSectionActivationAsync(GameSettingsDetailsSectionViewModelBase section)
    {
        // 激活可能异步刷新；异常只报告状态，不能让 fire-and-forget 任务失去观察。
        try
        {
            await section.OnSectionActivatedAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to activate game settings section. SectionType={SectionType} InstanceId={InstanceId}",
                section.GetType().Name,
                SelectedInstance?.Instance.Id);
        }
    }

    private void Persistence_InstanceSaved(GameInstance instance)
    {
        InstanceSettingsSaved?.Invoke(instance);
    }

    private void General_DeleteInstanceRequested(GameSettingsInstanceItem instance)
    {
        DeleteInstanceRequested?.Invoke(instance);
    }

    private void ModManagement_OnlineModInstallRequested(GameInstance instance)
    {
        OnlineModInstallRequested?.Invoke(instance);
    }

    private void ModManagement_DeleteModsRequested(ModDeleteRequest request)
    {
        DeleteModsRequested?.Invoke(request);
    }

    private void SaveManagement_DeleteSavesRequested(SaveDeleteRequest request)
    {
        DeleteSavesRequested?.Invoke(request);
    }

    private void SaveManagement_SaveImportFailedRequested(SaveImportFailureRequest request)
    {
        SaveImportFailedRequested?.Invoke(request);
    }

    private void ResourcePackManagement_DeleteResourcePacksRequested(ResourcePackDeleteRequest request)
    {
        DeleteResourcePacksRequested?.Invoke(request);
    }

    private void ResourcePackManagement_ResourcePackImportFailedRequested(ResourcePackImportFailureRequest request)
    {
        ResourcePackImportFailedRequested?.Invoke(request);
    }

    private void ShaderPackManagement_DeleteShaderPacksRequested(ShaderPackDeleteRequest request)
    {
        DeleteShaderPacksRequested?.Invoke(request);
    }

    private void ShaderPackManagement_ShaderPackImportFailedRequested(ShaderPackImportFailureRequest request)
    {
        ShaderPackImportFailedRequested?.Invoke(request);
    }

    private void ModManagement_ImportModConflictRequested(ModImportConflictRequest request)
    {
        ImportModConflictRequested?.Invoke(request);
    }
}
