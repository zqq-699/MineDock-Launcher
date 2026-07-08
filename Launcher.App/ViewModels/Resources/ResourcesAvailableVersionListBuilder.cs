using Launcher.App.Resources;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Resources;

internal sealed class ResourcesAvailableVersionListBuilder
{
    private readonly ResourcesOnlineProjectPageOptions options;

    public ResourcesAvailableVersionListBuilder(ResourcesOnlineProjectPageOptions options)
    {
        this.options = options;
    }

    public ResourcesFilterOptionItem CreateAllVersionFilterOption()
    {
        return new ResourcesFilterOptionItem { Id = "all", Title = options.AllVersionsText };
    }

    public List<ResourcesFilterOptionItem> CreateDefaultLoaderFilterOptions()
    {
        return
        [
            new ResourcesFilterOptionItem { Id = "all", Title = options.AllLoadersText }
        ];
    }

    public IReadOnlyList<ResourcesFilterOptionItem> CreateVersionFilterOptions(IReadOnlyList<ResourceProjectVersion> versions)
    {
        return versions
            .SelectMany(version => NormalizeGameVersionCompatibilityValues(version.GameVersions))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(version => new ResourcesFilterOptionItem { Id = version, Title = version })
            .ToList();
    }

    public IReadOnlyList<ResourcesFilterOptionItem> CreateLoaderFilterOptions(IReadOnlyList<ResourceProjectVersion> versions)
    {
        var loaderOptions = CreateDefaultLoaderFilterOptions();
        if (!options.ShowsLoaderFilters)
            return loaderOptions;

        var loaderIds = versions
            .SelectMany(ResolveCompatibilityLoaders)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(loader => !string.Equals(loader, "all", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var loaderId in loaderIds)
            loaderOptions.Add(new ResourcesFilterOptionItem { Id = loaderId, Title = GetLoaderTitle(loaderId) });

        return loaderOptions
            .DistinctBy(option => option.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string ResolveDefaultVersionFilterId(ResourcesModInstallTargetItemViewModel? target)
    {
        if (target?.IsLocalDownload != false || IsUnknownInstanceVersionTarget(target))
            return "all";

        var minecraftVersion = target.Instance?.MinecraftVersion?.Trim();
        return string.IsNullOrWhiteSpace(minecraftVersion) ? "all" : minecraftVersion;
    }

    public string ResolveDefaultLoaderFilterId(ResourcesModInstallTargetItemViewModel? target)
    {
        if (target?.IsLocalDownload != false || IsUnknownInstanceVersionTarget(target) || target.Instance is null)
            return "all";

        return target.Instance.Loader switch
        {
            LoaderKind.Fabric => "fabric",
            LoaderKind.Forge => "forge",
            LoaderKind.NeoForge => "neoforge",
            LoaderKind.Quilt => "quilt",
            _ => "all"
        };
    }

    public string FormatTitle(ResourcesModInstallTargetItemViewModel? target)
    {
        if (target?.IsLocalDownload != false || IsUnknownInstanceVersionTarget(target))
            return options.VersionsAllTitleText;

        if (target.Instance is not { } instance)
            return options.VersionsAllTitleText;

        var minecraftVersion = instance.MinecraftVersion?.Trim();
        if (string.IsNullOrWhiteSpace(minecraftVersion))
            return options.VersionsAllTitleText;

        return options.ShowsLoaderFilters
            ? $"{minecraftVersion}-{GetLoaderId(instance.Loader)}"
            : minecraftVersion;
    }

    public AvailableVersionListBuildResult Build(
        IReadOnlyList<ResourceProjectVersion> versions,
        string title,
        ResourcesModProjectItemViewModel? selectedProject,
        string fallbackIconKey,
        string? selectedVersionId,
        string? selectedLoaderId,
        string searchQuery)
    {
        var items = new List<object>();
        var filteredVersions = versions
            .Where(version => MatchesFilters(version, selectedVersionId, selectedLoaderId, searchQuery))
            .ToList();
        var visibleCount = AddGroupedItems(
            items,
            filteredVersions,
            title,
            selectedProject,
            fallbackIconKey,
            selectedVersionId,
            selectedLoaderId);
        return new AvailableVersionListBuildResult(items, visibleCount);
    }

    public int Append(
        IList<object> items,
        IReadOnlyList<ResourceProjectVersion> versions,
        string title,
        ResourcesModProjectItemViewModel? selectedProject,
        string fallbackIconKey,
        string? selectedVersionId,
        string? selectedLoaderId,
        string searchQuery,
        int currentVisibleCount)
    {
        RemoveEmptyPlaceholderHeader(items, title, currentVisibleCount);

        var appendedCount = 0;
        foreach (var version in versions.Where(version => MatchesFilters(version, selectedVersionId, selectedLoaderId, searchQuery)))
        {
            foreach (var groupTitle in CreateFilteredCompatibilityGroupTitles(version, selectedVersionId, selectedLoaderId))
            {
                var insertIndex = FindGroupInsertIndex(items, groupTitle);
                items.Insert(insertIndex, new ResourcesModVersionItemViewModel(version, selectedProject, fallbackIconKey));
                appendedCount++;
            }
        }

        return appendedCount;
    }

    public string GetLoaderTitle(string loaderId)
    {
        return loaderId switch
        {
            "fabric" => Strings.Download_FabricLoaderTitle,
            "forge" => Strings.Download_ForgeLoaderTitle,
            "neoforge" => Strings.Download_NeoForgeLoaderTitle,
            "quilt" => Strings.Download_QuiltLoaderTitle,
            _ => loaderId
        };
    }

    internal bool MatchesFilters(
        ResourceProjectVersion version,
        string? selectedVersionId,
        string? selectedLoaderId,
        string searchQuery)
    {
        return MatchesSearch(version, searchQuery)
            && MatchesVersionFilter(version, selectedVersionId)
            && MatchesLoaderFilter(version, selectedLoaderId);
    }

    private int AddGroupedItems(
        ICollection<object> items,
        IReadOnlyList<ResourceProjectVersion> versions,
        string title,
        ResourcesModProjectItemViewModel? selectedProject,
        string fallbackIconKey,
        string? selectedVersionId,
        string? selectedLoaderId)
    {
        var groups = new List<AvailableVersionCompatibilityGroup>();
        var groupsByTitle = new Dictionary<string, AvailableVersionCompatibilityGroup>(StringComparer.OrdinalIgnoreCase);

        foreach (var version in versions)
        {
            foreach (var groupTitle in CreateFilteredCompatibilityGroupTitles(version, selectedVersionId, selectedLoaderId))
            {
                if (!groupsByTitle.TryGetValue(groupTitle, out var group))
                {
                    group = new AvailableVersionCompatibilityGroup(groupTitle);
                    groupsByTitle.Add(groupTitle, group);
                    groups.Add(group);
                }

                group.Versions.Add(version);
            }
        }

        if (groups.Count == 0)
        {
            items.Add(new ResourcesModVersionListHeaderItem(title));
            return 0;
        }

        var visibleCount = 0;
        foreach (var group in groups)
        {
            items.Add(new ResourcesModVersionListHeaderItem(group.Title));
            foreach (var version in group.Versions)
            {
                items.Add(new ResourcesModVersionItemViewModel(version, selectedProject, fallbackIconKey));
                visibleCount++;
            }
        }

        return visibleCount;
    }

    private IEnumerable<string> CreateFilteredCompatibilityGroupTitles(
        ResourceProjectVersion version,
        string? selectedVersionId,
        string? selectedLoaderId)
    {
        var gameVersions = NormalizeGameVersionCompatibilityValues(version.GameVersions);
        var loaders = options.ShowsLoaderFilters
            ? ResolveCompatibilityLoaders(version)
            : [string.Empty];

        if (!string.IsNullOrWhiteSpace(selectedVersionId)
            && !string.Equals(selectedVersionId, "all", StringComparison.OrdinalIgnoreCase))
        {
            gameVersions = gameVersions
                .Where(gameVersion => string.Equals(gameVersion, selectedVersionId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (options.ShowsLoaderFilters
            && !string.IsNullOrWhiteSpace(selectedLoaderId)
            && !string.Equals(selectedLoaderId, "all", StringComparison.OrdinalIgnoreCase))
        {
            loaders = loaders
                .Where(loader => string.Equals(loader, selectedLoaderId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        foreach (var gameVersion in gameVersions)
        {
            foreach (var loader in loaders)
            {
                yield return string.IsNullOrWhiteSpace(loader)
                    ? gameVersion
                    : $"{gameVersion}-{loader}";
            }
        }
    }

    private bool MatchesSearch(ResourceProjectVersion version, string searchQuery)
    {
        var query = searchQuery.Trim();
        if (string.IsNullOrWhiteSpace(query))
            return true;

        return ContainsSearchText(version.Name, query)
            || ContainsSearchText(version.VersionNumber, query)
            || ContainsSearchText(version.FileName, query)
            || ContainsSearchText(version.VersionType, query);
    }

    private static bool MatchesVersionFilter(ResourceProjectVersion version, string? selectedVersion)
    {
        if (string.IsNullOrWhiteSpace(selectedVersion)
            || string.Equals(selectedVersion, "all", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return NormalizeGameVersionCompatibilityValues(version.GameVersions)
            .Any(versionId => string.Equals(versionId, selectedVersion, StringComparison.OrdinalIgnoreCase));
    }

    private bool MatchesLoaderFilter(ResourceProjectVersion version, string? selectedLoader)
    {
        if (string.IsNullOrWhiteSpace(selectedLoader)
            || string.Equals(selectedLoader, "all", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!options.ShowsLoaderFilters)
            return true;

        return ResolveCompatibilityLoaders(version)
            .Any(loader => string.Equals(loader, selectedLoader, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsSearchText(string value, string query)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static void RemoveEmptyPlaceholderHeader(IList<object> items, string title, int currentVisibleCount)
    {
        if (currentVisibleCount == 0
            && items.Count == 1
            && items[0] is ResourcesModVersionListHeaderItem header
            && string.Equals(header.Title, title, StringComparison.OrdinalIgnoreCase))
        {
            items.Clear();
        }
    }

    private static int FindGroupInsertIndex(IList<object> items, string title)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index] is not ResourcesModVersionListHeaderItem header
                || !string.Equals(header.Title, title, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var insertIndex = index + 1;
            while (insertIndex < items.Count
                && items[insertIndex] is not ResourcesModVersionListHeaderItem)
            {
                insertIndex++;
            }

            return insertIndex;
        }

        items.Add(new ResourcesModVersionListHeaderItem(title));
        return items.Count;
    }

    internal static IReadOnlyList<string> NormalizeGameVersionCompatibilityValues(IReadOnlyList<string> values)
    {
        var normalized = values
            .Select(value => value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Where(IsMinecraftVersionLike)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return normalized.Count == 0 ? [Strings.Resources_ModVersionsUnknown] : normalized;
    }

    internal static IReadOnlyList<string> ResolveCompatibilityLoaders(ResourceProjectVersion version)
    {
        var loaders = NormalizeLoaderCompatibilityValues(version.Loaders);
        if (loaders.Count > 0)
            return loaders;

        loaders = NormalizeLoaderCompatibilityValues(version.GameVersions);
        if (loaders.Count > 0)
            return loaders;

        loaders = InferLoadersFromVersionText(version);
        return loaders.Count == 0 ? [Strings.Resources_ModLoadersUnknown] : loaders;
    }

    private static IReadOnlyList<string> NormalizeLoaderCompatibilityValues(IReadOnlyList<string> values)
    {
        return values
            .Select(TryNormalizeLoaderId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()!;
    }

    private static IReadOnlyList<string> InferLoadersFromVersionText(ResourceProjectVersion version)
    {
        var text = string.Join(
            ' ',
            version.FileName,
            version.Name,
            version.VersionNumber);

        var loaders = new List<string>();
        AddLoaderIfFound(text, "neoforge", loaders);
        AddLoaderIfFound(text, "fabric", loaders);
        AddLoaderIfFound(text, "forge", loaders);
        AddLoaderIfFound(text, "quilt", loaders);
        return loaders;
    }

    private static void AddLoaderIfFound(string text, string loader, ICollection<string> loaders)
    {
        if (ContainsLoaderToken(text, loader)
            && !loaders.Contains(loader, StringComparer.OrdinalIgnoreCase))
        {
            loaders.Add(loader);
        }
    }

    private static bool ContainsLoaderToken(string text, string loader)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var index = text.IndexOf(loader, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var before = index == 0 ? '\0' : text[index - 1];
            var afterIndex = index + loader.Length;
            var after = afterIndex >= text.Length ? '\0' : text[afterIndex];
            if (!char.IsLetterOrDigit(before) && !char.IsLetterOrDigit(after))
                return true;

            index = text.IndexOf(loader, index + loader.Length, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool IsMinecraftVersionLike(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0 || !char.IsDigit(trimmed[0]))
            return false;

        return trimmed.All(character =>
            char.IsLetterOrDigit(character)
            || character is '.' or '-' or '_');
    }

    private static string? TryNormalizeLoaderId(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "fabric" => "fabric",
            "forge" => "forge",
            "neoforge" => "neoforge",
            "quilt" => "quilt",
            _ => null
        };
    }

    internal static bool IsUnknownInstanceVersionTarget(ResourcesModInstallTargetItemViewModel? target)
    {
        return target?.IsLocalDownload is false
            && !target.IsNewInstanceInstall
            && string.IsNullOrWhiteSpace(target.Instance?.MinecraftVersion);
    }

    private static string GetLoaderId(LoaderKind loader)
    {
        return loader switch
        {
            LoaderKind.Fabric => "fabric",
            LoaderKind.Forge => "forge",
            LoaderKind.NeoForge => "neoforge",
            LoaderKind.Quilt => "quilt",
            _ => "vanilla"
        };
    }

    private sealed class AvailableVersionCompatibilityGroup
    {
        public AvailableVersionCompatibilityGroup(string title)
        {
            Title = title;
        }

        public string Title { get; }

        public List<ResourceProjectVersion> Versions { get; } = [];
    }
}

internal sealed record AvailableVersionListBuildResult(
    IReadOnlyList<object> Items,
    int VisibleVersionCount);
