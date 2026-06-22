using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Launcher.Infrastructure.Minecraft;

internal static class VanillaVersionComposer
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<string> CreateFinalVersionAsync(
        HttpClient httpClient,
        string minecraftVersion,
        string finalVersionName,
        string minecraftDirectory,
        DownloadSourcePreference downloadSourcePreference,
        int downloadSpeedLimitMbPerSecond = 0,
        IDownloadSpeedLimitState? downloadSpeedLimitState = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var finalVersionDirectory = Path.Combine(minecraftDirectory, "versions", finalVersionName);
        var finalVersionJsonPath = Path.Combine(finalVersionDirectory, $"{finalVersionName}.json");
        var finalVersionJarPath = Path.Combine(finalVersionDirectory, $"{finalVersionName}.jar");

        if (Directory.Exists(finalVersionDirectory))
            throw new IOException($"Version directory already exists: {finalVersionName}");

        var baseVersionJson = await DownloadBaseVersionJsonAsync(
            httpClient,
            minecraftVersion,
            downloadSourcePreference,
            downloadSpeedLimitMbPerSecond,
            downloadSpeedLimitState,
            logger,
            cancellationToken);
        var finalVersionJson = BuildFinalVersionJson(baseVersionJson, finalVersionName, minecraftVersion);

        Directory.CreateDirectory(finalVersionDirectory);

        try
        {
            await File.WriteAllTextAsync(
                finalVersionJsonPath,
                finalVersionJson.ToJsonString(JsonOptions),
                cancellationToken);

            await DownloadClientJarAsync(
                httpClient,
                baseVersionJson,
                finalVersionJarPath,
                downloadSourcePreference,
                downloadSpeedLimitMbPerSecond,
                downloadSpeedLimitState,
                logger,
                cancellationToken);
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
        string finalVersionName,
        string minecraftVersion)
    {
        var finalVersionJson = (JsonObject)baseVersionJson.DeepClone();
        finalVersionJson["id"] = finalVersionName;
        finalVersionJson["jar"] = finalVersionName;
        finalVersionJson.Remove("inheritsFrom");
        LauncherVersionMetadata.Apply(finalVersionJson, minecraftVersion);
        return finalVersionJson;
    }

    private static async Task<JsonObject> DownloadBaseVersionJsonAsync(
        HttpClient httpClient,
        string minecraftVersion,
        DownloadSourcePreference downloadSourcePreference,
        int downloadSpeedLimitMbPerSecond,
        IDownloadSpeedLimitState? downloadSpeedLimitState,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        return await VanillaVersionMetadataClient.DownloadVersionJsonAsync(
            httpClient,
            minecraftVersion,
            downloadSourcePreference,
            downloadSpeedLimitMbPerSecond,
            downloadSpeedLimitState,
            logger,
            cancellationToken);
    }

    private static async Task DownloadClientJarAsync(
        HttpClient httpClient,
        JsonObject baseVersionJson,
        string destinationJarPath,
        DownloadSourcePreference downloadSourcePreference,
        int downloadSpeedLimitMbPerSecond,
        IDownloadSpeedLimitState? downloadSpeedLimitState,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var clientUrl = VanillaVersionMetadataClient.GetClientJarUrl(baseVersionJson);
        if (string.IsNullOrWhiteSpace(clientUrl))
            throw new InvalidDataException("Minecraft version metadata is missing downloads.client.url.");

        var executor = new MinecraftDownloadRequestExecutor(
            httpClient,
            logger,
            DownloadBandwidthLimiter.Create(downloadSpeedLimitMbPerSecond, downloadSpeedLimitState),
            category: DownloadConcurrencyCategory.Runtime);
        using var jarResponse = await executor.GetAsync(
            clientUrl,
            downloadSourcePreference,
            categoryHint: "Mojang",
            cancellationToken);
        jarResponse.Response.EnsureSuccessStatusCode();
        await using var jarStream = await jarResponse.Response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destinationStream = File.Create(destinationJarPath);
        await jarStream.CopyToAsync(destinationStream, cancellationToken);
    }
}
