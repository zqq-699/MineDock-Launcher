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

using Launcher.App.Resources;

namespace Launcher.App.ViewModels.GameSettings;

internal sealed record GameSettingsInstanceFilterResult(
    IReadOnlyList<GameSettingsInstanceItem> Instances,
    string EmptyMessage,
    bool ShouldClearSelectedInstance);

internal static class GameSettingsInstanceFilter
{
    public static GameSettingsInstanceFilterResult Apply(
        IEnumerable<GameSettingsInstanceItem> allInstances,
        GameSettingsInstanceCategory? category,
        string searchQuery,
        GameSettingsInstanceItem? selectedInstance,
        bool hasLoadedInstances,
        bool isLoadingInstances,
        bool hasInstanceLoadError)
    {
        if (hasInstanceLoadError)
            return Empty(shouldClearSelectedInstance: false);

        var query = searchQuery.Trim();
        var filteredInstances = category?.Id == "mod_loader"
            ? allInstances.Where(instance => instance.HasModLoader)
            : ListFilterUtilities.ApplyMinecraftCategory(
                allInstances,
                category?.Id,
                instance => instance.IsRelease,
                instance => instance.IsSnapshot,
                instance => instance.IsAprilFools,
                instance => instance.IsBeta,
                instance => instance.IsAlpha);

        if (category?.Id is null or "all")
            filteredInstances = allInstances;

        if (category?.Id is not (null or "all" or "mod_loader")
            && !ListFilterUtilities.IsKnownMinecraftCategory(category.Id))
        {
            filteredInstances = allInstances;
        }

        if (!string.IsNullOrWhiteSpace(query))
            filteredInstances = filteredInstances.Where(instance => instance.MatchesSearch(query));

        var instances = filteredInstances
            .OrderByDescending(instance => instance.Instance.CreatedAt)
            .ThenByDescending(instance => instance.Instance.UpdatedAt)
            .ToList();
        var emptyMessage = ListFilterUtilities.CreateEmptyMessage(
            instances.Count,
            hasLoadedInstances,
            isLoadingInstances,
            () => CreateEmptyMessage(category, query));
        var shouldClearSelectedInstance = ListFilterUtilities.ShouldClearSelection(selectedInstance, instances);

        return new GameSettingsInstanceFilterResult(instances, emptyMessage, shouldClearSelectedInstance);
    }

    private static GameSettingsInstanceFilterResult Empty(bool shouldClearSelectedInstance)
    {
        return new GameSettingsInstanceFilterResult(
            Array.Empty<GameSettingsInstanceItem>(),
            string.Empty,
            shouldClearSelectedInstance);
    }

    private static string CreateEmptyMessage(GameSettingsInstanceCategory? category, string query)
    {
        if (!string.IsNullOrWhiteSpace(query))
            return Strings.GameSettings_NoMatchingInstances;

        if (category?.Id is null or "all")
            return Strings.GameSettings_NoInstances;

        return string.Format(Strings.GameSettings_NoCategoryInstancesFormat, category.Title);
    }
}

