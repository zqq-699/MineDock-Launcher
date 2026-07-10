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

using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests.Helpers;

internal sealed class TestSettingsService : ISettingsService
{
    private LauncherSettings settings;

    public TestSettingsService(LauncherSettings settings)
    {
        this.settings = settings;
    }

    public int SaveCount { get; private set; }

    public Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(settings);
    }

    public Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken = default)
    {
        SaveCount++;
        this.settings = settings;
        return Task.CompletedTask;
    }
}

