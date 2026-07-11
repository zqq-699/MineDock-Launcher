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

using Launcher.App.Resources;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Resources;

/// <summary>
/// 把资源版本按兼容性、Loader 和搜索条件投影为带分组标题的可增量 UI 列表。
/// </summary>
internal sealed class ResourcesAvailableVersionListBuilder
{
    // Builder 不持有页面状态，只封装多个资源页面必须一致使用的筛选、分组和插入规则。
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
        // 只展示数据中实际存在的兼容版本，并使用 Minecraft 版本语义而非普通字符串排序。
        return versions
            .SelectMany(version => NormalizeGameVersionCompatibilityValues(version.GameVersions))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(version => new ResourcesFilterOptionItem { Id = version, Title = version })
            .ToList();
    }

    public IReadOnlyList<ResourcesFilterOptionItem> CreateLoaderFilterOptions(IReadOnlyList<ResourceProjectVersion> versions)
    {
        // Loader 可能来自结构化字段或旧数据的版本名推断，统一规范化后再去重。
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
        // UI 集合同时包含标题和版本项；先完成筛选，再生成不会出现空组的扁平投影。
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
        // 追加分页保留已有对象，避免虚拟化容器和当前滚动位置因全量重建而失效。
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
        // 搜索、Minecraft 版本与 Loader 是交集关系；“全部”值由各自匹配函数解释。
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
        // 标题只在至少存在一个可见子项时加入，避免留下无法解释的空分组。
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
        // 新页结果插入已有同名标题末尾，而不是简单追加到列表尾部破坏分组连续性。
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
        // 优先信任 API 的结构化 Loader；字段缺失时才从版本名称进行保守推断。
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
        // 文本推断只识别带边界的已知 token，避免把普通单词片段误判为 Loader。
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
        // 未知目标不能做严格兼容过滤，否则无法解析的旧实例会被错误显示为没有可用版本。
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
