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

namespace Launcher.App.ViewModels.Resources;

public sealed partial class ResourcesProjectDetailsViewModel : ObservableObject
{
    private readonly Stack<ResourcesModProjectItemViewModel> backStack = new();

    [ObservableProperty]
    private ResourcesModProjectItemViewModel? currentProject;

    public bool CanGoBackToDependencyParent => backStack.Count > 0;

    public void SelectRoot(ResourcesModProjectItemViewModel project)
    {
        backStack.Clear();
        CurrentProject = project;
        OnPropertyChanged(nameof(CanGoBackToDependencyParent));
    }

    public void OpenDependency(ResourcesModProjectItemViewModel project)
    {
        if (CurrentProject is not null)
            backStack.Push(CurrentProject);
        CurrentProject = project;
        OnPropertyChanged(nameof(CanGoBackToDependencyParent));
    }

    public bool TryGoBack(out ResourcesModProjectItemViewModel? project)
    {
        if (!backStack.TryPop(out project))
            return false;
        CurrentProject = project;
        OnPropertyChanged(nameof(CanGoBackToDependencyParent));
        return true;
    }

    public void SetCurrent(ResourcesModProjectItemViewModel project)
    {
        CurrentProject = project;
    }

    public void Reset()
    {
        backStack.Clear();
        CurrentProject = null;
        OnPropertyChanged(nameof(CanGoBackToDependencyParent));
    }
}
