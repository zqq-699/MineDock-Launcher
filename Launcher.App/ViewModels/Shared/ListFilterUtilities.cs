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

namespace Launcher.App.ViewModels.Shared;

internal static class ListFilterUtilities
{
    public static IEnumerable<T> ApplyMinecraftCategory<T>(
        IEnumerable<T> items,
        string? categoryId,
        Func<T, bool> isRelease,
        Func<T, bool> isSnapshot,
        Func<T, bool> isAprilFools,
        Func<T, bool> isBeta,
        Func<T, bool> isAlpha)
    {
        return categoryId switch
        {
            "snapshot" => items.Where(item => isSnapshot(item) && !isAprilFools(item)),
            "april_fools" => items.Where(isAprilFools),
            "ancient" => items.Where(item => isBeta(item) || isAlpha(item)),
            "old_beta" => items.Where(isBeta),
            "old_alpha" => items.Where(isAlpha),
            _ => items.Where(isRelease)
        };
    }

    public static bool IsKnownMinecraftCategory(string? categoryId)
    {
        return categoryId is "release" or "snapshot" or "april_fools" or "ancient" or "old_beta" or "old_alpha";
    }

    public static string CreateEmptyMessage(
        int itemCount,
        bool hasLoadedItems,
        bool isLoadingItems,
        Func<string> createMessage)
    {
        return itemCount == 0 && hasLoadedItems && !isLoadingItems
            ? createMessage()
            : string.Empty;
    }

    public static bool ShouldClearSelection<T>(T? selectedItem, IReadOnlyCollection<T> visibleItems)
        where T : class
    {
        return selectedItem is not null && !visibleItems.Contains(selectedItem);
    }
}
