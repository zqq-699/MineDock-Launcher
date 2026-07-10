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

using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Launcher.App.ViewModels.Download;

public sealed class DownloadVersionListViewModel : ObservableObject
{
    private readonly DownloadPageViewModel parent;

    public DownloadVersionListViewModel(DownloadPageViewModel parent)
    {
        this.parent = parent;
        parent.PropertyChanged += OnParentPropertyChanged;
    }

    public IReadOnlyList<DownloadMinecraftVersionItem> VisibleVersions => parent.VisibleVersions;

    public DownloadMinecraftVersionItem? SelectedMinecraftVersion => parent.SelectedMinecraftVersion;

    public int ListEntranceAnimationToken => parent.ListEntranceAnimationToken;

    public ICommand SelectMinecraftVersionCommand => parent.SelectMinecraftVersionCommand;

    private void OnParentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(e.PropertyName);
    }
}
