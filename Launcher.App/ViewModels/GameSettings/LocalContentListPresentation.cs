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

namespace Launcher.App.ViewModels.GameSettings;

internal static class LocalContentListPresentation
{
    public static bool HasSameReferences<T>(IReadOnlyList<T> current, IReadOnlyList<T> next)
        where T : class
    {
        if (current.Count != next.Count)
            return false;
        for (var index = 0; index < current.Count; index++)
        {
            if (!ReferenceEquals(current[index], next[index]))
                return false;
        }
        return true;
    }

    public static IReadOnlyList<object> CreateSectionedItems<TItem>(
        IReadOnlyList<TItem> visibleItems,
        object infoSection,
        object listSection,
        bool includeInfoSection)
        where TItem : class
    {
        if (!includeInfoSection)
            return [];
        var hasListSection = visibleItems.Count > 0;
        var items = new object[visibleItems.Count + (hasListSection ? 2 : 1)];
        items[0] = infoSection;
        if (hasListSection)
            items[1] = listSection;
        for (var index = 0; index < visibleItems.Count; index++)
            items[index + (hasListSection ? 2 : 1)] = visibleItems[index];
        return items;
    }
}
