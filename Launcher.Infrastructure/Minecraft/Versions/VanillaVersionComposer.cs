using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Launcher.Infrastructure.Minecraft;

internal static class VanillaVersionComposer
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<string> CreateFinalVersionAsync(
        HttpClient httpClient,
        string minecraftVersion,
        string finalVersionName,
        string minecraftDirectory,
        CancellationToken cancellationToken = default)
    {
        var finalVersionDirectory = Path.Combine(minecraftDirectory, "versions", finalVersionName);
        var finalVersionJsonPath = Path.Combine(finalVersionDirectory, $"{finalVersionName}.json");
        var finalVersionJarPath = Path.Combine(finalVersionDirectory, $"{finalVersionName}.jar");

        if (Directory.Exists(finalVersionDirectory))
            throw new IOException($"Version directory already exists: {finalVersionName}");

        var baseVersionJson = await DownloadBaseVersionJsonAsync(httpClient, minecraftVersion, cancellationToken);
        var finalVersionJson = BuildFinalVersionJson(baseVersionJson, finalVersionName);

        Directory.CreateDirectory(finalVersionDirectory);

        try
        {
            await File.WriteAllTextAsync(
                finalVersionJsonPath,
                finalVersionJson.ToJsonString(JsonOptions),
                cancellationToken);

            await DownloadClientJarAsync(httpClient, baseVersionJson, finalVersionJarPath, cancellationToken);
        }
        catch
        {
            if (Directory.Exists(finalVersionDirectory))
                Directory.Delete(finalVersionDirectory, recursive: true);

            throw;
        }

        return finalVersionName;
    }

    internal static JsonObject BuildFinalVersionJson(JsonObject baseVersionJson, string finalVersionName)
    {
        var finalVersionJson = (JsonObject)baseVersionJson.DeepClone();
        finalVersionJson["id"] = finalVersionName;
        finalVersionJson["jar"] = finalVersionName;
        finalVersionJson.Remove("inheritsFrom");
        return finalVersionJson;
    }

    private static async Task<JsonObject> DownloadBaseVersionJsonAsync(
        HttpClient httpClient,
        string minecraftVersion,
        CancellationToken cancellationToken)
    {
        using var manifestStream = await httpClient.GetStreamAsync(
            "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json",
            cancellationToken);
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

    private static async Task DownloadClientJarAsync(
        HttpClient httpClient,
        JsonObject baseVersionJson,
        string destinationJarPath,
        CancellationToken cancellationToken)
    {
        var clientUrl = baseVersionJson["downloads"]?["client"]?["url"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(clientUrl))
            throw new InvalidDataException("Minecraft version metadata is missing downloads.client.url.");

        await using var jarStream = await httpClient.GetStreamAsync(clientUrl, cancellationToken);
        await using var destinationStream = File.Create(destinationJarPath);
        await jarStream.CopyToAsync(destinationStream, cancellationToken);
    }
}
