using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Launcher.Infrastructure.Minecraft;

internal static class FabricVersionComposer
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<string> CreateFinalVersionAsync(
        HttpClient httpClient,
        string minecraftVersion,
        string loaderVersion,
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
        var fabricProfileJson = await DownloadFabricProfileJsonAsync(httpClient, minecraftVersion, loaderVersion, cancellationToken);
        var finalVersionJson = BuildFinalVersionJson(baseVersionJson, fabricProfileJson, finalVersionName, minecraftVersion);

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

    internal static JsonObject BuildFinalVersionJson(
        JsonObject baseVersionJson,
        JsonObject fabricProfileJson,
        string finalVersionName,
        string minecraftVersion)
    {
        return VersionJsonMergeHelper.MergeFlattenedVersion(
            baseVersionJson,
            fabricProfileJson,
            finalVersionName,
            minecraftVersion);
    }

    private static async Task<JsonObject> DownloadBaseVersionJsonAsync(
        HttpClient httpClient,
        string minecraftVersion,
        CancellationToken cancellationToken)
    {
        return await VanillaVersionMetadataClient.DownloadVersionJsonAsync(
            httpClient,
            minecraftVersion,
            cancellationToken);
    }

    private static async Task<JsonObject> DownloadFabricProfileJsonAsync(
        HttpClient httpClient,
        string minecraftVersion,
        string loaderVersion,
        CancellationToken cancellationToken)
    {
        var profileUrl = $"https://meta.fabricmc.net/v2/versions/loader/{minecraftVersion}/{loaderVersion}/profile/json";
        using var profileStream = await httpClient.GetStreamAsync(profileUrl, cancellationToken);
        var profileNode = await JsonNode.ParseAsync(profileStream, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException($"Fabric profile metadata is empty: {minecraftVersion} {loaderVersion}");
        return profileNode.AsObject();
    }

    private static async Task DownloadClientJarAsync(
        HttpClient httpClient,
        JsonObject baseVersionJson,
        string destinationJarPath,
        CancellationToken cancellationToken)
    {
        var clientUrl = VanillaVersionMetadataClient.GetClientJarUrl(baseVersionJson);
        if (string.IsNullOrWhiteSpace(clientUrl))
            throw new InvalidDataException("Minecraft version metadata is missing downloads.client.url.");

        await using var jarStream = await httpClient.GetStreamAsync(clientUrl, cancellationToken);
        await using var destinationStream = File.Create(destinationJarPath);
        await jarStream.CopyToAsync(destinationStream, cancellationToken);
    }

}
