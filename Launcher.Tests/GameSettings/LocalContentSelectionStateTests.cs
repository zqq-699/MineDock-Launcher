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
