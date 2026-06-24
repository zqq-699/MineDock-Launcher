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
    private const string ModrinthBaseUrl = "https://api.modrinth.com/v2";
    private const string CurseForgeBaseUrl = "https://api.curseforge.com/v1";
    private const int MinecraftGameId = 432;
    private const int MinecraftModsClassId = 6;
    private const int CurseForgeSortByTotalDownloads = 6;
    private const int PageSize = 20;

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
            "Searching resource mods. Query={Query} MinecraftVersion={MinecraftVersion} Loader={Loader} Source={Source}",
            request.Query,
            request.MinecraftVersion,
            request.Loader,
            request.Source);

        var projects = new List<ResourceProject>();
        var curseForgeUnavailable = false;
        var curseForgeApiKeyMissing = false;

        if (request.Source is null or ResourceProjectSource.Modrinth)
            projects.AddRange(await SearchModrinthModsAsync(request, cancellationToken).ConfigureAwait(false));

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
                projects.AddRange(await SearchCurseForgeModsAsync(request, apiKey, cancellationToken).ConfigureAwait(false));
            }
        }

        return new ResourceCatalogSearchResult
        {
            Projects = projects
                .OrderByDescending(project => project.Downloads)
                .ThenBy(project => project.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            IsCurseForgeUnavailable = curseForgeUnavailable,
            IsCurseForgeApiKeyMissing = curseForgeApiKeyMissing
        };
    }

    private async Task<IReadOnlyList<ResourceProject>> SearchModrinthModsAsync(
        ResourceCatalogSearchRequest request,
        CancellationToken cancellationToken)
    {
        var facets = new List<List<string>>
        {
            new() { "project_type:mod" }
        };

        if (!string.IsNullOrWhiteSpace(request.MinecraftVersion))
            facets.Add(new List<string> { $"versions:{request.MinecraftVersion}" });

        if (request.Loader is not LoaderKind.Vanilla)
            facets.Add(new List<string> { $"categories:{request.Loader.ToString().ToLowerInvariant()}" });

        var url = $"{ModrinthBaseUrl}/search?limit={PageSize}&index=downloads&query={Uri.EscapeDataString(request.Query ?? string.Empty)}&facets={Uri.EscapeDataString(JsonSerializer.Serialize(facets))}";
        var response = await httpClient.GetFromJsonAsync<ModrinthSearchResponse>(url, cancellationToken).ConfigureAwait(false);

        return response?.Hits.Select(hit => new ResourceProject
        {
            Source = ResourceProjectSource.Modrinth,
            ProjectId = hit.ProjectId,
            Slug = hit.Slug,
            Title = hit.Title,
            Description = hit.Description,
            IconUrl = hit.IconUrl,
            Downloads = hit.Downloads,
            ProjectUrl = string.IsNullOrWhiteSpace(hit.Slug)
                ? string.Empty
                : $"https://modrinth.com/mod/{hit.Slug}"
        }).ToList() ?? [];
    }

    private async Task<IReadOnlyList<ResourceProject>> SearchCurseForgeModsAsync(
        ResourceCatalogSearchRequest request,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var query = new List<string>
        {
            $"gameId={MinecraftGameId}",
            $"classId={MinecraftModsClassId}",
            $"sortField={CurseForgeSortByTotalDownloads}",
            "sortOrder=desc",
            $"pageSize={PageSize}"
        };

        if (!string.IsNullOrWhiteSpace(request.Query))
            query.Add($"searchFilter={Uri.EscapeDataString(request.Query)}");
        if (!string.IsNullOrWhiteSpace(request.MinecraftVersion))
            query.Add($"gameVersion={Uri.EscapeDataString(request.MinecraftVersion)}");
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
            return [];
        }

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<CurseForgeSearchResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return payload?.Data.Select(mod => new ResourceProject
        {
            Source = ResourceProjectSource.CurseForge,
            ProjectId = mod.Id.ToString(),
            Slug = mod.Slug,
            Title = mod.Name,
            Description = mod.Summary,
            IconUrl = mod.Logo?.ThumbnailUrl ?? mod.Logo?.Url,
            Downloads = mod.DownloadCount,
            ProjectUrl = mod.Links?.WebsiteUrl ?? (string.IsNullOrWhiteSpace(mod.Slug)
                ? string.Empty
                : $"https://www.curseforge.com/minecraft/mc-mods/{mod.Slug}")
        }).ToList() ?? [];
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
    }

    private sealed class CurseForgeSearchResponse
    {
        [JsonPropertyName("data")]
        public List<CurseForgeMod> Data { get; init; } = [];
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
}
