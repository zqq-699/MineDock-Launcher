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
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.GameSettings;

public sealed partial class ModrinthSearchViewModel : ObservableObject
{
    private readonly IModrinthService modrinthService;
    private readonly IStatusService statusService;

    [ObservableProperty]
    private string modSearchQuery = string.Empty;

    [ObservableProperty]
    private ModrinthProject? selectedModrinthProject;

    public ModrinthSearchViewModel(
        IModrinthService modrinthService,
        IStatusService statusService)
    {
        this.modrinthService = modrinthService;
        this.statusService = statusService;
    }

    public ObservableCollection<ModrinthProject> ModrinthProjects { get; } = [];

    public async Task SearchModsAsync(GameInstance? selectedInstance)
    {
        if (selectedInstance is null)
        {
            ReportStatus(Strings.Status_SelectInstanceFirst);
            return;
        }

        ReportStatus(Strings.Status_SearchingModrinth);
        var projects = await modrinthService.SearchModsAsync(
            ModSearchQuery,
            selectedInstance.MinecraftVersion,
            selectedInstance.Loader);
        ModrinthProjects.ReplaceWith(projects);

        ReportStatus(string.Format(Strings.Status_ModrinthResultsFoundFormat, ModrinthProjects.Count));
    }

    public async Task<bool> InstallSelectedModAsync(
        GameInstance? selectedInstance,
        IProgress<LauncherProgress>? progress)
    {
        if (selectedInstance is null || SelectedModrinthProject is null)
            return false;

        try
        {
            await modrinthService.InstallLatestCompatibleAsync(SelectedModrinthProject, selectedInstance, progress);
        }
        catch (NoCompatibleModFileException)
        {
            ReportStatus(Strings.Status_ModCompatibleFileNotFound);
            return false;
        }

        ReportStatus(string.Format(Strings.Status_ModInstalledFormat, SelectedModrinthProject.Title));
        return true;
    }

    private void ReportStatus(string message)
    {
        statusService.Report(message);
    }
}

