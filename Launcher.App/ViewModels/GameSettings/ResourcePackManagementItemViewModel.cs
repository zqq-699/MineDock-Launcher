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
using Launcher.App.ViewModels.Resources;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.GameSettings;

public sealed partial class ResourcePackManagementItemViewModel : ObservableObject
{
    public ResourcePackManagementItemViewModel(LocalResourcePack resourcePack)
    {
        SyncFrom(resourcePack);
    }

    public string TrailingText => CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public string IconKey => string.IsNullOrWhiteSpace(IconSource)
        ? "main_menu_library"
        : string.Empty;

    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private string? subtitle;

    [ObservableProperty]
    private string fullPath = string.Empty;

    [ObservableProperty]
    private string? iconSource;

    [ObservableProperty]
    private DateTimeOffset createdAt;

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTitleTags))]
    private IReadOnlyList<string> titleTags = [];

    public bool HasTitleTags => TitleTags.Count > 0;

    public bool HasProjectDetails => ProjectReference is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProjectDetails))]
    private ResourceProjectReference? projectReference;

    public void SyncFrom(LocalResourcePack resourcePack)
    {
        Title = resourcePack.Name;
        Subtitle = string.Equals(resourcePack.Name, resourcePack.FileName, StringComparison.OrdinalIgnoreCase)
            ? null
            : resourcePack.FileName;
        FullPath = resourcePack.FullPath;
        IconSource = resourcePack.IconSource;
        TitleTags = ResourceProjectCategoryTitleFormatter.Format(ResourceProjectKind.ResourcePack, resourcePack.Categories);
        ProjectReference = resourcePack.ProjectReference;
        CreatedAt = resourcePack.CreatedAt;
    }

    partial void OnIconSourceChanged(string? value)
    {
        OnPropertyChanged(nameof(IconKey));
    }

    partial void OnCreatedAtChanged(DateTimeOffset value)
    {
        OnPropertyChanged(nameof(TrailingText));
    }
}
