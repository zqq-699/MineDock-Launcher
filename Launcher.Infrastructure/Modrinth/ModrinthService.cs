using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Modrinth.Dto;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Modrinth;

public sealed class ModrinthService : IModrinthService
{
    private const string BaseUrl = "https://api.modrinth.com/v2";
    private const string FabricApiProjectSlug = "fabric-api";
    private const string FabricApiProjectId = "P7dR8mSH";
    private const string FabricApiTitle = "Fabric API";
    private const string QuiltStandardLibraryProjectSlug = "qsl";
    private const string QuiltStandardLibraryProjectId = "qvIfYCYJ";
    private const string QuiltStandardLibraryTitle = "QFAPI / QSL";
    private readonly HttpClient httpClient;
    private readonly ILogger<ModrinthService> logger;

    public ModrinthService(HttpClient? httpClient = null, ILogger<ModrinthService>? logger = null)
    {
        this.httpClient = httpClient ?? new HttpClient();
        this.logger = logger ?? NullLogger<ModrinthService>.Instance;
        if (!this.httpClient.DefaultRequestHeaders.UserAgent.Any())
            this.httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("MDL/0.1 (MineDock-Launcher)");
    }

    public async Task<IReadOnlyList<ModrinthProject>> SearchModsAsync(string query, string minecraftVersion, LoaderKind loader, CancellationToken cancellationToken = default)
    {
        var facets = new List<List<string>>
        {
            new() { "project_type:mod" }
        };

        if (!string.IsNullOrWhiteSpace(minecraftVersion))
            facets.Add(new List<string> { $"versions:{minecraftVersion}" });

        if (loader is not LoaderKind.Vanilla)
            facets.Add(new List<string> { $"categories:{loader.ToString().ToLowerInvariant()}" });

        logger.LogInformation(
            "Searching Modrinth mods. Query={Query} MinecraftVersion={MinecraftVersion} Loader={Loader}",
            query,
            minecraftVersion,
            loader);
        var url = $"{BaseUrl}/search?limit=24&query={Uri.EscapeDataString(query ?? string.Empty)}&facets={Uri.EscapeDataString(JsonSerializer.Serialize(facets))}";
        var response = await httpClient.GetFromJsonAsync<ModrinthSearchResponse>(url, cancellationToken);
        var projects = response?.Hits.Select(hit => new ModrinthProject
        {
            ProjectId = hit.ProjectId,
            Slug = hit.Slug,
            Title = hit.Title,
            Description = hit.Description,
            IconUrl = hit.IconUrl,
            Downloads = hit.Downloads
        }).ToList() ?? [];
        logger.LogInformation("Modrinth search completed. ResultCount={ResultCount}", projects.Count);
        return projects;
    }

    public async Task<IReadOnlyList<ModrinthVersionInfo>> GetFabricApiVersionsAsync(
        string minecraftVersion,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Loading Fabric API versions. MinecraftVersion={MinecraftVersion}",
            minecraftVersion);

        var versions = await GetCompatibleVersionsAsync(
            FabricApiProjectSlug,
            minecraftVersion,
            LoaderKind.Fabric,
            cancellationToken);
        var result = MapVersionInfos(versions);

