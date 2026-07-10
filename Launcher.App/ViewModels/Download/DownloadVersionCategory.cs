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

namespace Launcher.App.ViewModels.Download;

public sealed partial class DownloadVersionCategory : ObservableObject
{
    public DownloadVersionCategory(string id, string title, string icon, string? iconKey = null, bool isEnabled = true)
    {
        Id = id;
        Title = title;
        Icon = icon;
        IconKey = iconKey;
        IsEnabled = isEnabled;
    }

    public string Id { get; }

    public string Title { get; }

    public string Icon { get; }

    public string? IconKey { get; }

    public string IconMode => string.IsNullOrWhiteSpace(IconKey) ? "Glyph" : "Svg";

    public bool IsEnabled { get; }

    [ObservableProperty]
    private bool isSelected;
}

