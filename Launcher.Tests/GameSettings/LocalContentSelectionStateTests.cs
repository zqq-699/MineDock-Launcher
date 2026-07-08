namespace Launcher.Tests.GameSettings;

public sealed class LocalContentSelectionStateTests
{
    [Fact]
    public void SelectAllAndClearSelectedPaths_UpdateVisibleSelection()
    {
        var state = CreateState();
        var items = new[]
        {
            new TestItem("a"),
            new TestItem("b")
        };

        state.SelectAll(items);

        Assert.All(items, item => Assert.True(item.IsSelected));
        Assert.Equal(2, state.CountSelectedVisibleItems(items));

        state.ClearSelectedPaths();
        state.ClearVisibleSelections(items);

        Assert.All(items, item => Assert.False(item.IsSelected));
        Assert.Equal(0, state.CountSelectedVisibleItems(items));
    }

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

    [Fact]
    public void SelectSingle_RemembersPathAndClearsVisibleSelections()
    {
        var state = CreateState();
        var first = new TestItem("a") { IsSelected = true };
        var second = new TestItem("b") { IsSelected = true };

        state.SelectSingle(second, [first, second]);

        Assert.Equal("b", state.LastSingleSelectedPath);
        Assert.False(first.IsSelected);
        Assert.False(second.IsSelected);
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
