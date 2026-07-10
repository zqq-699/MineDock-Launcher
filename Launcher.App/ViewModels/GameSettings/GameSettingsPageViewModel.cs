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

using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.GameSettings;

public sealed partial class GameSettingsPageViewModel : ObservableObject
{
    private readonly IStatusService statusService;
    private readonly IInstanceFolderService instanceFolderService;
    private readonly IFloatingMessageService floatingMessageService;
    private readonly ILogger<GameSettingsPageViewModel> logger;
    private INotifyPropertyChanged? selectedInstanceNotifier;
    private string lastImportDropHintMessage = string.Empty;

    [ObservableProperty]
    private GameSettingsPageStep currentStep = GameSettingsPageStep.List;

    public GameSettingsPageViewModel(
        GameSettingsInstanceListViewModel instanceList,
        GameSettingsDetailsViewModel details,
        GameSettingsEditDialogViewModel editDialog,
        GameSettingsDialogsViewModel dialogs,
        IStatusService statusService,
        IInstanceFolderService instanceFolderService,
        IFloatingMessageService floatingMessageService,
        ILogger<GameSettingsPageViewModel>? logger = null)
    {
        InstanceList = instanceList;
        Details = details;
        EditDialog = editDialog;
        Dialogs = dialogs;
        this.statusService = statusService;
        this.instanceFolderService = instanceFolderService;
        this.floatingMessageService = floatingMessageService;
        this.logger = logger ?? NullLogger<GameSettingsPageViewModel>.Instance;

        foreach (var section in GameSettingsDetailSectionFactory.Create())
            DetailSections.Add(section);
        SelectDetailsSectionCore(DetailSections.FirstOrDefault());

        InstanceList.PropertyChanged += InstanceList_PropertyChanged;
        EditDialog.InstanceUpdated += EditDialog_InstanceUpdated;
        EditDialog.InstanceRenameStarting += EditDialog_InstanceRenameStarting;
        EditDialog.InstanceRenameFinished += EditDialog_InstanceRenameFinished;
        Dialogs.InstanceDeleted += Dialogs_InstanceDeleted;
        Details.InstanceSettingsSaved += Details_InstanceSettingsSaved;
        Details.DeleteInstanceRequested += Dialogs.OpenDeleteInstance;
        Details.DeleteModsRequested += Dialogs.OpenDeleteMods;
        Details.DeleteSavesRequested += Dialogs.OpenDeleteSaves;
        Details.DeleteResourcePacksRequested += Dialogs.OpenDeleteResourcePacks;
        Details.DeleteShaderPacksRequested += Dialogs.OpenDeleteShaderPacks;
        Details.ImportModConflictRequested += Dialogs.OpenModImportConflict;
        Details.SaveImportFailedRequested += Dialogs.OpenSaveImportFailure;
        Details.ResourcePackImportFailedRequested += Dialogs.OpenResourcePackImportFailure;
        Details.ShaderPackImportFailedRequested += Dialogs.OpenShaderPackImportFailure;
        Details.OnlineModInstallRequested += Details_OnlineModInstallRequested;
        Details.PropertyChanged += Details_PropertyChanged;
        Details.ModManagement.PropertyChanged += ModManagement_PropertyChanged;
        Details.SaveManagement.PropertyChanged += SaveManagement_PropertyChanged;
        Details.ResourcePackManagement.PropertyChanged += ResourcePackManagement_PropertyChanged;
        Details.ShaderPackManagement.PropertyChanged += ShaderPackManagement_PropertyChanged;
        Details.Backup.PropertyChanged += Backup_PropertyChanged;

        HandleSelectedInstanceChanged();
    }

    public event Action<GameInstance>? LaunchInstanceRequested;

    public event Action<GameSettingsInstancesChangedEventArgs>? InstancesChanged;

    public event Action<GameInstance>? OnlineModInstallRequested;

    public GameSettingsInstanceListViewModel InstanceList { get; }

    public GameSettingsDetailsViewModel Details { get; }

    public GameSettingsEditDialogViewModel EditDialog { get; }

    public GameSettingsDialogsViewModel Dialogs { get; }

    public ObservableCollection<GameSettingsDetailSectionItem> DetailSections { get; } = [];

    public bool IsListStep => CurrentStep is GameSettingsPageStep.List;

    public bool IsDetailsStep => CurrentStep is GameSettingsPageStep.Details;

    public System.Collections.IEnumerable CurrentSecondaryMenuItems => IsDetailsStep
        ? DetailSections
        : InstanceList.Categories;

