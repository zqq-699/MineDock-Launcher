using Launcher.App.Resources;

namespace Launcher.App.ViewModels;

internal sealed record DownloadVersionFilterResult(
    IReadOnlyList<DownloadMinecraftVersionItem> Versions,
    string EmptyMessage,
    bool ShouldClearSelectedVersion);

internal static class DownloadVersionFilter
{
    public static DownloadVersionFilterResult Apply(
        IEnumerable<DownloadMinecraftVersionItem> allVersions,
        DownloadVersionCategory? category,
        string searchQuery,
        DownloadMinecraftVersionItem? selectedVersion,
        bool hasLoadedVersions,
        bool isLoadingVersions,
        bool hasVersionLoadError)
    {
        if (hasVersionLoadError)
            return Empty(shouldClearSelectedVersion: false);

        if (category is null)
        {
            return new DownloadVersionFilterResult(
                Array.Empty<DownloadMinecraftVersionItem>(),
                Strings.Status_UnimplementedCategory,
                ShouldClearSelectedVersion: true);
        }

        var categoryId = MinecraftVersionIconResolver.NormalizeVersionType(category.Id);
        if (categoryId is not ("release" or "snapshot" or "old_beta" or "old_alpha"))
        {
            return new DownloadVersionFilterResult(
                Array.Empty<DownloadMinecraftVersionItem>(),
                Strings.Status_UnimplementedCategory,
                ShouldClearSelectedVersion: true);
        }

        var query = searchQuery.Trim();
        var filteredVersions = categoryId switch
        {
            "snapshot" => allVersions.Where(version => version.IsSnapshot),
            "old_beta" => allVersions.Where(version => version.IsBeta),
            "old_alpha" => allVersions.Where(version => version.IsAlpha),
            _ => allVersions.Where(version => version.IsRelease)
        };

        if (!string.IsNullOrWhiteSpace(query))
            filteredVersions = filteredVersions.Where(version => version.Name.Contains(query, StringComparison.OrdinalIgnoreCase));

        var versions = Sort(filteredVersions, categoryId).ToList();
        var emptyMessage = versions.Count == 0 && hasLoadedVersions && !isLoadingVersions
            ? CreateEmptyMessage(category.Title, query)
            : string.Empty;
        var shouldClearSelectedVersion = selectedVersion is not null && !versions.Contains(selectedVersion);

        return new DownloadVersionFilterResult(versions, emptyMessage, shouldClearSelectedVersion);
    }

    private static DownloadVersionFilterResult Empty(bool shouldClearSelectedVersion)
    {
        return new DownloadVersionFilterResult(
            Array.Empty<DownloadMinecraftVersionItem>(),
            string.Empty,
            shouldClearSelectedVersion);
    }

    private static string CreateEmptyMessage(string categoryTitle, string query)
    {
        return string.IsNullOrWhiteSpace(query)
            ? string.Format(Strings.Status_NoCategoryVersionsFormat, categoryTitle)
            : Strings.Status_NoMatchingVersions;
    }

    private static IEnumerable<DownloadMinecraftVersionItem> Sort(
        IEnumerable<DownloadMinecraftVersionItem> versions,
        string? categoryId)
    {
        if (categoryId is "snapshot" or "old_beta" or "old_alpha")
        {
            return versions
                .OrderByDescending(version => version.Version.ReleaseTime ?? DateTimeOffset.MinValue)
                .ThenByDescending(version => version.Name, StringComparer.OrdinalIgnoreCase);
        }

        return versions;
    }
}
