using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.CurseForge;
using Launcher.Infrastructure.FileSystem;
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
    private const int MinecraftResourcePacksClassId = 12;
    private const int MinecraftWorldsClassId = 17;
    private const int MinecraftModpacksClassId = 4471;
    private const int MinecraftShaderPacksClassId = 6552;
    private const int CurseForgeSortByTotalDownloads = 6;
    private const int CurseForgeSortByFileDate = 1;

    private readonly HttpClient httpClient;
    private readonly ICurseForgeApiKeyResolver curseForgeApiKeyResolver;
    private readonly ILocalSaveService localSaveService;
    private readonly ILogger<ResourceCatalogService> logger;
    private readonly object curseForgeCategoriesGate = new();
    private readonly Dictionary<ResourceProjectKind, Task<IReadOnlyList<CurseForgeCategory>>> curseForgeCategoriesTasks = [];

    public ResourceCatalogService(
        HttpClient? httpClient = null,
        LauncherPathProvider? pathProvider = null,
        ISettingsService? settingsService = null,
        ILogger<ResourceCatalogService>? logger = null,
        ICurseForgeApiKeyResolver? curseForgeApiKeyResolver = null,
        ILocalSaveService? localSaveService = null)
    {
        var resolvedPathProvider = pathProvider ?? new LauncherPathProvider();
        this.httpClient = httpClient ?? new HttpClient();
        this.logger = logger ?? NullLogger<ResourceCatalogService>.Instance;
        this.curseForgeApiKeyResolver = curseForgeApiKeyResolver
            ?? new CurseForgeApiKeyResolver(resolvedPathProvider, settingsService);
        this.localSaveService = localSaveService
            ?? new LocalSaveService(resolvedPathProvider);

        if (!this.httpClient.DefaultRequestHeaders.UserAgent.Any())
            this.httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Launcher/0.1 (offline-wpf-launcher)");
    }

    public async Task<ResourceCatalogSearchResult> SearchModsAsync(
        ResourceCatalogSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        return await SearchProjectsAsync(
                WithKind(request, ResourceProjectKind.Mod),
                cancellationToken)
            .ConfigureAwait(false);

        static ResourceCatalogSearchRequest WithKind(
            ResourceCatalogSearchRequest request,
            ResourceProjectKind kind)
        {
            return new ResourceCatalogSearchRequest
            {
                Kind = kind,
                Query = request.Query,
                MinecraftVersion = request.MinecraftVersion,
                MinecraftVersions = request.MinecraftVersions,
                Loader = request.Loader,
                Source = request.Source,
                Category = request.Category,
                Offset = request.Offset,
                PageSize = request.PageSize
            };
        }
    }

    public async Task<ResourceCatalogSearchResult> SearchProjectsAsync(
        ResourceCatalogSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Searching resource projects. Kind={Kind} Query={Query} MinecraftVersion={MinecraftVersion} MinecraftVersionCount={MinecraftVersionCount} Loader={Loader} Source={Source} Category={Category}",
            request.Kind,
            request.Query,
            request.MinecraftVersion,
            ResolveMinecraftVersions(request).Count,
            request.Loader,
            request.Source,
            request.Category);

        var projects = new List<ResourceProject>();
        var hasMore = false;
        var curseForgeUnavailable = false;
        var curseForgeApiKeyMissing = false;

        if (request.Kind is ResourceProjectKind.World
            && request.Source is ResourceProjectSource.Modrinth)
        {
            logger.LogInformation("Skipping Modrinth resource search because worlds are only available through CurseForge.");
            return new ResourceCatalogSearchResult();
        }

        if (request.Kind is not ResourceProjectKind.World
            && request.Source is null or ResourceProjectSource.Modrinth)
        {
            var modrinthResult = await SearchModrinthProjectsAsync(request, cancellationToken).ConfigureAwait(false);
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
                var curseForgeResult = await SearchCurseForgeProjectsAsync(request, apiKey, cancellationToken).ConfigureAwait(false);
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

        if (request.Kind is ResourceProjectKind.Mod
            && !request.IncludeAllVersions
            && request.Loader is LoaderKind.Vanilla)
        {
            return new ResourceProjectVersionsResult();
        }

        return request.Source switch
        {
            ResourceProjectSource.Modrinth => await GetModrinthProjectVersionsAsync(request, cancellationToken)
                .ConfigureAwait(false),
            ResourceProjectSource.CurseForge => await GetCurseForgeProjectVersionsAsync(request, cancellationToken)
                .ConfigureAwait(false),
            _ => new ResourceProjectVersionsResult()
        };
    }

    private async Task<ResourceCatalogSourceSearchResult> SearchModrinthProjectsAsync(
        ResourceCatalogSearchRequest request,
        CancellationToken cancellationToken)
    {
        var pageSize = NormalizePageSize(request.PageSize);
        var offset = NormalizeOffset(request.Offset);
        var facets = new List<List<string>>
        {
            new() { $"project_type:{MapModrinthProjectType(request.Kind)}" }
        };

        var minecraftVersions = ResolveMinecraftVersions(request);
        if (minecraftVersions.Count > 0)
            facets.Add(minecraftVersions.Select(version => $"versions:{version}").ToList());

        if (HasLoaderFacet(request.Kind) && request.Loader is not LoaderKind.Vanilla)
            facets.Add(new List<string> { $"categories:{request.Loader.ToString().ToLowerInvariant()}" });

        if (request.Category is { } category)
            facets.Add(new List<string> { $"categories:{MapModrinthCategory(category)}" });

        var url = $"{ModrinthBaseUrl}/search?limit={pageSize}&offset={offset}&index=downloads&query={Uri.EscapeDataString(request.Query ?? string.Empty)}&facets={Uri.EscapeDataString(JsonSerializer.Serialize(facets))}";
        var response = await httpClient.GetFromJsonAsync<ModrinthSearchResponse>(url, cancellationToken).ConfigureAwait(false);

        var projects = response?.Hits.Select(hit => new ResourceProject
        {
            Source = ResourceProjectSource.Modrinth,
            Kind = request.Kind,
            ProjectId = hit.ProjectId,
            Slug = hit.Slug,
            Title = hit.Title,
            Description = hit.Description,
            IconUrl = hit.IconUrl,
            Downloads = hit.Downloads,
            SupportedMinecraftVersions = NormalizeDistinct(hit.Versions),
            SupportedLoaders = HasLoaderFacet(request.Kind)
                ? NormalizeDistinct(hit.Categories.Where(KnownModrinthLoaders.Contains))
                : [],
            ProjectUrl = string.IsNullOrWhiteSpace(hit.Slug)
                ? string.Empty
                : $"https://modrinth.com/{MapModrinthProjectType(request.Kind)}/{hit.Slug}"
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
            var query = new List<string>
            {
                $"game_versions={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { request.MinecraftVersion }))}"
            };
            if (HasLoaderFacet(request.Kind))
            {
                var loader = request.Loader.ToString().ToLowerInvariant();
                query.Insert(0, $"loaders={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { loader }))}");
            }

            url += $"?{string.Join("&", query)}";
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
                        Kind = request.Kind,
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

        var pageSize = NormalizeVersionPageSize(request.PageSize);
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
        if (HasLoaderFacet(request.Kind)
            && !request.IncludeAllVersions
            && TryMapCurseForgeLoader(request.Loader, out var loaderType))
        {
            query.Add($"modLoaderType={(int)loaderType}");
        }

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
                .Where(file => file.Id > 0
                    && (!string.IsNullOrWhiteSpace(file.FileName)
                        || !string.IsNullOrWhiteSpace(file.DownloadUrl)))
                .Select(file => new ResourceProjectVersion
                {
                    VersionId = file.Id.ToString(),
                    Kind = request.Kind,
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
                    Loaders = HasLoaderFacet(request.Kind)
                        ? NormalizeDistinct(file.SortableGameVersions
                            .Select(version => TryMapCurseForgeLoader(version.ModLoader))
                            .Where(loader => !string.IsNullOrWhiteSpace(loader))!)
                        : []
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

        if (version.Kind is ResourceProjectKind.World)
            return await InstallWorldProjectVersionAsync(version, instance, cancellationToken).ConfigureAwait(false);

        var installDirectory = ResolveInstallDirectory(instance, version.Kind);
        Directory.CreateDirectory(installDirectory);
        var target = await DownloadProjectVersionCoreAsync(version, installDirectory, cancellationToken).ConfigureAwait(false);

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

    public Task<bool> ProjectVersionDownloadExistsAsync(
        ResourceProjectVersion version,
        string targetDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetDirectory))
            return Task.FromResult(false);

        var target = Path.Combine(targetDirectory, ResolveProjectVersionFileName(version));
        return Task.FromResult(File.Exists(target));
    }

    public Task<bool> ProjectVersionInstallExistsAsync(
        ResourceProjectVersion version,
        GameInstance instance,
        CancellationToken cancellationToken = default)
    {
        if (version.Kind is ResourceProjectKind.World)
            return Task.FromResult(false);

        if (string.IsNullOrWhiteSpace(instance.InstanceDirectory))
            return Task.FromResult(false);

        var target = Path.Combine(ResolveInstallDirectory(instance, version.Kind), ResolveProjectVersionFileName(version));
        return Task.FromResult(File.Exists(target));
    }

    private async Task<ResourceCatalogSourceSearchResult> SearchCurseForgeProjectsAsync(
        ResourceCatalogSearchRequest request,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var categoryId = await ResolveCurseForgeCategoryIdAsync(request.Category, request.Kind, apiKey, cancellationToken)
            .ConfigureAwait(false);
        if (request.Category.HasValue && categoryId is null)
        {
            logger.LogWarning(
                "Skipping CurseForge resource search because the selected category could not be resolved. Category={Category}",
                request.Category);
            return new ResourceCatalogSourceSearchResult([], HasMore: false);
        }

        var minecraftVersions = ResolveMinecraftVersions(request);
        if (minecraftVersions.Count == 0)
            return await SearchCurseForgeProjectsAsync(request, apiKey, null, categoryId, cancellationToken)
                .ConfigureAwait(false);

        var projects = new Dictionary<string, ResourceProject>(StringComparer.OrdinalIgnoreCase);
        var hasMore = false;
        foreach (var minecraftVersion in minecraftVersions)
        {
            var result = await SearchCurseForgeProjectsAsync(request, apiKey, minecraftVersion, categoryId, cancellationToken)
                .ConfigureAwait(false);
            hasMore |= result.HasMore;
            foreach (var project in result.Projects)
                projects.TryAdd(CreateProjectKey(project), project);
        }

        return new ResourceCatalogSourceSearchResult(projects.Values.ToList(), hasMore);
    }

    private async Task<ResourceCatalogSourceSearchResult> SearchCurseForgeProjectsAsync(
        ResourceCatalogSearchRequest request,
        string apiKey,
        string? minecraftVersion,
        int? categoryId,
        CancellationToken cancellationToken)
    {
        var pageSize = NormalizePageSize(request.PageSize);
        var offset = NormalizeOffset(request.Offset);
        var query = new List<string>
        {
            $"gameId={MinecraftGameId}",
            $"classId={MapCurseForgeClassId(request.Kind)}",
            $"sortField={CurseForgeSortByTotalDownloads}",
            "sortOrder=desc",
            $"pageSize={pageSize}",
            $"index={offset}"
        };

        if (!string.IsNullOrWhiteSpace(request.Query))
            query.Add($"searchFilter={Uri.EscapeDataString(request.Query)}");
        if (!string.IsNullOrWhiteSpace(minecraftVersion))
            query.Add($"gameVersion={Uri.EscapeDataString(minecraftVersion)}");
        if (HasLoaderFacet(request.Kind) && TryMapCurseForgeLoader(request.Loader, out var loaderType))
            query.Add($"modLoaderType={(int)loaderType}");
        if (categoryId.HasValue)
            query.Add($"categoryId={categoryId.Value}");

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
            Kind = request.Kind,
            ProjectId = mod.Id.ToString(),
            Slug = mod.Slug,
            Title = mod.Name,
            Description = mod.Summary,
            IconUrl = mod.Logo?.ThumbnailUrl ?? mod.Logo?.Url,
            Downloads = mod.DownloadCount,
            SupportedMinecraftVersions = ResolveCurseForgeMinecraftVersions(mod),
            SupportedLoaders = HasLoaderFacet(request.Kind) ? ResolveCurseForgeLoaders(mod) : [],
            ProjectUrl = mod.Links?.WebsiteUrl ?? (string.IsNullOrWhiteSpace(mod.Slug)
                ? string.Empty
                : $"https://www.curseforge.com/minecraft/{MapCurseForgeWebsitePath(request.Kind)}/{mod.Slug}")
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

    private async Task<int?> ResolveCurseForgeCategoryIdAsync(
        ResourceProjectCategory? category,
        ResourceProjectKind kind,
        string apiKey,
        CancellationToken cancellationToken)
    {
        if (!category.HasValue)
            return null;

        var aliases = ResolveCurseForgeCategoryAliases(category.Value)
            .Select(NormalizeCurseForgeCategoryText)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (aliases.Count == 0)
            return null;

        var categories = await GetCurseForgeCategoriesAsync(kind, apiKey, cancellationToken).ConfigureAwait(false);
        foreach (var candidate in categories)
        {
            if (ResolveCurseForgeCategoryCandidateKeys(candidate).Any(aliases.Contains))
                return candidate.Id;
        }

        return null;
    }

    private Task<IReadOnlyList<CurseForgeCategory>> GetCurseForgeCategoriesAsync(
        ResourceProjectKind kind,
        string apiKey,
        CancellationToken cancellationToken)
    {
        lock (curseForgeCategoriesGate)
        {
            if (!curseForgeCategoriesTasks.TryGetValue(kind, out var task)
                || task.IsCanceled
                || task.IsFaulted)
            {
                task = LoadCurseForgeCategoriesAsync(kind, apiKey, cancellationToken);
                curseForgeCategoriesTasks[kind] = task;
            }

            return task;
        }
    }

    private async Task<IReadOnlyList<CurseForgeCategory>> LoadCurseForgeCategoriesAsync(
        ResourceProjectKind kind,
        string apiKey,
        CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"{CurseForgeBaseUrl}/categories?gameId={MinecraftGameId}&classId={MapCurseForgeClassId(kind)}");
        httpRequest.Headers.TryAddWithoutValidation("Accept", "application/json");
        httpRequest.Headers.TryAddWithoutValidation("x-api-key", apiKey);

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            logger.LogWarning(
                "CurseForge resource categories were rejected. StatusCode={StatusCode}",
                (int)response.StatusCode);
            return [];
        }

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<CurseForgeCategoriesResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return payload?.Data ?? [];
    }

    private static IEnumerable<string> ResolveCurseForgeCategoryCandidateKeys(CurseForgeCategory category)
    {
        foreach (var value in new[] { category.Slug, category.Name })
        {
            var normalized = NormalizeCurseForgeCategoryText(value);
            if (!string.IsNullOrWhiteSpace(normalized))
                yield return normalized;

            var lastSegment = ResolveLastCategorySegment(value);
            normalized = NormalizeCurseForgeCategoryText(lastSegment);
            if (!string.IsNullOrWhiteSpace(normalized))
                yield return normalized;
        }
    }

    private static string ResolveLastCategorySegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var segments = value.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 0 ? value : segments[^1];
    }

    private static string NormalizeCurseForgeCategoryText(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
    }

    private static IReadOnlyList<string> ResolveCurseForgeCategoryAliases(ResourceProjectCategory category)
    {
        return category switch
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
    }

    private static string MapModrinthCategory(ResourceProjectCategory category)
    {
        return category switch
        {
            ResourceProjectCategory.Optimization => "optimization",
            ResourceProjectCategory.Utility => "utility",
            ResourceProjectCategory.Adventure => "adventure",
            ResourceProjectCategory.Decoration => "decoration",
            ResourceProjectCategory.Equipment => "equipment",
            ResourceProjectCategory.Technology => "technology",
            ResourceProjectCategory.Magic => "magic",
            ResourceProjectCategory.Mobs => "mobs",
            ResourceProjectCategory.WorldGeneration => "worldgen",
            ResourceProjectCategory.Storage => "storage",
            ResourceProjectCategory.Library => "library",
            ResourceProjectCategory.Simplistic => "simplistic",
            ResourceProjectCategory.Themed => "themed",
            ResourceProjectCategory.Realistic => "realistic",
            ResourceProjectCategory.VanillaLike => "vanilla-like",
            ResourceProjectCategory.Audio => "audio",
            ResourceProjectCategory.Cartoon => "cartoon",
            ResourceProjectCategory.Cursed => "cursed",
            ResourceProjectCategory.Fantasy => "fantasy",
            ResourceProjectCategory.SemiRealistic => "semi-realistic",
            ResourceProjectCategory.Creation => "creation",
            ResourceProjectCategory.GameMap => "game-map",
            ResourceProjectCategory.Parkour => "parkour",
            ResourceProjectCategory.Puzzle => "puzzle",
            ResourceProjectCategory.Survival => "survival",
            ResourceProjectCategory.Quests => "quests",
            ResourceProjectCategory.KitchenSink => "kitchen-sink",
            ResourceProjectCategory.Lightweight => "lightweight",
            ResourceProjectCategory.Multiplayer => "multiplayer",
            ResourceProjectCategory.Exploration => "exploration",
            _ => string.Empty
        };
    }

    private static string MapModrinthProjectType(ResourceProjectKind kind)
    {
        return kind switch
        {
            ResourceProjectKind.ResourcePack => "resourcepack",
            ResourceProjectKind.ShaderPack => "shader",
            ResourceProjectKind.World => "world",
            ResourceProjectKind.Modpack => "modpack",
            _ => "mod"
        };
    }

    private static int MapCurseForgeClassId(ResourceProjectKind kind)
    {
        return kind switch
        {
            ResourceProjectKind.ResourcePack => MinecraftResourcePacksClassId,
            ResourceProjectKind.ShaderPack => MinecraftShaderPacksClassId,
            ResourceProjectKind.World => MinecraftWorldsClassId,
            ResourceProjectKind.Modpack => MinecraftModpacksClassId,
            _ => MinecraftModsClassId
        };
    }

    private static string MapCurseForgeWebsitePath(ResourceProjectKind kind)
    {
        return kind switch
        {
            ResourceProjectKind.ResourcePack => "texture-packs",
            ResourceProjectKind.ShaderPack => "shaders",
            ResourceProjectKind.World => "worlds",
            ResourceProjectKind.Modpack => "modpacks",
            _ => "mc-mods"
        };
    }

    private static bool HasLoaderFacet(ResourceProjectKind kind)
    {
        return kind is ResourceProjectKind.Mod or ResourceProjectKind.Modpack;
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

    private static int NormalizeVersionPageSize(int pageSize)
    {
        return Math.Clamp(pageSize, 1, 10000);
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
        var fileName = ResolveProjectVersionFileName(version);
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

    private static string ResolveProjectVersionFileName(ResourceProjectVersion version)
    {
        var fileName = Path.GetFileName(version.FileName);
        return string.IsNullOrWhiteSpace(fileName)
            ? $"{version.VersionId}{ResolveDefaultFileExtension(version.Kind)}"
            : fileName;
    }

    private static string ResolveDefaultFileExtension(ResourceProjectKind kind)
    {
        return kind switch
        {
            ResourceProjectKind.Modpack => ".mrpack",
            ResourceProjectKind.ResourcePack or ResourceProjectKind.ShaderPack or ResourceProjectKind.World => ".zip",
            _ => ".jar"
        };
    }

    private static string ResolveInstallDirectory(GameInstance instance, ResourceProjectKind kind)
    {
        var directoryName = kind switch
        {
            ResourceProjectKind.ResourcePack => "resourcepacks",
            ResourceProjectKind.ShaderPack => "shaderpacks",
            ResourceProjectKind.World => "saves",
            _ => "mods"
        };

        return Path.Combine(instance.InstanceDirectory, directoryName);
    }

    private async Task<string> InstallWorldProjectVersionAsync(
        ResourceProjectVersion version,
        GameInstance instance,
        CancellationToken cancellationToken)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"launcher-world-install-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var archivePath = await DownloadProjectVersionCoreAsync(version, tempDirectory, cancellationToken)
                .ConfigureAwait(false);
            var result = await localSaveService.ImportFromArchiveAsync(instance, archivePath, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess || result.ImportedSave is null)
            {
                throw new InvalidOperationException(
                    $"Failed to import world archive. FailureReason={result.FailureReason}");
            }

            logger.LogInformation(
                "Resource world version installed. VersionId={VersionId} SaveDirectory={SaveDirectory}",
                version.VersionId,
                result.ImportedSave.FullPath);
            return result.ImportedSave.FullPath;
        }
        finally
        {
            SafeDeleteDirectory(tempDirectory);
        }
    }

    private void SafeDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Failed to delete temporary resource world directory. Directory={Directory}", directory);
        }
    }

    private async Task DownloadFileAsync(string url, string target, CancellationToken cancellationToken)
    {
        await using var stream = await httpClient.GetStreamAsync(url, cancellationToken).ConfigureAwait(false);
        await using var destination = File.Create(target);
        await stream.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<string> CreateCurseForgeFallbackUrls(long fileId, string fileName, string? primaryUrl)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return [];

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

    private sealed class CurseForgeCategoriesResponse
    {
        [JsonPropertyName("data")]
        public List<CurseForgeCategory> Data { get; init; } = [];
    }

    private sealed class CurseForgeCategory
    {
        [JsonPropertyName("id")]
        public int Id { get; init; }

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("slug")]
        public string Slug { get; init; } = string.Empty;
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
