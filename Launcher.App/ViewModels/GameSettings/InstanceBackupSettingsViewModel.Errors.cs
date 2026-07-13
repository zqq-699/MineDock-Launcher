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
using Launcher.App.ViewModels.Download;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.GameSettings;

public sealed partial class InstanceBackupSettingsViewModel
{
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
        OnPropertyChanged(nameof(CanShowBackupScrollableContent));
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
