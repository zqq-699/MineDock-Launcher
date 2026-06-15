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
        var filteredInstances = category?.Id switch
        {
            "mod_loader" => allInstances.Where(instance => instance.HasModLoader),
            "release" => allInstances.Where(instance => instance.IsRelease),
            "snapshot" => allInstances.Where(instance => instance.IsSnapshot),
            "old_beta" => allInstances.Where(instance => instance.IsBeta),
            "old_alpha" => allInstances.Where(instance => instance.IsAlpha),
            _ => allInstances
        };

        if (!string.IsNullOrWhiteSpace(query))
            filteredInstances = filteredInstances.Where(instance => instance.MatchesSearch(query));

        var instances = filteredInstances.ToList();
        var emptyMessage = instances.Count == 0 && hasLoadedInstances && !isLoadingInstances
            ? CreateEmptyMessage(category, query)
            : string.Empty;
        var shouldClearSelectedInstance = selectedInstance is not null && !instances.Contains(selectedInstance);

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

