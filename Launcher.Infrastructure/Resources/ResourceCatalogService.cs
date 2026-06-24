using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.CurseForge;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Resources;

public sealed class ResourceCatalogService : IResourceCatalogService
{
    private static readonly HashSet<string> KnownModrinthLoaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "fabric",
        "forge",
        "neoforge",
        "quilt"
    };

    private const string ModrinthBaseUrl = "https://api.modrinth.com/v2";
    private const string CurseForgeBaseUrl = "https://api.curseforge.com/v1";
    private const int MinecraftGameId = 432;
    private const int MinecraftModsClassId = 6;
    private const int CurseForgeSortByTotalDownloads = 6;

    private readonly HttpClient httpClient;
    private readonly ICurseForgeApiKeyResolver curseForgeApiKeyResolver;
    private readonly ILogger<ResourceCatalogService> logger;

    public ResourceCatalogService(
        HttpClient? httpClient = null,
        LauncherPathProvider? pathProvider = null,
        ISettingsService? settingsService = null,
        ILogger<ResourceCatalogService>? logger = null,
        ICurseForgeApiKeyResolver? curseForgeApiKeyResolver = null)
    {
        this.httpClient = httpClient ?? new HttpClient();
        this.logger = logger ?? NullLogger<ResourceCatalogService>.Instance;
        this.curseForgeApiKeyResolver = curseForgeApiKeyResolver
            ?? new CurseForgeApiKeyResolver(pathProvider ?? new LauncherPathProvider(), settingsService);

        if (!this.httpClient.DefaultRequestHeaders.UserAgent.Any())
            this.httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Launcher/0.1 (offline-wpf-launcher)");
    }

    public async Task<ResourceCatalogSearchResult> SearchModsAsync(
        ResourceCatalogSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Searching resource mods. Query={Query} MinecraftVersion={MinecraftVersion} MinecraftVersionCount={MinecraftVersionCount} Loader={Loader} Source={Source}",
            request.Query,
            request.MinecraftVersion,
            ResolveMinecraftVersions(request).Count,
            request.Loader,
            request.Source);

        var projects = new List<ResourceProject>();
        var hasMore = false;
        var curseForgeUnavailable = false;
        var curseForgeApiKeyMissing = false;

        if (request.Source is null or ResourceProjectSource.Modrinth)
        {
            var modrinthResult = await SearchModrinthModsAsync(request, cancellationToken).ConfigureAwait(false);
            projects.AddRange(modrinthResult.Projects);
            hasMore |= modrinthResult.HasMore;
        }

        if (request.Source is null or ResourceProjectSource.CurseForge)
        {
            var apiKey = await curseForgeApiKeyResolver.TryResolveAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                curseForgeUnavailable = true;
                curseForgeApiKeyMissing = true;
                logger.LogWarning("Skipping CurseForge resource search because API key is not configured.");
            }
            else
            {
                var curseForgeResult = await SearchCurseForgeModsAsync(request, apiKey, cancellationToken).ConfigureAwait(false);
                projects.AddRange(curseForgeResult.Projects);
                hasMore |= curseForgeResult.HasMore;
            }
        }

        return new ResourceCatalogSearchResult
        {
            Projects = projects
                .OrderByDescending(project => project.Downloads)
                .ThenBy(project => project.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            IsCurseForgeUnavailable = curseForgeUnavailable,
            IsCurseForgeApiKeyMissing = curseForgeApiKeyMissing,
            HasMore = hasMore
        };
    }

    private async Task<ResourceCatalogSourceSearchResult> SearchModrinthModsAsync(
        ResourceCatalogSearchRequest request,
        CancellationToken cancellationToken)
    {
        var pageSize = NormalizePageSize(request.PageSize);
        var offset = NormalizeOffset(request.Offset);
        var facets = new List<List<string>>
        {
            new() { "project_type:mod" }
        };

        var minecraftVersions = ResolveMinecraftVersions(request);
        if (minecraftVersions.Count > 0)
            facets.Add(minecraftVersions.Select(version => $"versions:{version}").ToList());

        if (request.Loader is not LoaderKind.Vanilla)
            facets.Add(new List<string> { $"categories:{request.Loader.ToString().ToLowerInvariant()}" });

        var url = $"{ModrinthBaseUrl}/search?limit={pageSize}&offset={offset}&index=downloads&query={Uri.EscapeDataString(request.Query ?? string.Empty)}&facets={Uri.EscapeDataString(JsonSerializer.Serialize(facets))}";
        var response = await httpClient.GetFromJsonAsync<ModrinthSearchResponse>(url, cancellationToken).ConfigureAwait(false);

        var projects = response?.Hits.Select(hit => new ResourceProject
        {
            Source = ResourceProjectSource.Modrinth,
            ProjectId = hit.ProjectId,
            Slug = hit.Slug,
            Title = hit.Title,
            Description = hit.Description,
            IconUrl = hit.IconUrl,
            Downloads = hit.Downloads,
            SupportedMinecraftVersions = NormalizeDistinct(hit.Versions),
            SupportedLoaders = NormalizeDistinct(hit.Categories.Where(KnownModrinthLoaders.Contains)),
            ProjectUrl = string.IsNullOrWhiteSpace(hit.Slug)
                ? string.Empty
                : $"https://modrinth.com/mod/{hit.Slug}"
        }).ToList() ?? [];

        return new ResourceCatalogSourceSearchResult(
            projects,
            HasMore(response?.TotalHits, offset, projects.Count, pageSize));
    }

    private async Task<ResourceCatalogSourceSearchResult> SearchCurseForgeModsAsync(
        ResourceCatalogSearchRequest request,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var minecraftVersions = ResolveMinecraftVersions(request);
        if (minecraftVersions.Count == 0)
            return await SearchCurseForgeModsAsync(request, apiKey, minecraftVersion: null, cancellationToken)
                .ConfigureAwait(false);

        var projects = new Dictionary<string, ResourceProject>(StringComparer.OrdinalIgnoreCase);
        var hasMore = false;
        foreach (var minecraftVersion in minecraftVersions)
        {
            var result = await SearchCurseForgeModsAsync(request, apiKey, minecraftVersion, cancellationToken)
                .ConfigureAwait(false);
            hasMore |= result.HasMore;
            foreach (var project in result.Projects)
                projects.TryAdd(CreateProjectKey(project), project);
        }

        return new ResourceCatalogSourceSearchResult(projects.Values.ToList(), hasMore);
    }

    private async Task<ResourceCatalogSourceSearchResult> SearchCurseForgeModsAsync(
        ResourceCatalogSearchRequest request,
        string apiKey,
        string? minecraftVersion,
        CancellationToken cancellationToken)
    {
        var pageSize = NormalizePageSize(request.PageSize);
        var offset = NormalizeOffset(request.Offset);
        var query = new List<string>
        {
            $"gameId={MinecraftGameId}",
            $"classId={MinecraftModsClassId}",
            $"sortField={CurseForgeSortByTotalDownloads}",
            "sortOrder=desc",
            $"pageSize={pageSize}",
            $"index={offset}"
        };

        if (!string.IsNullOrWhiteSpace(request.Query))
            query.Add($"searchFilter={Uri.EscapeDataString(request.Query)}");
        if (!string.IsNullOrWhiteSpace(minecraftVersion))
            query.Add($"gameVersion={Uri.EscapeDataString(minecraftVersion)}");
        if (TryMapCurseForgeLoader(request.Loader, out var loaderType))
            query.Add($"modLoaderType={(int)loaderType}");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, $"{CurseForgeBaseUrl}/mods/search?{string.Join("&", query)}");
        httpRequest.Headers.TryAddWithoutValidation("Accept", "application/json");
        httpRequest.Headers.TryAddWithoutValidation("x-api-key", apiKey);

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            logger.LogWarning(
                "CurseForge resource search was rejected. StatusCode={StatusCode}",
                (int)response.StatusCode);
            return new ResourceCatalogSourceSearchResult([], HasMore: false);
        }

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<CurseForgeSearchResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var projects = payload?.Data.Select(mod => new ResourceProject
        {
            Source = ResourceProjectSource.CurseForge,
            ProjectId = mod.Id.ToString(),
            Slug = mod.Slug,
            Title = mod.Name,
            Description = mod.Summary,
            IconUrl = mod.Logo?.ThumbnailUrl ?? mod.Logo?.Url,
            Downloads = mod.DownloadCount,
            SupportedMinecraftVersions = ResolveCurseForgeMinecraftVersions(mod),
            SupportedLoaders = ResolveCurseForgeLoaders(mod),
            ProjectUrl = mod.Links?.WebsiteUrl ?? (string.IsNullOrWhiteSpace(mod.Slug)
                ? string.Empty
                : $"https://www.curseforge.com/minecraft/mc-mods/{mod.Slug}")
        }).ToList() ?? [];

        return new ResourceCatalogSourceSearchResult(
            projects,
            HasMore(payload?.Pagination?.TotalCount, offset, projects.Count, pageSize));
    }

    private static IReadOnlyList<string> ResolveMinecraftVersions(ResourceCatalogSearchRequest request)
    {
        if (request.MinecraftVersions.Count > 0)
            return NormalizeDistinct(request.MinecraftVersions);

        return string.IsNullOrWhiteSpace(request.MinecraftVersion)
            ? []
            : [request.MinecraftVersion.Trim()];
    }

    private static string CreateProjectKey(ResourceProject project)
    {
        if (!string.IsNullOrWhiteSpace(project.ProjectId))
            return $"{project.Source}:{project.ProjectId}";

        if (!string.IsNullOrWhiteSpace(project.Slug))
            return $"{project.Source}:slug:{project.Slug}";

        return $"{project.Source}:title:{project.Title}";
    }

    private static int NormalizeOffset(int offset)
    {
        return Math.Max(0, offset);
    }

    private static int NormalizePageSize(int pageSize)
    {
        return Math.Clamp(pageSize, 1, 50);
    }

    private static bool HasMore(int? totalCount, int offset, int resultCount, int pageSize)
    {
        if (totalCount.HasValue)
            return offset + resultCount < totalCount.Value;

        return resultCount >= pageSize;
    }

    private static IReadOnlyList<string> ResolveCurseForgeMinecraftVersions(CurseForgeMod mod)
    {
        return NormalizeDistinct(
            mod.LatestFilesIndexes.Select(index => index.GameVersion)
                .Concat(mod.GameVersionLatestFiles.Select(file => file.GameVersion)));
    }

    private static IReadOnlyList<string> ResolveCurseForgeLoaders(CurseForgeMod mod)
    {
        return NormalizeDistinct(
            mod.LatestFilesIndexes.Select(index => TryMapCurseForgeLoader(index.ModLoader))
                .Concat(mod.GameVersionLatestFiles.Select(file => TryMapCurseForgeLoader(file.ModLoader)))
                .Where(loader => !string.IsNullOrWhiteSpace(loader))!);
    }

    private static IReadOnlyList<string> NormalizeDistinct(IEnumerable<string?> values)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? TryMapCurseForgeLoader(int? loader)
    {
        return loader switch
        {
            (int)CurseForgeModLoaderType.Forge => "forge",
            (int)CurseForgeModLoaderType.Fabric => "fabric",
            (int)CurseForgeModLoaderType.Quilt => "quilt",
            (int)CurseForgeModLoaderType.NeoForge => "neoforge",
            _ => null
        };
    }

    private static bool TryMapCurseForgeLoader(LoaderKind loader, out CurseForgeModLoaderType loaderType)
    {
        loaderType = loader switch
        {
            LoaderKind.Forge => CurseForgeModLoaderType.Forge,
            LoaderKind.Fabric => CurseForgeModLoaderType.Fabric,
            LoaderKind.Quilt => CurseForgeModLoaderType.Quilt,
            LoaderKind.NeoForge => CurseForgeModLoaderType.NeoForge,
            _ => default
        };
        return loader is not LoaderKind.Vanilla;
    }

    private enum CurseForgeModLoaderType
    {
        Any = 0,
        Forge = 1,
        Cauldron = 2,
        LiteLoader = 3,
        Fabric = 4,
        Quilt = 5,
        NeoForge = 6
    }

    private sealed class ModrinthSearchResponse
    {
        [JsonPropertyName("hits")]
        public List<ModrinthHit> Hits { get; init; } = [];

        [JsonPropertyName("total_hits")]
        public int? TotalHits { get; init; }
    }

    private sealed class ModrinthHit
    {
        [JsonPropertyName("project_id")]
        public string ProjectId { get; init; } = string.Empty;

        [JsonPropertyName("slug")]
        public string Slug { get; init; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; init; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; init; } = string.Empty;

        [JsonPropertyName("icon_url")]
        public string? IconUrl { get; init; }

        [JsonPropertyName("downloads")]
        public long Downloads { get; init; }

        [JsonPropertyName("versions")]
        public List<string> Versions { get; init; } = [];

        [JsonPropertyName("categories")]
        public List<string> Categories { get; init; } = [];
    }

    private sealed class CurseForgeSearchResponse
    {
        [JsonPropertyName("data")]
        public List<CurseForgeMod> Data { get; init; } = [];

        [JsonPropertyName("pagination")]
        public CurseForgePagination? Pagination { get; init; }
    }

    private sealed class CurseForgeMod
    {
        [JsonPropertyName("id")]
        public long Id { get; init; }

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("slug")]
        public string Slug { get; init; } = string.Empty;

        [JsonPropertyName("summary")]
        public string Summary { get; init; } = string.Empty;

        [JsonPropertyName("downloadCount")]
        public long DownloadCount { get; init; }

        [JsonPropertyName("links")]
        public CurseForgeModLinks? Links { get; init; }

        [JsonPropertyName("logo")]
        public CurseForgeModLogo? Logo { get; init; }

        [JsonPropertyName("latestFilesIndexes")]
        public List<CurseForgeLatestFileIndex> LatestFilesIndexes { get; init; } = [];

        [JsonPropertyName("gameVersionLatestFiles")]
        public List<CurseForgeGameVersionLatestFile> GameVersionLatestFiles { get; init; } = [];
    }

    private sealed class CurseForgeModLinks
    {
        [JsonPropertyName("websiteUrl")]
        public string? WebsiteUrl { get; init; }
    }

    private sealed class CurseForgeModLogo
    {
        [JsonPropertyName("thumbnailUrl")]
        public string? ThumbnailUrl { get; init; }

        [JsonPropertyName("url")]
        public string? Url { get; init; }
    }

    private sealed class CurseForgeLatestFileIndex
    {
        [JsonPropertyName("gameVersion")]
        public string? GameVersion { get; init; }

        [JsonPropertyName("modLoader")]
        public int? ModLoader { get; init; }
    }

    private sealed class CurseForgeGameVersionLatestFile
    {
        [JsonPropertyName("gameVersion")]
        public string? GameVersion { get; init; }

        [JsonPropertyName("modLoader")]
        public int? ModLoader { get; init; }
    }

    private sealed class CurseForgePagination
    {
        [JsonPropertyName("totalCount")]
        public int? TotalCount { get; init; }
    }

    private sealed record ResourceCatalogSourceSearchResult(
        IReadOnlyList<ResourceProject> Projects,
        bool HasMore);
}
