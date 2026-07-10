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

using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Launcher.Domain.Models;
using Launcher.Infrastructure.CurseForge;
using Microsoft.Extensions.Logging;

namespace Launcher.Infrastructure.Resources;

internal sealed class CurseForgeResourceClient(
    HttpClient httpClient,
    ICurseForgeApiKeyResolver apiKeyResolver,
    ILogger logger) : IResourceProviderClient
{
    private const string BaseUrl = "https://api.curseforge.com/v1";
    private const int MinecraftGameId = 432;
    private const int ModsClassId = 6;
    private const int ResourcePacksClassId = 12;
    private const int WorldsClassId = 17;
    private const int ModpacksClassId = 4471;
    private const int ShaderPacksClassId = 6552;
    private readonly object categoryGate = new();
    private readonly Dictionary<ResourceProjectKind, Task<IReadOnlyList<CurseForgeCategory>>> categoryTasks = [];

    public ResourceProjectSource Source => ResourceProjectSource.CurseForge;

    public bool Supports(ResourceProjectKind kind) => true;

    public async Task<ResourceProviderSearchResult> SearchAsync(
        ResourceCatalogSearchRequest request,
        CancellationToken cancellationToken)
    {
        var apiKey = await apiKeyResolver.TryResolveAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("Skipping CurseForge resource search because API key is not configured.");
            return new ResourceProviderSearchResult([], false, true, true);
        }

        var categoryId = await ResolveCategoryIdAsync(request.Category, request.Kind, apiKey, cancellationToken).ConfigureAwait(false);
        if (request.Category.HasValue && categoryId is null)
            return new ResourceProviderSearchResult([], false);

        var versions = ResolveMinecraftVersions(request);
        if (versions.Count == 0)
            return await SearchSingleAsync(request, apiKey, null, categoryId, cancellationToken).ConfigureAwait(false);

        var projects = new Dictionary<string, ResourceProject>(StringComparer.OrdinalIgnoreCase);
        var hasMore = false;
        foreach (var version in versions)
        {
            var result = await SearchSingleAsync(request, apiKey, version, categoryId, cancellationToken).ConfigureAwait(false);
            if (result.IsUnavailable)
                return result;
            hasMore |= result.HasMore;
            foreach (var project in result.Projects)
                projects.TryAdd(CreateProjectKey(project), project);
        }
        return new ResourceProviderSearchResult(projects.Values.ToList(), hasMore);
    }

    public async Task<ResourceProjectVersionsResult> GetVersionsAsync(
        ResourceProjectVersionsRequest request,
        CancellationToken cancellationToken)
    {
        if (!long.TryParse(request.ProjectId, out var projectId)
            || !request.IncludeAllVersions && string.IsNullOrWhiteSpace(request.MinecraftVersion))
        {
            return new ResourceProjectVersionsResult();
        }

        var apiKey = await apiKeyResolver.TryResolveAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new ResourceProjectVersionsResult
            {
                IsCurseForgeUnavailable = true,
                IsCurseForgeApiKeyMissing = true
            };
        }

        var pageSize = Math.Clamp(request.PageSize, 1, 10000);
        var offset = Math.Max(0, request.Offset);
        var query = new List<string>
        {
            $"pageSize={pageSize}", $"index={offset}", "sortField=1", "sortOrder=desc"
        };
        if (!request.IncludeAllVersions)
            query.Insert(0, $"gameVersion={Uri.EscapeDataString(request.MinecraftVersion)}");
        if (HasLoaderFacet(request.Kind) && !request.IncludeAllVersions && TryMapLoader(request.Loader, out var loader))
            query.Add($"modLoaderType={(int)loader}");

        using var httpRequest = CreateRequest(HttpMethod.Get, $"{BaseUrl}/mods/{projectId}/files?{string.Join("&", query)}", apiKey);
        using var response = await httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return new ResourceProjectVersionsResult { IsCurseForgeUnavailable = true };
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<FilesResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
        var files = payload?.Data ?? [];
        var excludedIds = CreateCurrentIds(request.ProjectId, request.Slug, projectId.ToString());
        var dependencyProjects = request.Kind is ResourceProjectKind.Mod
            ? await LoadDependencyProjectsAsync(CollectRequiredProjectIds(files, excludedIds), apiKey, cancellationToken).ConfigureAwait(false)
            : new Dictionary<string, ResourceProject>(StringComparer.OrdinalIgnoreCase);

        return new ResourceProjectVersionsResult
        {
            Versions = files
                .Where(file => file.Id > 0 && (!string.IsNullOrWhiteSpace(file.FileName) || !string.IsNullOrWhiteSpace(file.DownloadUrl)))
                .Select(file => MapVersion(file, request.Kind, dependencyProjects, excludedIds))
                .ToList(),
            HasMore = payload?.Pagination?.TotalCount is { } total
                ? offset + files.Count < total
                : files.Count >= pageSize
        };
    }

    public async Task<ResourceProjectDependenciesResult> GetDependenciesAsync(
        ResourceProjectDependenciesRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Kind is not ResourceProjectKind.Mod)
            return new ResourceProjectDependenciesResult();
        var result = await GetVersionsAsync(new ResourceProjectVersionsRequest
        {
            Kind = request.Kind,
            Source = request.Source,
            ProjectId = request.ProjectId,
            Slug = request.Slug,
            IncludeAllVersions = true,
            Offset = 0,
            PageSize = 10000
        }, cancellationToken).ConfigureAwait(false);
        return new ResourceProjectDependenciesResult
        {
            RequiredProjects = result.Versions.SelectMany(version => version.RequiredDependencies)
                .Select(value => value.Project)
                .GroupBy(project => project.ProjectId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList()
        };
    }

    private async Task<ResourceProviderSearchResult> SearchSingleAsync(
        ResourceCatalogSearchRequest request,
        string apiKey,
        string? minecraftVersion,
        int? categoryId,
        CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(request.PageSize, 1, 50);
        var offset = Math.Max(0, request.Offset);
        var query = new List<string>
        {
            $"gameId={MinecraftGameId}", $"classId={MapClassId(request.Kind)}", "sortField=6", "sortOrder=desc",
            $"pageSize={pageSize}", $"index={offset}"
        };
        if (!string.IsNullOrWhiteSpace(request.Query))
            query.Add($"searchFilter={Uri.EscapeDataString(request.Query)}");
        if (!string.IsNullOrWhiteSpace(minecraftVersion))
            query.Add($"gameVersion={Uri.EscapeDataString(minecraftVersion)}");
        if (HasLoaderFacet(request.Kind) && TryMapLoader(request.Loader, out var loader))
            query.Add($"modLoaderType={(int)loader}");
        if (categoryId.HasValue)
            query.Add($"categoryId={categoryId.Value}");

        using var httpRequest = CreateRequest(HttpMethod.Get, $"{BaseUrl}/mods/search?{string.Join("&", query)}", apiKey);
        using var response = await httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return new ResourceProviderSearchResult([], false, true);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<SearchResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
        var projects = payload?.Data.Select(mod => MapProject(mod, request.Kind)).ToList() ?? [];
        var hasMore = payload?.Pagination?.TotalCount is { } total
            ? offset + projects.Count < total
            : projects.Count >= pageSize;
        return new ResourceProviderSearchResult(projects, hasMore);
    }

    private async Task<IReadOnlyDictionary<string, ResourceProject>> LoadDependencyProjectsAsync(
        IReadOnlyList<string> ids,
        string apiKey,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
            return new Dictionary<string, ResourceProject>(StringComparer.OrdinalIgnoreCase);
        using var request = CreateRequest(HttpMethod.Post, $"{BaseUrl}/mods", apiKey);
        request.Content = JsonContent.Create(new ModsRequest(ids.Select(long.Parse).ToList()));
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return new Dictionary<string, ResourceProject>(StringComparer.OrdinalIgnoreCase);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<SearchResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
        return payload?.Data
            .Where(mod => mod.Id > 0 && mod.ClassId == ModsClassId)
            .Select(mod => MapProject(mod, ResourceProjectKind.Mod))
            .GroupBy(project => project.ProjectId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, ResourceProject>(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<int?> ResolveCategoryIdAsync(
        ResourceProjectCategory? category,
        ResourceProjectKind kind,
        string apiKey,
        CancellationToken cancellationToken)
    {
        if (!category.HasValue)
            return null;
        var aliases = ResolveCategoryAliases(category.Value)
            .Select(NormalizeCategory)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var categories = await GetCategoriesAsync(kind, apiKey, cancellationToken).ConfigureAwait(false);
        return categories.FirstOrDefault(candidate =>
            new[] { candidate.Name, candidate.Slug }
                .SelectMany(value => new[] { value, value.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? value })
                .Select(NormalizeCategory)
                .Any(aliases.Contains))?.Id;
    }

    private Task<IReadOnlyList<CurseForgeCategory>> GetCategoriesAsync(
        ResourceProjectKind kind,
        string apiKey,
        CancellationToken cancellationToken)
    {
        lock (categoryGate)
        {
            if (!categoryTasks.TryGetValue(kind, out var task) || task.IsCanceled || task.IsFaulted)
            {
                task = LoadCategoriesAsync(kind, apiKey, cancellationToken);
                categoryTasks[kind] = task;
            }
            return task;
        }
    }

    private async Task<IReadOnlyList<CurseForgeCategory>> LoadCategoriesAsync(
        ResourceProjectKind kind,
        string apiKey,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, $"{BaseUrl}/categories?gameId={MinecraftGameId}&classId={MapClassId(kind)}", apiKey);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return [];
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CategoriesResponse>(cancellationToken: cancellationToken).ConfigureAwait(false))?.Data ?? [];
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url, string apiKey)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        return request;
    }

    private static ResourceProjectVersion MapVersion(
        CurseForgeFile file,
        ResourceProjectKind kind,
        IReadOnlyDictionary<string, ResourceProject> projects,
        ISet<string> excludedIds)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return new ResourceProjectVersion
        {
            VersionId = file.Id.ToString(),
            Kind = kind,
            Name = string.IsNullOrWhiteSpace(file.DisplayName) ? file.FileName : file.DisplayName,
            VersionNumber = string.IsNullOrWhiteSpace(file.DisplayName) ? file.FileName : file.DisplayName,
            VersionType = file.ReleaseType switch { 1 => "release", 2 => "beta", 3 => "alpha", _ => string.Empty },
            FileName = file.FileName,
            PrimaryDownloadUrl = string.IsNullOrWhiteSpace(file.DownloadUrl) ? BuildCdnUrl("edge.forgecdn.net", file.Id, file.FileName) : file.DownloadUrl,
            FallbackDownloadUrls = CreateFallbackUrls(file.Id, file.FileName, file.DownloadUrl),
            Downloads = file.DownloadCount,
            PublishedAt = file.FileDate,
            GameVersions = NormalizeDistinct(file.GameVersions),
            Loaders = HasLoaderFacet(kind)
                ? NormalizeDistinct(file.SortableGameVersions.Select(version => TryMapLoader(version.ModLoader)))
                : [],
            RequiredDependencies = file.Dependencies
                .Where(value => value.RelationType == 3 && value.ModId > 0)
                .Select(value => value.ModId.ToString())
                .Where(id => !excludedIds.Contains(id) && seen.Add(id) && projects.ContainsKey(id))
                .Select(id => new ResourceProjectDependency { Project = projects[id], VersionId = string.Empty })
                .ToList()
        };
    }

    private static ResourceProject MapProject(CurseForgeMod mod, ResourceProjectKind kind) => new()
    {
        Source = ResourceProjectSource.CurseForge,
        Kind = kind,
        ProjectId = mod.Id.ToString(),
        Slug = mod.Slug,
        Title = mod.Name,
        Description = mod.Summary,
        IconUrl = mod.Logo?.ThumbnailUrl ?? mod.Logo?.Url,
        Downloads = mod.DownloadCount,
        SupportedMinecraftVersions = NormalizeDistinct(mod.LatestFilesIndexes.Select(value => value.GameVersion)
            .Concat(mod.GameVersionLatestFiles.Select(value => value.GameVersion))),
        SupportedLoaders = HasLoaderFacet(kind)
            ? NormalizeDistinct(mod.LatestFilesIndexes.Select(value => TryMapLoader(value.ModLoader))
                .Concat(mod.GameVersionLatestFiles.Select(value => TryMapLoader(value.ModLoader))))
            : [],
        ProjectUrl = mod.Links?.WebsiteUrl ?? (string.IsNullOrWhiteSpace(mod.Slug)
            ? string.Empty
            : $"https://www.curseforge.com/minecraft/{MapWebsitePath(kind)}/{mod.Slug}")
    };

    private static List<string> CollectRequiredProjectIds(IEnumerable<CurseForgeFile> files, ISet<string> excludedIds) => files
        .SelectMany(file => file.Dependencies)
        .Where(value => value.RelationType == 3 && value.ModId > 0)
        .Select(value => value.ModId.ToString())
        .Where(id => !excludedIds.Contains(id))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static HashSet<string> CreateCurrentIds(params string?[] ids) =>
        new(ids.Where(id => !string.IsNullOrWhiteSpace(id))!, StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<string> ResolveMinecraftVersions(ResourceCatalogSearchRequest request) =>
        request.MinecraftVersions.Count > 0
            ? NormalizeDistinct(request.MinecraftVersions)
            : string.IsNullOrWhiteSpace(request.MinecraftVersion) ? [] : [request.MinecraftVersion.Trim()];

    private static IReadOnlyList<string> NormalizeDistinct(IEnumerable<string?> values) => values
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value!.Trim().ToLowerInvariant())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static bool HasLoaderFacet(ResourceProjectKind kind) => kind is ResourceProjectKind.Mod or ResourceProjectKind.Modpack;

    private static bool TryMapLoader(LoaderKind loader, out CurseForgeLoader value)
    {
        value = loader switch
        {
            LoaderKind.Forge => CurseForgeLoader.Forge,
            LoaderKind.Fabric => CurseForgeLoader.Fabric,
            LoaderKind.Quilt => CurseForgeLoader.Quilt,
            LoaderKind.NeoForge => CurseForgeLoader.NeoForge,
            _ => default
        };
        return loader is not LoaderKind.Vanilla;
    }

    private static string? TryMapLoader(int? loader) => loader switch
    {
        1 => "forge", 4 => "fabric", 5 => "quilt", 6 => "neoforge", _ => null
    };

    private static int MapClassId(ResourceProjectKind kind) => kind switch
    {
        ResourceProjectKind.ResourcePack => ResourcePacksClassId,
        ResourceProjectKind.ShaderPack => ShaderPacksClassId,
        ResourceProjectKind.World => WorldsClassId,
        ResourceProjectKind.Modpack => ModpacksClassId,
        _ => ModsClassId
    };

    private static string MapWebsitePath(ResourceProjectKind kind) => kind switch
    {
        ResourceProjectKind.ResourcePack => "texture-packs",
        ResourceProjectKind.ShaderPack => "shaders",
        ResourceProjectKind.World => "worlds",
        ResourceProjectKind.Modpack => "modpacks",
        _ => "mc-mods"
    };

    private static string CreateProjectKey(ResourceProject project) =>
        !string.IsNullOrWhiteSpace(project.ProjectId) ? $"{project.Source}:{project.ProjectId}" : $"{project.Source}:slug:{project.Slug}";

    private static IReadOnlyList<string> CreateFallbackUrls(long id, string fileName, string? primary)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return [];
        return new[] { BuildCdnUrl("edge.forgecdn.net", id, fileName), BuildCdnUrl("mediafilez.forgecdn.net", id, fileName) }
            .Where(url => !string.Equals(url, primary, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildCdnUrl(string host, long id, string fileName) =>
        $"https://{host}/files/{id / 1000}/{id % 1000}/{Uri.EscapeDataString(fileName)}";

    private static string NormalizeCategory(string value) => string.IsNullOrWhiteSpace(value)
        ? string.Empty
        : new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static IReadOnlyList<string> ResolveCategoryAliases(ResourceProjectCategory category) => category switch
    {
        ResourceProjectCategory.Optimization => ["optimization", "performance"],
        ResourceProjectCategory.Utility => ["utility", "utilities", "qol", "quality of life", "miscellaneous"],
        ResourceProjectCategory.Adventure => ["adventure", "rpg", "adventure rpg", "adventure and rpg"],
        ResourceProjectCategory.Decoration => ["decoration", "decorative", "cosmetic"],
        ResourceProjectCategory.Equipment => ["equipment", "armor weapons tools", "armor", "weapons", "tools"],
        ResourceProjectCategory.Technology => ["technology", "tech"],
        ResourceProjectCategory.Magic => ["magic"],
        ResourceProjectCategory.Mobs => ["mobs", "creatures"],
        ResourceProjectCategory.WorldGeneration => ["worldgen", "world gen", "world generation", "biomes", "dimensions"],
        ResourceProjectCategory.Storage => ["storage"],
        ResourceProjectCategory.Library => ["library", "api", "library api", "api and library"],
        ResourceProjectCategory.Simplistic => ["simplistic", "simple", "16x"],
        ResourceProjectCategory.Themed => ["themed", "theme", "medieval", "modern", "fantasy"],
        ResourceProjectCategory.Realistic => ["realistic", "realism", "photorealistic", "128x", "256x", "512x"],
        ResourceProjectCategory.VanillaLike => ["vanilla like", "vanilla-like", "vanilla"],
        ResourceProjectCategory.Audio => ["audio", "sound", "music"],
        ResourceProjectCategory.Cartoon => ["cartoon"],
        ResourceProjectCategory.Cursed => ["cursed"],
        ResourceProjectCategory.Fantasy => ["fantasy"],
        ResourceProjectCategory.SemiRealistic => ["semi realistic", "semi-realistic", "semirealistic"],
        ResourceProjectCategory.Creation => ["creation", "creations", "building", "buildings", "creative"],
        ResourceProjectCategory.GameMap => ["game map", "game maps", "minigame", "mini game", "mini games"],
        ResourceProjectCategory.Parkour => ["parkour"],
        ResourceProjectCategory.Puzzle => ["puzzle", "puzzles"],
        ResourceProjectCategory.Survival => ["survival"],
        ResourceProjectCategory.Quests => ["quests", "questing", "quest"],
        ResourceProjectCategory.KitchenSink => ["kitchen sink", "kitchen-sink", "kitchensink"],
        ResourceProjectCategory.Lightweight => ["lightweight", "small", "light"],
        ResourceProjectCategory.Multiplayer => ["multiplayer", "server", "servers"],
        ResourceProjectCategory.Exploration => ["exploration", "explore"],
        _ => []
    };

    private enum CurseForgeLoader { Forge = 1, Fabric = 4, Quilt = 5, NeoForge = 6 }

    private sealed class SearchResponse
    {
        [JsonPropertyName("data")] public List<CurseForgeMod> Data { get; init; } = [];
        [JsonPropertyName("pagination")] public Pagination? Pagination { get; init; }
    }
    private sealed class FilesResponse
    {
        [JsonPropertyName("data")] public List<CurseForgeFile> Data { get; init; } = [];
        [JsonPropertyName("pagination")] public Pagination? Pagination { get; init; }
    }
    private sealed class CategoriesResponse { [JsonPropertyName("data")] public List<CurseForgeCategory> Data { get; init; } = []; }
    private sealed record ModsRequest([property: JsonPropertyName("modIds")] IReadOnlyList<long> ModIds);
    private sealed class Pagination { [JsonPropertyName("totalCount")] public int? TotalCount { get; init; } }
    private sealed class CurseForgeCategory
    {
        [JsonPropertyName("id")] public int Id { get; init; }
        [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
        [JsonPropertyName("slug")] public string Slug { get; init; } = string.Empty;
    }
    private sealed class CurseForgeMod
    {
        [JsonPropertyName("id")] public long Id { get; init; }
        [JsonPropertyName("classId")] public int? ClassId { get; init; }
        [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
        [JsonPropertyName("slug")] public string Slug { get; init; } = string.Empty;
        [JsonPropertyName("summary")] public string Summary { get; init; } = string.Empty;
        [JsonPropertyName("downloadCount")] public long DownloadCount { get; init; }
        [JsonPropertyName("links")] public Links? Links { get; init; }
        [JsonPropertyName("logo")] public Logo? Logo { get; init; }
        [JsonPropertyName("latestFilesIndexes")] public List<LatestFile> LatestFilesIndexes { get; init; } = [];
        [JsonPropertyName("gameVersionLatestFiles")] public List<LatestFile> GameVersionLatestFiles { get; init; } = [];
    }
    private sealed class Links { [JsonPropertyName("websiteUrl")] public string? WebsiteUrl { get; init; } }
    private sealed class Logo
    {
        [JsonPropertyName("thumbnailUrl")] public string? ThumbnailUrl { get; init; }
        [JsonPropertyName("url")] public string? Url { get; init; }
    }
    private sealed class LatestFile
    {
        [JsonPropertyName("gameVersion")] public string? GameVersion { get; init; }
        [JsonPropertyName("modLoader")] public int? ModLoader { get; init; }
    }
    private sealed class CurseForgeFile
    {
        [JsonPropertyName("id")] public long Id { get; init; }
        [JsonPropertyName("displayName")] public string DisplayName { get; init; } = string.Empty;
        [JsonPropertyName("fileName")] public string FileName { get; init; } = string.Empty;
        [JsonPropertyName("downloadUrl")] public string? DownloadUrl { get; init; }
        [JsonPropertyName("releaseType")] public int ReleaseType { get; init; }
        [JsonPropertyName("downloadCount")] public long DownloadCount { get; init; }
        [JsonPropertyName("fileDate")] public DateTimeOffset? FileDate { get; init; }
        [JsonPropertyName("gameVersions")] public List<string> GameVersions { get; init; } = [];
        [JsonPropertyName("sortableGameVersions")] public List<SortableVersion> SortableGameVersions { get; init; } = [];
        [JsonPropertyName("dependencies")] public List<FileDependency> Dependencies { get; init; } = [];
    }
    private sealed class SortableVersion { [JsonPropertyName("modLoader")] public int? ModLoader { get; init; } }
    private sealed class FileDependency
    {
        [JsonPropertyName("modId")] public long ModId { get; init; }
        [JsonPropertyName("relationType")] public int RelationType { get; init; }
    }
}
