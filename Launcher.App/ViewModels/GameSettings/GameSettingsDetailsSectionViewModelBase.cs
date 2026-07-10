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

namespace Launcher.App.ViewModels.GameSettings;

public abstract class GameSettingsDetailsSectionViewModelBase : ObservableObject
{
    protected GameSettingsDetailsSectionViewModelBase(GameSettingsDetailsViewModel parent)
    {
        Parent = parent;
    }

    public GameSettingsDetailsViewModel Parent { get; }

    public virtual bool UsesFullViewportLayout => false;

    public virtual void OnSelectedInstanceChanged(GameInstance? instance)
    {
    }

    public virtual void OnSectionDeactivated()
    {
    }

    public virtual Task OnSectionActivatedAsync()
    {
        return Task.CompletedTask;
    }
}
