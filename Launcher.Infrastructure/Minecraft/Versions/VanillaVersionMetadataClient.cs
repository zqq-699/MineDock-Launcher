using System.IO;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace Launcher.Infrastructure.Minecraft;

internal static class VanillaVersionMetadataClient
{
    private const string VersionManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    public static async Task<JsonObject> DownloadVersionJsonAsync(
        HttpClient httpClient,
        string minecraftVersion,
        CancellationToken cancellationToken = default)
    {
        using var manifestStream = await httpClient.GetStreamAsync(VersionManifestUrl, cancellationToken);
        var manifestNode = await JsonNode.ParseAsync(manifestStream, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("Minecraft version manifest is empty.");

        var versionEntries = manifestNode["versions"]?.AsArray()
            ?? throw new InvalidDataException("Minecraft version manifest is missing versions.");
        var versionUrl = versionEntries
            .Select(entry => entry?.AsObject())
            .FirstOrDefault(entry =>
                string.Equals(entry?["id"]?.GetValue<string>(), minecraftVersion, StringComparison.OrdinalIgnoreCase))?["url"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(versionUrl))
            throw new InvalidOperationException($"Minecraft version metadata not found: {minecraftVersion}");

        using var versionStream = await httpClient.GetStreamAsync(versionUrl, cancellationToken);
        var versionNode = await JsonNode.ParseAsync(versionStream, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException($"Minecraft version metadata is empty: {minecraftVersion}");
        return versionNode.AsObject();
    }

    public static string? GetClientJarUrl(JsonObject versionJson)
    {
        return versionJson["downloads"]?["client"]?["url"]?.GetValue<string>();
    }
}