    public string PageTitle => IsDetailsStep && InstanceList.SelectedInstance is not null
        ? InstanceList.SelectedInstance.Name
        : InstanceList.SelectedCategory?.Title ?? Strings.GameSettings_AllCategory;

    public string? PageTitleIconSource => IsDetailsStep
        ? InstanceList.SelectedInstance?.IconSource
        : null;

    public bool IsModManagementDetailsStep => IsDetailsSection("mod_management");

    public bool IsSaveManagementDetailsStep => IsDetailsSection("saves");

    public bool IsResourcePackManagementDetailsStep => IsDetailsSection("resource_packs");

    public bool IsShaderPackManagementDetailsStep => IsDetailsSection("shaders");

    public bool IsBackupManagementDetailsStep => IsDetailsSection("backup");

    public bool IsExportDetailsStep => IsDetailsSection("export");

    public bool IsTopResourceManagementDetailsStep => IsModManagementDetailsStep
        || IsSaveManagementDetailsStep
        || IsResourcePackManagementDetailsStep
        || IsShaderPackManagementDetailsStep
        || IsBackupManagementDetailsStep;

    public bool IsTopSearchVisible => IsListStep || IsTopResourceManagementDetailsStep;

    public string TopSearchQuery
    {
        get
        {
            if (IsModManagementDetailsStep)
                return Details.ModManagement.ModSearchQuery;
            if (IsSaveManagementDetailsStep)
                return Details.SaveManagement.SaveSearchQuery;
            if (IsResourcePackManagementDetailsStep)
                return Details.ResourcePackManagement.ResourcePackSearchQuery;
            if (IsShaderPackManagementDetailsStep)
                return Details.ShaderPackManagement.ShaderPackSearchQuery;
            if (IsBackupManagementDetailsStep)
                return Details.Backup.BackupSearchQuery;
            return InstanceList.SearchQuery;
        }
        set
        {
            if (IsModManagementDetailsStep)
                Details.ModManagement.ModSearchQuery = value;
            else if (IsSaveManagementDetailsStep)
                Details.SaveManagement.SaveSearchQuery = value;
            else if (IsResourcePackManagementDetailsStep)
                Details.ResourcePackManagement.ResourcePackSearchQuery = value;
            else if (IsShaderPackManagementDetailsStep)
                Details.ShaderPackManagement.ShaderPackSearchQuery = value;
            else if (IsBackupManagementDetailsStep)
                Details.Backup.BackupSearchQuery = value;
            else if (IsListStep)
                InstanceList.SearchQuery = value;
            OnPropertyChanged();
        }
    }

    public bool UpdateImportDropState(IReadOnlyList<string> paths)
    {
        var evaluation = EvaluateImportDrop(paths);
        ApplyImportDropHint(evaluation);
        return evaluation.ShouldHandle && evaluation.CanAccept;
    }

    public void ClearImportDropState()
    {
        lastImportDropHintMessage = string.Empty;
        floatingMessageService.Show(string.Empty);
    }

    public async Task HandleImportDropAsync(IReadOnlyList<string> paths)
    {
        var evaluation = EvaluateImportDrop(paths);
        ApplyImportDropHint(evaluation);
        if (!evaluation.ShouldHandle)
        {
            ClearImportDropState();
            return;
        }
        ClearImportDropState();
        if (!evaluation.CanAccept)
            return;
        try
        {
            logger.LogInformation(
                "Handling game settings import drop. Section={SectionId} FileCount={FileCount} InstanceId={InstanceId}",
                Details.SelectedSection?.Id ?? "<none>",
                paths.Count,
                InstanceList.SelectedInstance?.Instance.Id ?? "<none>");
            await Details.HandleImportDropAsync(paths);
        }
        finally
        {
            ClearImportDropState();
        }
    }

    public void PrimeFromSettings(LauncherSettings settings) => Details.PrimeFromSettings(settings);

    public Task EnsureInstancesLoadedAsync(CancellationToken cancellationToken = default) =>
        InstanceList.EnsureLoadedAsync(cancellationToken);

    [RelayCommand]
    public Task RefreshInstancesAsync(CancellationToken cancellationToken = default) =>
        InstanceList.RefreshAsync(cancellationToken);

    public Task RefreshInstancesForPageActivationAsync(CancellationToken cancellationToken = default) =>
        InstanceList.RefreshForActivationAsync(cancellationToken);

    public Task RefreshInstancesSilentlyAsync(CancellationToken cancellationToken = default) =>
        InstanceList.RefreshSilentlyAsync(cancellationToken);

