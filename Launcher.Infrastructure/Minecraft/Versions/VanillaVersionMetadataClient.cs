using System.IO;
using System.Net.Http;
using System.Text.Json.Nodes;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Launcher.Infrastructure.Minecraft;

internal static class VanillaVersionMetadataClient
{
    private const string VersionManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    public static async Task<JsonObject> DownloadVersionJsonAsync(
        HttpClient httpClient,
        string minecraftVersion,
        DownloadSourcePreference downloadSourcePreference,
        int downloadSpeedLimitMbPerSecond = 0,
        IDownloadSpeedLimitState? downloadSpeedLimitState = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var executor = new MinecraftDownloadRequestExecutor(
            httpClient,
            logger,
            DownloadBandwidthLimiter.Create(downloadSpeedLimitMbPerSecond, downloadSpeedLimitState),
            category: DownloadConcurrencyCategory.Metadata);
        var versionUrl = await executor.ExecuteAsync(
            VersionManifestUrl,
            downloadSourcePreference,
            categoryHint: "Mojang",
            async (context, token) =>
            {
                await using var manifestStream = await context.Response.Content.ReadAsStreamAsync(token);
                var manifestNode = await JsonNode.ParseAsync(manifestStream, cancellationToken: token);
                if (manifestNode is not JsonObject manifestObject
                    || manifestObject["versions"] is not JsonArray versionEntries)
                {
                    throw new DownloadContentValidationException(
                        "Minecraft version manifest is missing a versions array.");
                }

                if (versionEntries.Any(entry => !IsValidManifestEntry(entry)))
                {
                    throw new DownloadContentValidationException(
                        "Minecraft version manifest contains an invalid version entry.");
                }

                var resolvedVersionUrl = FindVersionUrl(versionEntries, minecraftVersion);
                if (string.IsNullOrWhiteSpace(resolvedVersionUrl))
                {
                    throw new DownloadContentValidationException(
                        $"Minecraft version manifest does not contain {minecraftVersion}.");
                }

                return resolvedVersionUrl;
            },
            cancellationToken);

        return await executor.ExecuteAsync(
            versionUrl,
            downloadSourcePreference,
            categoryHint: "Mojang",
            async (context, token) =>
            {
                await using var versionStream = await context.Response.Content.ReadAsStreamAsync(token);
                var versionNode = await JsonNode.ParseAsync(versionStream, cancellationToken: token);
                if (versionNode is not JsonObject versionObject)
                {
                    throw new DownloadContentValidationException(
                        $"Minecraft version metadata is not a JSON object: {minecraftVersion}");
                }

                return versionObject;
            },
            cancellationToken);
    }

    public static string? GetClientJarUrl(JsonObject versionJson)
    {
        return versionJson["downloads"]?["client"]?["url"]?.GetValue<string>();
    }

    private static string? FindVersionUrl(JsonArray versionEntries, string minecraftVersion)
    {
        foreach (var entry in versionEntries.OfType<JsonObject>())
        {
            if (entry["id"] is not JsonValue idValue
                || !idValue.TryGetValue<string>(out var id)
                || !string.Equals(id, minecraftVersion, StringComparison.OrdinalIgnoreCase)
                || entry["url"] is not JsonValue urlValue
                || !urlValue.TryGetValue<string>(out var url)
                || string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            return url;
        }

        return null;
    }

    private static bool IsValidManifestEntry(JsonNode? entry)
    {
        return entry is JsonObject versionObject
            && versionObject["id"] is JsonValue idValue
            && idValue.TryGetValue<string>(out var id)
            && !string.IsNullOrWhiteSpace(id)
            && versionObject["url"] is JsonValue urlValue
            && urlValue.TryGetValue<string>(out var url)
            && !string.IsNullOrWhiteSpace(url);
    }

    public static string? GetClientJarSha1(JsonObject versionJson)
    {
        return versionJson["downloads"]?["client"]?["sha1"]?.GetValue<string>();
    }

    public static long? GetClientJarSize(JsonObject versionJson)
    {
        return versionJson["downloads"]?["client"]?["size"]?.GetValue<long?>();
    }
}
