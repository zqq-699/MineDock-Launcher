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

namespace Launcher.App.ViewModels.GameSettings;

public sealed partial class GameSettingsDetailsViewModel : ObservableObject, IDisposable
{
    private readonly InstanceSettingsPersistenceCoordinator persistence;

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
        ILogger logger,
        IModpackExportService? modpackExportService = null)
    {
        persistence = new InstanceSettingsPersistenceCoordinator(
            instanceService,
            statusService,
            uiDispatcher,
            logger);
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
            importPathValidator);
        ModManagement.DeleteModsRequested += ModManagement_DeleteModsRequested;
        ModManagement.ImportModConflictRequested += ModManagement_ImportModConflictRequested;
        ModManagement.OnlineModInstallRequested += ModManagement_OnlineModInstallRequested;
        SaveManagement = new InstanceSaveManagementSettingsViewModel(
            this,
            localSavesViewModel,
            statusService,
            instanceFolderService,
            filePickerService,
            importPathValidator);
        SaveManagement.DeleteSavesRequested += SaveManagement_DeleteSavesRequested;
        SaveManagement.SaveImportFailedRequested += SaveManagement_SaveImportFailedRequested;
        ResourcePackManagement = new InstanceResourcePackManagementSettingsViewModel(
            this,
            localResourcePacksViewModel,
            statusService,
            instanceFolderService,
            filePickerService,
            importPathValidator);
        ResourcePackManagement.DeleteResourcePacksRequested += ResourcePackManagement_DeleteResourcePacksRequested;
        ResourcePackManagement.ResourcePackImportFailedRequested += ResourcePackManagement_ResourcePackImportFailedRequested;
        ShaderPackManagement = new InstanceShaderPackManagementSettingsViewModel(
            this,
            localShaderPacksViewModel,
            statusService,
            instanceFolderService,
            filePickerService,
            importPathValidator);
        ShaderPackManagement.DeleteShaderPacksRequested += ShaderPackManagement_DeleteShaderPacksRequested;
        ShaderPackManagement.ShaderPackImportFailedRequested += ShaderPackManagement_ShaderPackImportFailedRequested;
        Backup = new InstanceBackupSettingsViewModel(
            this,
            instanceService,
            backupService,
            statusService,
            instanceFolderService,
            filePickerService,
            floatingMessageService);
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
        if (CurrentSectionViewModel is not null)
            _ = CurrentSectionViewModel.OnSectionActivatedAsync();
    }

    partial void OnCurrentSectionViewModelChanged(GameSettingsDetailsSectionViewModelBase? value)
    {
        OnPropertyChanged(nameof(ScrollSectionViewModel));
        OnPropertyChanged(nameof(FullViewportSectionViewModel));
    }

    private void ApplySelectedInstanceChanged(GameSettingsInstanceItem? value)
    {
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

        if (CurrentSectionViewModel is not null)
            _ = CurrentSectionViewModel.OnSectionActivatedAsync();
    }

    private void RefreshSelectedInstanceReference(GameSettingsInstanceItem? value)
    {
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

        if (shouldReactivateCurrentSection && CurrentSectionViewModel is not null)
            _ = CurrentSectionViewModel.OnSectionActivatedAsync();
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
