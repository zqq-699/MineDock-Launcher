using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.IO;
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
    private const int CurseForgeSortByFileDate = 1;

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

    public async Task<ResourceProjectVersionsResult> GetProjectVersionsAsync(
        ResourceProjectVersionsRequest request,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Loading resource project versions. Source={Source} ProjectId={ProjectId} MinecraftVersion={MinecraftVersion} Loader={Loader} IncludeAllVersions={IncludeAllVersions}",
            request.Source,
            request.ProjectId,
            request.MinecraftVersion,
            request.Loader,
            request.IncludeAllVersions);

        if (!request.IncludeAllVersions && request.Loader is LoaderKind.Vanilla)
            return new ResourceProjectVersionsResult();

        return request.Source switch
        {
            ResourceProjectSource.Modrinth => await GetModrinthProjectVersionsAsync(request, cancellationToken)
                .ConfigureAwait(false),
            ResourceProjectSource.CurseForge => await GetCurseForgeProjectVersionsAsync(request, cancellationToken)
                .ConfigureAwait(false),
            _ => new ResourceProjectVersionsResult()
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

    private async Task<ResourceProjectVersionsResult> GetModrinthProjectVersionsAsync(
        ResourceProjectVersionsRequest request,
        CancellationToken cancellationToken)
    {
        var projectIdOrSlug = string.IsNullOrWhiteSpace(request.ProjectId)
            ? request.Slug
            : request.ProjectId;
        if (string.IsNullOrWhiteSpace(projectIdOrSlug)
            || (!request.IncludeAllVersions && string.IsNullOrWhiteSpace(request.MinecraftVersion)))
            return new ResourceProjectVersionsResult();

        var url = $"{ModrinthBaseUrl}/project/{Uri.EscapeDataString(projectIdOrSlug)}/version";
        if (!request.IncludeAllVersions)
        {
            var loader = request.Loader.ToString().ToLowerInvariant();
            url += $"?loaders={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { loader }))}&game_versions={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { request.MinecraftVersion }))}";
        }

        var versions = await httpClient.GetFromJsonAsync<List<ModrinthProjectVersion>>(url, cancellationToken)
            .ConfigureAwait(false) ?? [];

        return new ResourceProjectVersionsResult
        {
            Versions = versions
                .Where(version => !string.IsNullOrWhiteSpace(version.Id) && version.Files.Count > 0)
                .Select(version =>
                {
                    var file = version.Files.FirstOrDefault(candidate => candidate.IsPrimary) ?? version.Files[0];
                    return new ResourceProjectVersion
                    {
                        VersionId = version.Id,
                        Name = string.IsNullOrWhiteSpace(version.Name) ? version.VersionNumber : version.Name,
                        VersionNumber = version.VersionNumber,
                        VersionType = version.VersionType,
                        FileName = file.FileName,
                        PrimaryDownloadUrl = file.Url,
                        Downloads = version.Downloads,
                        PublishedAt = version.DatePublished,
                        GameVersions = NormalizeDistinct(version.GameVersions),
                        Loaders = NormalizeDistinct(version.Loaders)
                    };
                })
                .ToList()
        };
    }

    private async Task<ResourceProjectVersionsResult> GetCurseForgeProjectVersionsAsync(
        ResourceProjectVersionsRequest request,
        CancellationToken cancellationToken)
    {
        if (!long.TryParse(request.ProjectId, out var projectId)
            || (!request.IncludeAllVersions && string.IsNullOrWhiteSpace(request.MinecraftVersion)))
            return new ResourceProjectVersionsResult();

        var apiKey = await curseForgeApiKeyResolver.TryResolveAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("Skipping CurseForge resource versions because API key is not configured.");
            return new ResourceProjectVersionsResult
            {
                IsCurseForgeUnavailable = true,
                IsCurseForgeApiKeyMissing = true
            };
        }

        var pageSize = NormalizePageSize(request.PageSize);
        var offset = NormalizeOffset(request.Offset);
        var query = new List<string>
        {
            $"pageSize={pageSize}",
            $"index={offset}",
            $"sortField={CurseForgeSortByFileDate}",
            "sortOrder=desc"
        };
        if (!request.IncludeAllVersions)
            query.Insert(0, $"gameVersion={Uri.EscapeDataString(request.MinecraftVersion)}");
        if (!request.IncludeAllVersions && TryMapCurseForgeLoader(request.Loader, out var loaderType))
            query.Add($"modLoaderType={(int)loaderType}");

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"{CurseForgeBaseUrl}/mods/{projectId}/files?{string.Join("&", query)}");
        httpRequest.Headers.TryAddWithoutValidation("Accept", "application/json");
        httpRequest.Headers.TryAddWithoutValidation("x-api-key", apiKey);

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            logger.LogWarning(
                "CurseForge resource versions were rejected. ProjectId={ProjectId} StatusCode={StatusCode}",
                projectId,
                (int)response.StatusCode);
            return new ResourceProjectVersionsResult { IsCurseForgeUnavailable = true };
        }

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<CurseForgeFilesResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var files = payload?.Data ?? [];

        return new ResourceProjectVersionsResult
        {
            Versions = files
                .Where(file => file.Id > 0 && !string.IsNullOrWhiteSpace(file.FileName))
                .Select(file => new ResourceProjectVersion
                {
                    VersionId = file.Id.ToString(),
                    Name = string.IsNullOrWhiteSpace(file.DisplayName) ? file.FileName : file.DisplayName,
                    VersionNumber = string.IsNullOrWhiteSpace(file.DisplayName) ? file.FileName : file.DisplayName,
                    VersionType = MapCurseForgeReleaseType(file.ReleaseType),
                    FileName = file.FileName,
                    PrimaryDownloadUrl = string.IsNullOrWhiteSpace(file.DownloadUrl)
                        ? BuildCurseForgeCdnUrl("edge.forgecdn.net", file.Id, file.FileName)
                        : file.DownloadUrl,
                    FallbackDownloadUrls = CreateCurseForgeFallbackUrls(file.Id, file.FileName, file.DownloadUrl),
                    Downloads = file.DownloadCount,
                    PublishedAt = file.FileDate,
                    GameVersions = NormalizeDistinct(file.GameVersions),
                    Loaders = NormalizeDistinct(file.SortableGameVersions
                        .Select(version => TryMapCurseForgeLoader(version.ModLoader))
                        .Where(loader => !string.IsNullOrWhiteSpace(loader))!)
                })
                .ToList(),
            HasMore = HasMore(payload?.Pagination?.TotalCount, offset, files.Count, pageSize)
        };
    }

    public async Task<string> InstallProjectVersionAsync(
        ResourceProjectVersion version,
        GameInstance instance,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(instance.InstanceDirectory))
            throw new InvalidOperationException("The target instance directory is empty.");

        var modsDirectory = Path.Combine(instance.InstanceDirectory, "mods");
        Directory.CreateDirectory(modsDirectory);
        var target = await DownloadProjectVersionCoreAsync(version, modsDirectory, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Resource project version installed. VersionId={VersionId} Target={Target}",
            version.VersionId,
            target);
        return target;
    }

    public async Task<string> DownloadProjectVersionAsync(
        ResourceProjectVersion version,
        string targetDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetDirectory))
            throw new InvalidOperationException("The target download directory is empty.");

        Directory.CreateDirectory(targetDirectory);
        var target = await DownloadProjectVersionCoreAsync(version, targetDirectory, cancellationToken)
            .ConfigureAwait(false);

        logger.LogInformation(
            "Resource project version downloaded. VersionId={VersionId} Target={Target}",
            version.VersionId,
            target);
        return target;
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

    private static string MapCurseForgeReleaseType(int releaseType)
    {
        return releaseType switch
        {
            1 => "release",
            2 => "beta",
            3 => "alpha",
            _ => string.Empty
        };
    }

    private async Task<string> DownloadProjectVersionCoreAsync(
        ResourceProjectVersion version,
        string targetDirectory,
        CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(version.FileName);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = $"{version.VersionId}.jar";

        var target = Path.Combine(targetDirectory, fileName);
        var urls = new[] { version.PrimaryDownloadUrl }
            .Concat(version.FallbackDownloadUrls)
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (urls.Count == 0)
            throw new InvalidOperationException($"Resource project version has no download URL: {version.VersionId}");

        Exception? lastException = null;
        foreach (var url in urls)
        {
            try
            {
                await DownloadFileAsync(url, target, cancellationToken).ConfigureAwait(false);
                return target;
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException)
            {
                lastException = exception;
                logger.LogWarning(
                    exception,
                    "Failed to download resource project version candidate. VersionId={VersionId} Url={Url}",
                    version.VersionId,
                    url);
            }
        }

        throw new InvalidOperationException($"Failed to download resource project version: {version.VersionId}", lastException);
    }

    private async Task DownloadFileAsync(string url, string target, CancellationToken cancellationToken)
    {
        await using var stream = await httpClient.GetStreamAsync(url, cancellationToken).ConfigureAwait(false);
        await using var destination = File.Create(target);
        await stream.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<string> CreateCurseForgeFallbackUrls(long fileId, string fileName, string? primaryUrl)
    {
        var urls = new List<string>();
        AddDistinctUrl(urls, BuildCurseForgeCdnUrl("edge.forgecdn.net", fileId, fileName), primaryUrl);
        AddDistinctUrl(urls, BuildCurseForgeCdnUrl("mediafilez.forgecdn.net", fileId, fileName), primaryUrl);
        return urls;
    }

    private static string BuildCurseForgeCdnUrl(string host, long fileId, string fileName)
    {
        var part1 = fileId / 1000;
        var part2 = fileId % 1000;
        return $"https://{host}/files/{part1}/{part2}/{Uri.EscapeDataString(fileName)}";
    }

    private static void AddDistinctUrl(ICollection<string> urls, string candidateUrl, string? primaryUrl)
    {
        if (string.IsNullOrWhiteSpace(candidateUrl)
            || string.Equals(candidateUrl, primaryUrl, StringComparison.OrdinalIgnoreCase)
            || urls.Contains(candidateUrl, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        urls.Add(candidateUrl);
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

    private sealed class ModrinthProjectVersion
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("version_number")]
        public string VersionNumber { get; init; } = string.Empty;

        [JsonPropertyName("version_type")]
        public string VersionType { get; init; } = string.Empty;

        [JsonPropertyName("date_published")]
        public DateTimeOffset? DatePublished { get; init; }

        [JsonPropertyName("downloads")]
        public long Downloads { get; init; }

        [JsonPropertyName("game_versions")]
        public List<string> GameVersions { get; init; } = [];

        [JsonPropertyName("loaders")]
        public List<string> Loaders { get; init; } = [];

        [JsonPropertyName("files")]
        public List<ModrinthProjectVersionFile> Files { get; init; } = [];
    }

    private sealed class ModrinthProjectVersionFile
    {
        [JsonPropertyName("filename")]
        public string FileName { get; init; } = string.Empty;

        [JsonPropertyName("url")]
        public string Url { get; init; } = string.Empty;

        [JsonPropertyName("primary")]
        public bool IsPrimary { get; init; }
    }

    private sealed class CurseForgeSearchResponse
    {
        [JsonPropertyName("data")]
        public List<CurseForgeMod> Data { get; init; } = [];

        [JsonPropertyName("pagination")]
        public CurseForgePagination? Pagination { get; init; }
    }

    private sealed class CurseForgeFilesResponse
    {
        [JsonPropertyName("data")]
        public List<CurseForgeFile> Data { get; init; } = [];

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

    private sealed class CurseForgeFile
    {
        [JsonPropertyName("id")]
        public long Id { get; init; }

        [JsonPropertyName("displayName")]
        public string DisplayName { get; init; } = string.Empty;

        [JsonPropertyName("fileName")]
        public string FileName { get; init; } = string.Empty;

        [JsonPropertyName("downloadUrl")]
        public string? DownloadUrl { get; init; }

        [JsonPropertyName("releaseType")]
        public int ReleaseType { get; init; }

        [JsonPropertyName("downloadCount")]
        public long DownloadCount { get; init; }

        [JsonPropertyName("fileDate")]
        public DateTimeOffset? FileDate { get; init; }

        [JsonPropertyName("gameVersions")]
        public List<string> GameVersions { get; init; } = [];

        [JsonPropertyName("sortableGameVersions")]
        public List<CurseForgeSortableGameVersion> SortableGameVersions { get; init; } = [];
    }

    private sealed class CurseForgeSortableGameVersion
    {
        [JsonPropertyName("gameVersion")]
        public string? GameVersion { get; init; }

        [JsonPropertyName("gameVersionTypeId")]
        public int? GameVersionTypeId { get; init; }

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