    public async Task OpenInstanceDetailsAsync(GameInstance? instance, CancellationToken cancellationToken = default)
    {
        await OpenInstanceDetailsAsync(instance, null, cancellationToken);
    }

    public async Task OpenInstanceJavaSettingsAsync(GameInstance instance, CancellationToken cancellationToken = default)
    {
        await OpenInstanceDetailsAsync(instance, "java", cancellationToken);
    }

    public void ShowInstanceDetails(GameInstance? instance, string? sectionId = null)
    {
        if (instance is null)
        {
            CurrentStep = GameSettingsPageStep.List;
            return;
        }
        var item = InstanceList.GetOrAdd(instance);
        InstanceList.SelectInstance(item);
        SelectDetailsSectionCore(ResolveDetailSection(sectionId));
        CurrentStep = GameSettingsPageStep.Details;
    }

    public void AddOrUpdateInstance(GameInstance instance) => InstanceList.AddOrUpdate(instance);

    [RelayCommand]
    private void SelectInstance(GameSettingsInstanceItem instance)
    {
        InstanceList.SelectInstance(instance);
        SelectDetailsSectionCore(DetailSections.FirstOrDefault());
        CurrentStep = GameSettingsPageStep.Details;
    }

    [RelayCommand]
    private void SelectSecondaryMenuItem(object? item)
    {
        switch (item)
        {
            case GameSettingsInstanceCategory category:
                CurrentStep = GameSettingsPageStep.List;
                InstanceList.SelectCategory(category);
                break;
            case GameSettingsDetailSectionItem section:
                SelectDetailsSectionCore(section);
                break;
        }
    }

    [RelayCommand]
    private void BackToInstanceList()
    {
        CurrentStep = GameSettingsPageStep.List;
        InstanceList.SetPreserveFilteredSelection(false);
    }

    [RelayCommand]
    private void OpenInstanceFolder(GameSettingsInstanceItem instance)
    {
        var folderPath = instance.Instance.InstanceDirectory;
        if (!instanceFolderService.DirectoryExists(folderPath))
        {
            statusService.Report(Strings.Status_InstanceFolderNotFound);
            return;
        }
        if (!instanceFolderService.TryOpen(folderPath))
            statusService.Report(Strings.Status_OpenInstanceFolderFailed);
    }

    [RelayCommand]
    private void SelectInstanceAndGoHome(GameSettingsInstanceItem instance) =>
        LaunchInstanceRequested?.Invoke(instance.Instance);

