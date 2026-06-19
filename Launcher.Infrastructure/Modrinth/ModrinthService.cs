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
    private const string FabricApiTitle = "Fabric API";
    private readonly HttpClient httpClient;
    private readonly ILogger<ModrinthService> logger;

    public ModrinthService(HttpClient? httpClient = null, ILogger<ModrinthService>? logger = null)
    {
        this.httpClient = httpClient ?? new HttpClient();
        this.logger = logger ?? NullLogger<ModrinthService>.Instance;
        if (!this.httpClient.DefaultRequestHeaders.UserAgent.Any())
            this.httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Launcher/0.1 (offline-wpf-launcher)");
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

    public async Task<string> InstallLatestCompatibleAsync(ModrinthProject project, GameInstance instance, IProgress<LauncherProgress>? progress, CancellationToken cancellationToken = default)
    {
        var loader = instance.Loader is LoaderKind.Vanilla ? "fabric" : instance.Loader.ToString().ToLowerInvariant();
        logger.LogInformation(
            "Installing compatible Modrinth project. ProjectId={ProjectId} MinecraftVersion={MinecraftVersion} Loader={Loader}",
            project.ProjectId,
            instance.MinecraftVersion,
            instance.Loader);
        var versionsUrl = $"{BaseUrl}/project/{Uri.EscapeDataString(project.ProjectId)}/version?loaders={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { loader }))}&game_versions={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { instance.MinecraftVersion }))}";
        var versions = await httpClient.GetFromJsonAsync<List<ModrinthVersion>>(versionsUrl, cancellationToken) ?? [];
        var selected = versions.FirstOrDefault(version => version.Files.Count > 0);
        if (selected is null)
            throw new NoCompatibleModFileException(project.ProjectId, instance.MinecraftVersion, instance.Loader);

        var file = selected.Files.FirstOrDefault(f => f.IsPrimary) ?? selected.Files[0];
        var modsDirectory = Path.Combine(instance.InstanceDirectory, "mods");
        Directory.CreateDirectory(modsDirectory);
        var target = Path.Combine(modsDirectory, file.FileName);

        progress?.Report(new LauncherProgress(ModProgressStages.DownloadingFile, $"{project.Title} {selected.VersionNumber}"));
        await using var stream = await httpClient.GetStreamAsync(file.Url, cancellationToken);
        await using var destination = File.Create(target);
        await stream.CopyToAsync(destination, cancellationToken);
        logger.LogInformation(
            "Modrinth project installed. ProjectId={ProjectId} VersionNumber={VersionNumber} FileName={FileName} Target={Target}",
            project.ProjectId,
            selected.VersionNumber,
            file.FileName,
            target);
        return target;
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
}
