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
