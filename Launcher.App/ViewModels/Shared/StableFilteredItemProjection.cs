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

internal static class StableFilteredItemProjection
{
    public static IReadOnlyList<TItem> Synchronize<TSource, TKey, TItem>(
        IReadOnlyList<TSource> sourceItems,
        IDictionary<TKey, TItem> itemCache,
        Func<TSource, TKey> keySelector,
        Func<TSource, TItem> createItem,
        Action<TItem, TSource> updateItem,
        Func<TSource, bool> visibilityPredicate)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(sourceItems);
        ArgumentNullException.ThrowIfNull(itemCache);
        ArgumentNullException.ThrowIfNull(keySelector);
        ArgumentNullException.ThrowIfNull(createItem);
        ArgumentNullException.ThrowIfNull(updateItem);
        ArgumentNullException.ThrowIfNull(visibilityPredicate);

        var nextKeys = new HashSet<TKey>();
        var visibleItems = new List<TItem>(sourceItems.Count);

        foreach (var sourceItem in sourceItems)
        {
            var key = keySelector(sourceItem);
            nextKeys.Add(key);

            if (!itemCache.TryGetValue(key, out var item))
            {
                item = createItem(sourceItem);
                itemCache[key] = item;
            }
            else
            {
                updateItem(item, sourceItem);
            }

            if (visibilityPredicate(sourceItem))
                visibleItems.Add(item);
        }

        var staleKeys = itemCache.Keys.Where(key => !nextKeys.Contains(key)).ToArray();
        foreach (var staleKey in staleKeys)
            itemCache.Remove(staleKey);

        return visibleItems;
    }
}