    partial void OnCurrentStepChanged(GameSettingsPageStep value)
    {
        InstanceList.SetPreserveFilteredSelection(value is GameSettingsPageStep.Details);
        OnPropertyChanged(nameof(IsListStep));
        OnPropertyChanged(nameof(IsDetailsStep));
        OnPropertyChanged(nameof(CurrentSecondaryMenuItems));
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageTitleIconSource));
        RaiseTopSearchPropertyChanges();
    }

    private async Task OpenInstanceDetailsAsync(
        GameInstance? instance,
        string? sectionId,
        CancellationToken cancellationToken)
    {
        await InstanceList.RefreshForActivationAsync(cancellationToken);
        ShowInstanceDetails(instance, sectionId);
    }

    private bool IsDetailsSection(string sectionId) => IsDetailsStep
        && string.Equals(Details.SelectedSection?.Id, sectionId, StringComparison.OrdinalIgnoreCase);

    private void SelectDetailsSectionCore(GameSettingsDetailSectionItem? section)
    {
        foreach (var item in DetailSections)
            item.IsSelected = ReferenceEquals(item, section);
        Details.SetSelectedSection(section);
    }

    private GameSettingsDetailSectionItem? ResolveDetailSection(string? sectionId)
    {
        if (!string.IsNullOrWhiteSpace(sectionId))
        {
            var match = DetailSections.FirstOrDefault(item =>
                string.Equals(item.Id, sectionId, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match;
        }
        return DetailSections.FirstOrDefault();
    }

    private GameSettingsFileDropEvaluation EvaluateImportDrop(IReadOnlyList<string> paths) =>
        IsDetailsStep ? Details.EvaluateImportDrop(paths) : GameSettingsFileDropEvaluation.Hidden;

    private void ApplyImportDropHint(GameSettingsFileDropEvaluation evaluation)
    {
        var message = evaluation.ShouldHandle
            ? evaluation.CanAccept
                ? Strings.GameSettings_DropReleaseToImportMessage
                : Strings.GameSettings_DropUnsupportedFileMessage
            : string.Empty;
        if (string.Equals(lastImportDropHintMessage, message, StringComparison.Ordinal))
            return;
        lastImportDropHintMessage = message;
        floatingMessageService.Show(message);
    }

    private void HandleSelectedInstanceChanged()
    {
        if (selectedInstanceNotifier is not null)
            selectedInstanceNotifier.PropertyChanged -= SelectedInstance_PropertyChanged;
        selectedInstanceNotifier = InstanceList.SelectedInstance;
        if (selectedInstanceNotifier is not null)
            selectedInstanceNotifier.PropertyChanged += SelectedInstance_PropertyChanged;
        Details.SetSelectedInstance(InstanceList.SelectedInstance);
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageTitleIconSource));
        if (InstanceList.SelectedInstance is null && IsDetailsStep)
            CurrentStep = GameSettingsPageStep.List;
    }

    private void InstanceList_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(GameSettingsInstanceListViewModel.SelectedInstance):
                HandleSelectedInstanceChanged();
                break;
            case nameof(GameSettingsInstanceListViewModel.SelectedCategory):
                OnPropertyChanged(nameof(PageTitle));
                break;
            case nameof(GameSettingsInstanceListViewModel.SearchQuery):
                OnPropertyChanged(nameof(TopSearchQuery));
                break;
        }
    }

    private void SelectedInstance_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageTitleIconSource));
    }

    private void EditDialog_InstanceRenameStarting() => Details.SuspendLocalWatchersForInstanceRename();

    private void EditDialog_InstanceRenameFinished() => Details.ResumeLocalWatchersAfterInstanceRename();

    private void EditDialog_InstanceUpdated(GameInstance instance)
    {
        InstanceList.AddOrUpdate(instance);
        var item = InstanceList.Find(instance.Id);
        if (item is not null)
        {
            InstanceList.SelectInstance(item);
            CurrentStep = GameSettingsPageStep.Details;
        }
        InstancesChanged?.Invoke(GameSettingsInstancesChangedEventArgs.Updated(instance));
    }

    private void Dialogs_InstanceDeleted(GameSettingsInstanceItem item)
    {
        InstanceList.Remove(item.Instance.Id);
        InstancesChanged?.Invoke(GameSettingsInstancesChangedEventArgs.Deleted(item.Instance.Id));
    }

    private void Details_InstanceSettingsSaved(GameInstance instance)
    {
        InstanceList.AddOrUpdate(instance);
        InstancesChanged?.Invoke(GameSettingsInstancesChangedEventArgs.Updated(instance));
    }

    private void Details_OnlineModInstallRequested(GameInstance instance) => OnlineModInstallRequested?.Invoke(instance);

    private void Details_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GameSettingsDetailsViewModel.SelectedSection)
            or nameof(GameSettingsDetailsViewModel.CurrentSectionViewModel))
        {
            RaiseTopSearchPropertyChanges();
        }
    }

    private void ModManagement_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(InstanceModManagementSettingsViewModel.ModSearchQuery))
            OnPropertyChanged(nameof(TopSearchQuery));
    }

    private void SaveManagement_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(InstanceSaveManagementSettingsViewModel.SaveSearchQuery))
            OnPropertyChanged(nameof(TopSearchQuery));
    }

    private void ResourcePackManagement_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(InstanceResourcePackManagementSettingsViewModel.ResourcePackSearchQuery))
            OnPropertyChanged(nameof(TopSearchQuery));
    }

    private void ShaderPackManagement_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(InstanceShaderPackManagementSettingsViewModel.ShaderPackSearchQuery))
            OnPropertyChanged(nameof(TopSearchQuery));
    }

    private void Backup_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(InstanceBackupSettingsViewModel.BackupSearchQuery))
            OnPropertyChanged(nameof(TopSearchQuery));
    }

    private void RaiseTopSearchPropertyChanges()
    {
        OnPropertyChanged(nameof(IsModManagementDetailsStep));
        OnPropertyChanged(nameof(IsSaveManagementDetailsStep));
        OnPropertyChanged(nameof(IsResourcePackManagementDetailsStep));
        OnPropertyChanged(nameof(IsShaderPackManagementDetailsStep));
        OnPropertyChanged(nameof(IsBackupManagementDetailsStep));
        OnPropertyChanged(nameof(IsExportDetailsStep));
        OnPropertyChanged(nameof(IsTopResourceManagementDetailsStep));
        OnPropertyChanged(nameof(IsTopSearchVisible));
        OnPropertyChanged(nameof(TopSearchQuery));
    }
}
