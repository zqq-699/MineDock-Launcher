using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Modrinth.Dto;

namespace Launcher.Infrastructure.Modrinth;

public sealed class ModrinthService : IModrinthService
{
    private const string BaseUrl = "https://api.modrinth.com/v2";
    private readonly HttpClient httpClient;

    public ModrinthService(HttpClient? httpClient = null)
    {
        this.httpClient = httpClient ?? new HttpClient();
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

        var url = $"{BaseUrl}/search?limit=24&query={Uri.EscapeDataString(query ?? string.Empty)}&facets={Uri.EscapeDataString(JsonSerializer.Serialize(facets))}";
        var response = await httpClient.GetFromJsonAsync<ModrinthSearchResponse>(url, cancellationToken);
        return response?.Hits.Select(hit => new ModrinthProject
        {
            ProjectId = hit.ProjectId,
            Slug = hit.Slug,
            Title = hit.Title,
            Description = hit.Description,
            IconUrl = hit.IconUrl,
            Downloads = hit.Downloads
        }).ToList() ?? [];
    }

    public async Task<string> InstallLatestCompatibleAsync(ModrinthProject project, GameInstance instance, IProgress<LauncherProgress>? progress, CancellationToken cancellationToken = default)
    {
        var loader = instance.Loader is LoaderKind.Vanilla ? "fabric" : instance.Loader.ToString().ToLowerInvariant();
        var versionsUrl = $"{BaseUrl}/project/{Uri.EscapeDataString(project.ProjectId)}/version?loaders={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { loader }))}&game_versions={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { instance.MinecraftVersion }))}";
        var versions = await httpClient.GetFromJsonAsync<List<ModrinthVersion>>(versionsUrl, cancellationToken) ?? [];
        var selected = versions.FirstOrDefault(version => version.Files.Count > 0);
        if (selected is null)
            throw new InvalidOperationException("没有找到与当前游戏兼容的 Mod 文件。");

        var file = selected.Files.FirstOrDefault(f => f.IsPrimary) ?? selected.Files[0];
        var modsDirectory = Path.Combine(instance.InstanceDirectory, "mods");
        Directory.CreateDirectory(modsDirectory);
        var target = Path.Combine(modsDirectory, file.FileName);

        progress?.Report(new LauncherProgress("Modrinth", $"正在下载 {project.Title} {selected.VersionNumber}"));
        await using var stream = await httpClient.GetStreamAsync(file.Url, cancellationToken);
        await using var destination = File.Create(target);
        await stream.CopyToAsync(destination, cancellationToken);
        return target;
    }
}
