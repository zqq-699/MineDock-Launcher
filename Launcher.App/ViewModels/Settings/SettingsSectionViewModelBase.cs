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

namespace Launcher.App.ViewModels.Settings;

public abstract class SettingsSectionViewModelBase : ObservableObject
{
    private readonly SettingsPersistenceCoordinator persistence;
    private bool isLoading;

    private protected SettingsSectionViewModelBase(SettingsPersistenceCoordinator persistence)
    {
        this.persistence = persistence;
    }

    private protected LauncherSettings Settings => persistence.Settings;

    private protected bool CanPersist => persistence.IsPrimed && !isLoading;

    private protected void LoadState(Action load)
    {
        isLoading = true;
        try
        {
            load();
        }
        finally
        {
            isLoading = false;
        }
    }

    private protected void Persist(Action<LauncherSettings> update)
    {
        if (CanPersist)
            persistence.Update(update);
    }

    private protected Task PersistImmediatelyAsync(
        Action<LauncherSettings> update,
        CancellationToken cancellationToken = default)
    {
        return CanPersist
            ? persistence.SaveImmediatelyAsync(update, cancellationToken)
            : Task.CompletedTask;
    }
}
