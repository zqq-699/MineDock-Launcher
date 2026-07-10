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

namespace Launcher.Tests.GameSettings;

public sealed class LocalContentSelectionStateTests
{

    [Fact]
    public void SyncSelectionToItems_DropsSelectionsNoLongerVisible()
    {
        var state = CreateState();
        var first = new TestItem("a");
        var second = new TestItem("b");
        state.ItemsByPath[first.FullPath] = first;
        state.ItemsByPath[second.FullPath] = second;
        state.SelectAll([first, second]);

        state.SyncSelectionToItems([first], isMultiSelectMode: true);

        Assert.True(first.IsSelected);
        Assert.False(second.IsSelected);
        Assert.Equal([first], state.GetSelectedVisibleItems([first, second]));
    }

    private static LocalContentSelectionState<TestItem> CreateState()
    {
        return new LocalContentSelectionState<TestItem>(
            item => item.FullPath,
            item => item.IsSelected,
            static (item, isSelected) => item.IsSelected = isSelected);
    }

    private sealed class TestItem
    {
        public TestItem(string fullPath)
        {
            FullPath = fullPath;
        }

        public string FullPath { get; }

        public bool IsSelected { get; set; }
    }
}
