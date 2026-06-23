using System.Collections.ObjectModel;

namespace Launcher.App.ViewModels.Shared;

internal static class ObservableCollectionExtensions
{
    public static void ReplaceWith<T>(this ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items)
            collection.Add(item);
    }

    public static bool ReplaceWithIfChanged<T>(this ObservableCollection<T> collection, IReadOnlyList<T> items)
    {
        if (collection.Count == items.Count)
        {
            var isSame = true;
            for (var index = 0; index < items.Count; index++)
            {
                if (!EqualityComparer<T>.Default.Equals(collection[index], items[index]))
                {
                    isSame = false;
                    break;
                }
            }

            if (isSame)
                return false;
        }

        collection.ReplaceWith(items);
        return true;
    }
}

