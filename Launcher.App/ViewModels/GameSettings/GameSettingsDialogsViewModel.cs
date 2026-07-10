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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.GameSettings;

public sealed partial class GameSettingsDialogsViewModel : ObservableObject
{
    private readonly IGameInstanceService instanceService;
    private readonly IStatusService statusService;
    private readonly GameSettingsDetailsViewModel details;
    private readonly ILogger<GameSettingsDialogsViewModel> logger;

    [ObservableProperty] private bool isDeleteInstanceDialogOpen;
    [ObservableProperty] private GameSettingsInstanceItem? instancePendingDelete;
    [ObservableProperty] private bool isDeleteContentDialogOpen;
    private PendingContentDeletion? pendingContentDeletion;
    [ObservableProperty] private bool isReplaceModImportDialogOpen;
    [ObservableProperty] private ModImportConflictRequest? pendingModImportConflict;
    [ObservableProperty] private bool isInvalidImportDialogOpen;
    [ObservableProperty] private string invalidImportDialogMessage = string.Empty;
    [ObservableProperty] private string invalidImportDialogTitle = Strings.Dialog_InvalidSaveImportTitle;

    public GameSettingsDialogsViewModel(
        IGameInstanceService instanceService,
        IStatusService statusService,
        GameSettingsDetailsViewModel details,
        ILogger<GameSettingsDialogsViewModel>? logger = null)
    {
        this.instanceService = instanceService;
        this.statusService = statusService;
        this.details = details;
        this.logger = logger ?? NullLogger<GameSettingsDialogsViewModel>.Instance;
    }

    public event Action<GameSettingsInstanceItem>? InstanceDeleted;

    public string DeleteInstanceDialogMessage => InstancePendingDelete is null
        ? string.Empty
        : string.Format(Strings.Dialog_DeleteInstanceMessageFormat, InstancePendingDelete.Name);

    public string DeleteContentDialogTitle => pendingContentDeletion?.Kind switch
    {
        ContentKind.Saves => Strings.Dialog_DeleteSavesTitle,
        ContentKind.ResourcePacks => Strings.Dialog_DeleteResourcePacksTitle,
        ContentKind.ShaderPacks => Strings.Dialog_DeleteShaderPacksTitle,
        _ => Strings.Dialog_DeleteModsTitle
    };

    public string DeleteContentDialogMessage => pendingContentDeletion is null
        ? string.Empty
        : FormatDeleteMessage(pendingContentDeletion);

    public string ReplaceModImportDialogMessage => PendingModImportConflict is null
        ? string.Empty
        : string.Format(Strings.Dialog_ReplaceModImportMessageFormat, PendingModImportConflict.FileName);

    [RelayCommand]
    public void OpenDeleteInstance(GameSettingsInstanceItem instance)
    {
        InstancePendingDelete = instance;
        IsDeleteInstanceDialogOpen = true;
    }

    public void OpenDeleteMods(ModDeleteRequest request) =>
        OpenContentDeletion(ContentKind.Mods, request.FullPaths, request.Titles);

    public void OpenDeleteSaves(SaveDeleteRequest request) =>
        OpenContentDeletion(ContentKind.Saves, request.FullPaths, request.Titles);

    public void OpenDeleteResourcePacks(ResourcePackDeleteRequest request) =>
        OpenContentDeletion(ContentKind.ResourcePacks, request.FullPaths, request.Titles);

    public void OpenDeleteShaderPacks(ShaderPackDeleteRequest request) =>
        OpenContentDeletion(ContentKind.ShaderPacks, request.FullPaths, request.Titles);

    public void OpenModImportConflict(ModImportConflictRequest request)
    {
        PendingModImportConflict = request;
        IsReplaceModImportDialogOpen = true;
    }

    public void OpenSaveImportFailure(SaveImportFailureRequest request) =>
        OpenImportFailure(Strings.Dialog_InvalidSaveImportTitle, request.Message);

    public void OpenResourcePackImportFailure(ResourcePackImportFailureRequest request) =>
        OpenImportFailure(Strings.Dialog_InvalidResourcePackImportTitle, request.Message);

    public void OpenShaderPackImportFailure(ShaderPackImportFailureRequest request) =>
        OpenImportFailure(Strings.Dialog_InvalidShaderPackImportTitle, request.Message);

    [RelayCommand]
    private void CancelDeleteInstanceDialog()
    {
        IsDeleteInstanceDialogOpen = false;
        InstancePendingDelete = null;
    }

