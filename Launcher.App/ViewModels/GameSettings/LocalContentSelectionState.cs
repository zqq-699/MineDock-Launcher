namespace Launcher.App.ViewModels.GameSettings;

internal sealed class LocalContentSelectionState<TItem>
{
    private readonly Func<TItem, string> pathSelector;
    private readonly Func<TItem, bool> isSelectedSelector;
    private readonly Action<TItem, bool> setSelected;
    private readonly Dictionary<string, TItem> itemsByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> selectedPaths = new(StringComparer.OrdinalIgnoreCase);

    public LocalContentSelectionState(
        Func<TItem, string> pathSelector,
        Func<TItem, bool> isSelectedSelector,
        Action<TItem, bool> setSelected)
    {
        this.pathSelector = pathSelector;
        this.isSelectedSelector = isSelectedSelector;
        this.setSelected = setSelected;
    }

    public IDictionary<string, TItem> ItemsByPath => itemsByPath;

    public string? LastSingleSelectedPath { get; private set; }

    public void RememberSingleSelection(TItem? selectedItem)
    {
        if (selectedItem is not null)
            LastSingleSelectedPath = pathSelector(selectedItem);
    }

    public void ClearCache()
    {
        itemsByPath.Clear();
    }

    public void Reset()
    {
        LastSingleSelectedPath = null;
        selectedPaths.Clear();
    }

    public void BeginMultiSelect(TItem? selectedItem, IReadOnlyList<TItem> visibleItems)
    {
        RememberSingleSelection(selectedItem);
        selectedPaths.Clear();
        ClearVisibleSelections(visibleItems);
    }

    public void ClearSelectedPaths()
    {
        selectedPaths.Clear();
    }

    public void SelectAll(IReadOnlyList<TItem> visibleItems)
    {
        foreach (var item in visibleItems)
        {
            setSelected(item, true);
            selectedPaths.Add(pathSelector(item));
        }
    }

    public void ClearVisibleSelections(IReadOnlyList<TItem> visibleItems)
    {
        foreach (var item in visibleItems)
            setSelected(item, false);
    }

    public void ToggleSelection(TItem item)
    {
        var isSelected = !isSelectedSelector(item);
        setSelected(item, isSelected);
        if (isSelected)
            selectedPaths.Add(pathSelector(item));
        else
            selectedPaths.Remove(pathSelector(item));
    }

    public void SelectSingle(TItem item, IReadOnlyList<TItem> visibleItems)
    {
        LastSingleSelectedPath = pathSelector(item);
        ClearVisibleSelections(visibleItems);
    }

    public void SyncSelectionToItems(IReadOnlyList<TItem> visibleItems, bool isMultiSelectMode)
    {
        if (isMultiSelectMode)
            selectedPaths.IntersectWith(visibleItems.Select(pathSelector));

        foreach (var item in itemsByPath.Values)
            setSelected(item, isMultiSelectMode && selectedPaths.Contains(pathSelector(item)));
    }

    public IReadOnlyList<TItem> GetSelectedVisibleItems(IReadOnlyList<TItem> visibleItems)
    {
        return visibleItems
            .Where(item => selectedPaths.Contains(pathSelector(item)))
            .ToArray();
    }

    public int CountSelectedVisibleItems(IReadOnlyList<TItem> visibleItems)
    {
        return visibleItems.Count(isSelectedSelector);
    }
}
