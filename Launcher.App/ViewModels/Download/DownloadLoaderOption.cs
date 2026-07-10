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
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Download;

public sealed partial class DownloadLoaderOption : ObservableObject
{
    public DownloadLoaderOption(LoaderKind kind, string title, string subtitle, string icon, string? iconSource = null)
    {
        Kind = kind;
        Title = title;
        Subtitle = subtitle;
        Icon = icon;
        IconSource = iconSource;
    }

    public LoaderKind Kind { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public string Icon { get; }

    public string? IconSource { get; }

    [ObservableProperty]
    private bool isSelected;
}

