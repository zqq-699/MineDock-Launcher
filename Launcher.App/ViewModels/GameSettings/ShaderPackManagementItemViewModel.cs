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

public sealed partial class ShaderPackManagementItemViewModel : ObservableObject
{
    public ShaderPackManagementItemViewModel(LocalShaderPack shaderPack)
    {
        SyncFrom(shaderPack);
    }

    public string TrailingText => CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public string IconKey => string.IsNullOrWhiteSpace(IconSource)
        ? "instance_setting_page/shader"
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

    public void SyncFrom(LocalShaderPack shaderPack)
    {
        Title = shaderPack.Name;
        Subtitle = string.Equals(shaderPack.Name, shaderPack.FileName, StringComparison.OrdinalIgnoreCase)
            ? null
            : shaderPack.FileName;
        FullPath = shaderPack.FullPath;
        IconSource = shaderPack.IconSource;
        TitleTags = ResourceProjectCategoryTitleFormatter.Format(ResourceProjectKind.ShaderPack, shaderPack.Categories);
        ProjectReference = shaderPack.ProjectReference;
        CreatedAt = shaderPack.CreatedAt;
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
