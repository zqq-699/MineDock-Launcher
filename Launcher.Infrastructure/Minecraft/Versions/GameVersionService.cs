using System.Text.Json.Nodes;
using System.Net.Http;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Minecraft;

public sealed class GameVersionService : IGameVersionService
{
    private readonly HttpClient httpClient;
    private readonly IDownloadSpeedLimitState? downloadSpeedLimitState;
    private readonly ILogger<GameVersionService> logger;

    public GameVersionService(
        HttpClient? httpClient = null,
        IDownloadSpeedLimitState? downloadSpeedLimitState = null,
        ILogger<GameVersionService>? logger = null)
    {
        this.httpClient = httpClient ?? new HttpClient();
        this.downloadSpeedLimitState = downloadSpeedLimitState;
        this.logger = logger ?? NullLogger<GameVersionService>.Instance;
    }

    public async Task<IReadOnlyList<MinecraftVersionInfo>> GetVersionsAsync(
        DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
        CancellationToken cancellationToken = default,
        int downloadSpeedLimitMbPerSecond = 0)
    {
        var bandwidthLimiter = DownloadBandwidthLimiter.Create(downloadSpeedLimitMbPerSecond, downloadSpeedLimitState);
        var executor = new MinecraftDownloadRequestExecutor(
            httpClient,
            logger,
            bandwidthLimiter,
            category: DownloadConcurrencyCategory.Metadata);
        using var manifestResponse = await executor.GetAsync(
            "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json",
            downloadSourcePreference,
            categoryHint: "Mojang",
            cancellationToken);
        manifestResponse.Response.EnsureSuccessStatusCode();

        await using var stream = await manifestResponse.Response.Content.ReadAsStreamAsync(cancellationToken);
        var manifestNode = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Minecraft version manifest is empty.");
        var versionEntries = manifestNode["versions"]?.AsArray()
            ?? throw new InvalidOperationException("Minecraft version manifest is missing versions.");

        var versions = versionEntries
            .Select(entry => entry?.AsObject())
            .Where(entry => entry is not null)
            .Select(entry => new MinecraftVersionInfo(
                entry!["id"]?.GetValue<string>() ?? string.Empty,
                entry["type"]?.GetValue<string>() ?? string.Empty,
                false,
                entry["releaseTime"]?.GetValue<DateTimeOffset?>()))
            .Where(version => !string.IsNullOrWhiteSpace(version.Name))
            .OrderBy(v => VersionTypeRank(v.Type))
            .ThenByDescending(v => v.Type.Equals("Release", StringComparison.OrdinalIgnoreCase) ? VersionSortKey(v.Name) : new Version(0, 0))
            .ThenByDescending(v => v.ReleaseTime ?? DateTimeOffset.MinValue)
            .ThenByDescending(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        logger.LogInformation(
            "Minecraft versions loaded. RequestedSourcePreference={RequestedSourcePreference} ResolvedSourceKind={ResolvedSourceKind} VersionCount={VersionCount}",
            downloadSourcePreference,
            manifestResponse.Resolution.ResolvedSourceKind,
            versions.Count);
        return versions;
    }

    private static int VersionTypeRank(string type)
    {
        return type.ToLowerInvariant() switch
        {
            "release" => 0,
            "snapshot" => 1,
            "old_beta" => 2,
            "old_alpha" => 3,
            _ => 4
        };
    }

    private static Version VersionSortKey(string name)
    {
        var clean = name.Split('-', StringSplitOptions.RemoveEmptyEntries)[0];
        return Version.TryParse(clean, out var version) ? version : new Version(0, 0);
    }
}
