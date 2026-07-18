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
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Models;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Accounts;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.Home;

public sealed class JavaRequirementNotMetEventArgs : EventArgs
{
    public JavaRequirementNotMetEventArgs(
        int? requiredMajorVersion,
        JavaRuntimeSelectionFailureReason reason,
        GameInstance instance,
        int? currentMajorVersion = null,
        string? currentVersion = null,
        int? recommendedMajorVersion = null)
    {
        RequiredMajorVersion = requiredMajorVersion;
        Reason = reason;
        Instance = instance;
        CurrentMajorVersion = currentMajorVersion;
        CurrentVersion = currentVersion;
        RecommendedMajorVersion = recommendedMajorVersion ?? requiredMajorVersion;
    }

    public int? RequiredMajorVersion { get; }

    public JavaRuntimeSelectionFailureReason Reason { get; }

    public GameInstance Instance { get; }

    public int? CurrentMajorVersion { get; }

    public string? CurrentVersion { get; }

    public int? RecommendedMajorVersion { get; }
}