    [RelayCommand]
    private async Task ConfirmDeleteInstanceDialogAsync()
    {
        if (InstancePendingDelete is null)
            return;
        var pending = InstancePendingDelete;
        IsDeleteInstanceDialogOpen = false;
        InstancePendingDelete = null;
        try
        {
            if (!await instanceService.DeleteInstanceAsync(pending.Instance.Id))
            {
                statusService.Report(Strings.Status_DeleteInstanceFailed);
                return;
            }
            statusService.Report(string.Format(Strings.Status_InstanceDeletedFormat, pending.Name));
            InstanceDeleted?.Invoke(pending);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to delete game instance. InstanceId={InstanceId}", pending.Instance.Id);
            statusService.Report(Strings.Status_DeleteInstanceFailed);
        }
    }

    [RelayCommand]
    private void CancelDeleteContentDialog()
    {
        IsDeleteContentDialogOpen = false;
        SetPendingContentDeletion(null);
    }

    [RelayCommand]
    private async Task ConfirmDeleteContentDialogAsync()
    {
        var pending = pendingContentDeletion;
        if (pending is null)
            return;
        IsDeleteContentDialogOpen = false;
        SetPendingContentDeletion(null);
        switch (pending.Kind)
        {
            case ContentKind.Mods:
                await details.DeleteModsAsync(pending.FullPaths);
                break;
            case ContentKind.Saves:
                await details.DeleteSavesAsync(pending.FullPaths);
                break;
            case ContentKind.ResourcePacks:
                await details.DeleteResourcePacksAsync(pending.FullPaths);
                break;
            case ContentKind.ShaderPacks:
                await details.DeleteShaderPacksAsync(pending.FullPaths);
                break;
        }
    }

    [RelayCommand]
    private void CancelReplaceModImportDialog()
    {
        IsReplaceModImportDialogOpen = false;
        PendingModImportConflict = null;
        details.ResolvePendingModImportConflict(false);
    }

    [RelayCommand]
    private void ConfirmReplaceModImportDialog()
    {
        if (PendingModImportConflict is null)
            return;
        IsReplaceModImportDialogOpen = false;
        PendingModImportConflict = null;
        details.ResolvePendingModImportConflict(true);
    }

    [RelayCommand]
    private void CloseInvalidImportDialog()
    {
        IsInvalidImportDialogOpen = false;
        InvalidImportDialogMessage = string.Empty;
        InvalidImportDialogTitle = Strings.Dialog_InvalidSaveImportTitle;
    }

    partial void OnInstancePendingDeleteChanged(GameSettingsInstanceItem? value) =>
        OnPropertyChanged(nameof(DeleteInstanceDialogMessage));

    partial void OnPendingModImportConflictChanged(ModImportConflictRequest? value) =>
        OnPropertyChanged(nameof(ReplaceModImportDialogMessage));

    private void OpenContentDeletion(ContentKind kind, IReadOnlyList<string> fullPaths, IReadOnlyList<string> titles)
    {
        SetPendingContentDeletion(new PendingContentDeletion(kind, fullPaths, titles));
        IsDeleteContentDialogOpen = true;
    }

    private void SetPendingContentDeletion(PendingContentDeletion? value)
    {
        if (!SetProperty(ref pendingContentDeletion, value))
            return;
        OnPropertyChanged(nameof(DeleteContentDialogTitle));
        OnPropertyChanged(nameof(DeleteContentDialogMessage));
    }

    private void OpenImportFailure(string title, string message)
    {
        InvalidImportDialogTitle = title;
        InvalidImportDialogMessage = message;
        IsInvalidImportDialogOpen = true;
    }

    private static string FormatDeleteMessage(PendingContentDeletion pending)
    {
        var single = pending.Titles.Count == 1;
        return pending.Kind switch
        {
            ContentKind.Saves => single
                ? string.Format(Strings.Dialog_DeleteSingleSaveMessageFormat, pending.Titles[0])
                : string.Format(Strings.Dialog_DeleteMultipleSavesMessageFormat, pending.Titles.Count),
            ContentKind.ResourcePacks => single
                ? string.Format(Strings.Dialog_DeleteSingleResourcePackMessageFormat, pending.Titles[0])
                : string.Format(Strings.Dialog_DeleteMultipleResourcePacksMessageFormat, pending.Titles.Count),
            ContentKind.ShaderPacks => single
                ? string.Format(Strings.Dialog_DeleteSingleShaderPackMessageFormat, pending.Titles[0])
                : string.Format(Strings.Dialog_DeleteMultipleShaderPacksMessageFormat, pending.Titles.Count),
            _ => single
                ? string.Format(Strings.Dialog_DeleteSingleModMessageFormat, pending.Titles[0])
                : string.Format(Strings.Dialog_DeleteMultipleModsMessageFormat, pending.Titles.Count)
        };
    }

    private enum ContentKind { Mods, Saves, ResourcePacks, ShaderPacks }

    private sealed record PendingContentDeletion(
        ContentKind Kind,
        IReadOnlyList<string> FullPaths,
        IReadOnlyList<string> Titles);
}
