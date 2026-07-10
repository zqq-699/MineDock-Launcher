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
using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Account;

public sealed partial class AccountSkinLibraryViewModel : ObservableObject
{
    [ObservableProperty]
    private LauncherSkinRecord? selectedSkin;

    [ObservableProperty]
    private bool isManagerDialogOpen;

    public ObservableCollection<LauncherSkinRecord> Skins { get; } = [];

    public LauncherSkinRecord? PreviousSkin => GetAdjacent(-1);

    public LauncherSkinRecord? NextSkin => GetAdjacent(1);

    public bool HasSkins => Skins.Count > 0;

    public void SelectPrevious()
    {
        if (PreviousSkin is { } skin)
            SelectedSkin = skin;
    }

    public void SelectNext()
    {
        if (NextSkin is { } skin)
            SelectedSkin = skin;
    }

    public void NotifyCollectionChanged()
    {
        OnPropertyChanged(nameof(HasSkins));
        NotifyAdjacentChanged();
    }

    partial void OnSelectedSkinChanged(LauncherSkinRecord? value)
    {
        NotifyAdjacentChanged();
    }

    private LauncherSkinRecord? GetAdjacent(int offset)
    {
        if (SelectedSkin is null || Skins.Count < 2)
            return null;
        var index = Skins.IndexOf(SelectedSkin);
        if (index < 0)
            return null;
        var adjacent = index + offset;
        return adjacent >= 0 && adjacent < Skins.Count ? Skins[adjacent] : null;
    }

    private void NotifyAdjacentChanged()
    {
        OnPropertyChanged(nameof(PreviousSkin));
        OnPropertyChanged(nameof(NextSkin));
    }
}