        logger.LogInformation(
            "Loaded Fabric API versions. MinecraftVersion={MinecraftVersion} Count={Count}",
            minecraftVersion,
            result.Count);
        return result;
    }

    public async Task<IReadOnlyList<ModrinthVersionInfo>> GetQuiltStandardLibraryVersionsAsync(
        string minecraftVersion,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Loading Quilt standard library versions. MinecraftVersion={MinecraftVersion}",
            minecraftVersion);

        var versions = await GetCompatibleVersionsAsync(
            QuiltStandardLibraryProjectSlug,
            minecraftVersion,
            LoaderKind.Quilt,
            cancellationToken);
        var result = MapVersionInfos(versions);

        logger.LogInformation(
            "Loaded Quilt standard library versions. MinecraftVersion={MinecraftVersion} Count={Count}",
            minecraftVersion,
            result.Count);
        return result;
    }

    public async Task<string> InstallLatestCompatibleAsync(ModrinthProject project, GameInstance instance, IProgress<LauncherProgress>? progress, CancellationToken cancellationToken = default)
    {
        var loader = instance.Loader is LoaderKind.Vanilla ? "fabric" : instance.Loader.ToString().ToLowerInvariant();
        logger.LogInformation(
            "Installing compatible Modrinth project. ProjectId={ProjectId} MinecraftVersion={MinecraftVersion} Loader={Loader}",
            project.ProjectId,
            instance.MinecraftVersion,
            instance.Loader);
        var versions = await GetCompatibleVersionsAsync(project.ProjectId, instance.MinecraftVersion, loader, cancellationToken);
        var selected = versions.FirstOrDefault(version => version.Files.Count > 0);
        if (selected is null)
            throw new NoCompatibleModFileException(project.ProjectId, instance.MinecraftVersion, instance.Loader);

        return await InstallVersionFileAsync(project.ProjectId, project.Title, selected, instance, progress, cancellationToken);
    }

    public Task<string> InstallFabricApiAsync(GameInstance instance, IProgress<LauncherProgress>? progress, CancellationToken cancellationToken = default)
    {
        return InstallLatestCompatibleAsync(
            new ModrinthProject
            {
                ProjectId = FabricApiProjectSlug,
                Slug = FabricApiProjectSlug,
                Title = FabricApiTitle
            },
            instance,
            progress,
            cancellationToken);
    }

    public async Task<string> InstallFabricApiAsync(
        GameInstance instance,
        string versionId,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(versionId))
            throw new InvalidOperationException("Fabric API version id is required.");

        logger.LogInformation(
            "Installing Fabric API. VersionId={VersionId} MinecraftVersion={MinecraftVersion} InstanceId={InstanceId}",
            versionId,
            instance.MinecraftVersion,
            instance.Id);
        var version = await httpClient.GetFromJsonAsync<ModrinthVersion>(
            $"{BaseUrl}/version/{Uri.EscapeDataString(versionId)}",
            cancellationToken)
            ?? throw new InvalidOperationException($"Modrinth version metadata is empty: {versionId}");

        if (!string.Equals(version.ProjectId, FabricApiProjectId, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(version.ProjectId, FabricApiProjectSlug, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Modrinth version is not a Fabric API version: {versionId}");
        }

        return await InstallVersionFileAsync(
            FabricApiProjectSlug,
            FabricApiTitle,
            version,
            instance,
            progress,
            cancellationToken);
    }

    public async Task<string> InstallQuiltStandardLibraryAsync(
        GameInstance instance,
        string versionId,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(versionId))
            throw new InvalidOperationException("Quilt standard library version id is required.");

        logger.LogInformation(
            "Installing Quilt standard library. VersionId={VersionId} MinecraftVersion={MinecraftVersion} InstanceId={InstanceId}",
            versionId,
            instance.MinecraftVersion,
            instance.Id);
        var version = await httpClient.GetFromJsonAsync<ModrinthVersion>(
            $"{BaseUrl}/version/{Uri.EscapeDataString(versionId)}",
            cancellationToken)
            ?? throw new InvalidOperationException($"Modrinth version metadata is empty: {versionId}");

        if (!string.Equals(version.ProjectId, QuiltStandardLibraryProjectId, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(version.ProjectId, QuiltStandardLibraryProjectSlug, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Modrinth version is not a QFAPI/QSL version: {versionId}");
        }

        return await InstallVersionFileAsync(
            QuiltStandardLibraryProjectSlug,
            QuiltStandardLibraryTitle,
            version,
            instance,
            progress,
            cancellationToken);
    }

    private Task<List<ModrinthVersion>> GetCompatibleVersionsAsync(
        string projectIdOrSlug,
        string minecraftVersion,
        LoaderKind loader,
        CancellationToken cancellationToken)
    {
        var loaderName = loader is LoaderKind.Vanilla ? "fabric" : loader.ToString().ToLowerInvariant();
        return GetCompatibleVersionsAsync(projectIdOrSlug, minecraftVersion, loaderName, cancellationToken);
    }

    private async Task<List<ModrinthVersion>> GetCompatibleVersionsAsync(
        string projectIdOrSlug,
        string minecraftVersion,
        string loader,
        CancellationToken cancellationToken)
    {
        var versionsUrl = $"{BaseUrl}/project/{Uri.EscapeDataString(projectIdOrSlug)}/version?loaders={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { loader }))}&game_versions={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { minecraftVersion }))}";
        return await httpClient.GetFromJsonAsync<List<ModrinthVersion>>(versionsUrl, cancellationToken) ?? [];
    }

    private static List<ModrinthVersionInfo> MapVersionInfos(IEnumerable<ModrinthVersion> versions)
    {
        return versions
            .Where(version => !string.IsNullOrWhiteSpace(version.Id) && version.Files.Count > 0)
            .Select(version => new ModrinthVersionInfo
            {
                VersionId = version.Id,
                Name = string.IsNullOrWhiteSpace(version.Name) ? version.VersionNumber : version.Name,
                VersionNumber = version.VersionNumber,
                IsStable = string.Equals(version.VersionType, "release", StringComparison.OrdinalIgnoreCase)
            })
            .ToList();
    }

    private async Task<string> InstallVersionFileAsync(
        string projectId,
        string projectTitle,
        ModrinthVersion version,
        GameInstance instance,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (version.Files.Count == 0)
            throw new NoCompatibleModFileException(projectId, instance.MinecraftVersion, instance.Loader);

        var file = version.Files.FirstOrDefault(f => f.IsPrimary) ?? version.Files[0];
        var modsDirectory = Path.Combine(instance.InstanceDirectory, "mods");
        Directory.CreateDirectory(modsDirectory);
        var target = Path.Combine(modsDirectory, file.FileName);

        progress?.Report(new LauncherProgress(ModProgressStages.DownloadingFile, $"{projectTitle} {version.VersionNumber}"));
        await using var stream = await httpClient.GetStreamAsync(file.Url, cancellationToken);
        await using var destination = File.Create(target);
        await stream.CopyToAsync(destination, cancellationToken);
        logger.LogInformation(
            "Modrinth project installed. ProjectId={ProjectId} VersionId={VersionId} VersionNumber={VersionNumber} FileName={FileName} Target={Target}",
            projectId,
            version.Id,
            version.VersionNumber,
            file.FileName,
            target);
        return target;
    }
}
