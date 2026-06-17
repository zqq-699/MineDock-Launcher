namespace Launcher.App.ViewModels.Shared;

internal static class ListFilterUtilities
{
    public static IEnumerable<T> ApplyMinecraftCategory<T>(
        IEnumerable<T> items,
        string? categoryId,
        Func<T, bool> isRelease,
        Func<T, bool> isSnapshot,
        Func<T, bool> isBeta,
        Func<T, bool> isAlpha)
    {
        return categoryId switch
        {
            "snapshot" => items.Where(isSnapshot),
            "old_beta" => items.Where(isBeta),
            "old_alpha" => items.Where(isAlpha),
            _ => items.Where(isRelease)
        };
    }

    public static bool IsKnownMinecraftCategory(string? categoryId)
    {
        return categoryId is "release" or "snapshot" or "old_beta" or "old_alpha";
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
