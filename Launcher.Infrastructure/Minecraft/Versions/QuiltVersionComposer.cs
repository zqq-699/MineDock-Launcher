using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Launcher.Infrastructure.Minecraft;

internal static class QuiltVersionComposer
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<string> CreateFinalVersionAsync(
        HttpClient httpClient,
        string minecraftVersion,
        string loaderVersion,
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

        var baseVersionJson = await VanillaVersionMetadataClient.DownloadVersionJsonAsync(
            httpClient,
            minecraftVersion,
            downloadSourcePreference,
            downloadSpeedLimitMbPerSecond,
            downloadSpeedLimitState,
            logger,
            cancellationToken);
        var quiltProfileJson = await DownloadQuiltProfileJsonAsync(
            httpClient,
            minecraftVersion,
            loaderVersion,
            downloadSourcePreference,
            downloadSpeedLimitMbPerSecond,
            downloadSpeedLimitState,
            logger,
            cancellationToken);
        var finalVersionJson = BuildFinalVersionJson(baseVersionJson, quiltProfileJson, finalVersionName, minecraftVersion);

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
        JsonObject quiltProfileJson,
        string finalVersionName,
        string minecraftVersion)
    {
        return VersionJsonMergeHelper.MergeFlattenedVersion(
            baseVersionJson,
            quiltProfileJson,
            finalVersionName,
            minecraftVersion);
    }

    private static async Task<JsonObject> DownloadQuiltProfileJsonAsync(
        HttpClient httpClient,
        string minecraftVersion,
        string loaderVersion,
        DownloadSourcePreference downloadSourcePreference,
        int downloadSpeedLimitMbPerSecond,
        IDownloadSpeedLimitState? downloadSpeedLimitState,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var profileUrl = $"https://meta.quiltmc.org/v3/versions/loader/{minecraftVersion}/{loaderVersion}/profile/json";
        var executor = new MinecraftDownloadRequestExecutor(
            httpClient,
            logger,
            DownloadBandwidthLimiter.Create(downloadSpeedLimitMbPerSecond, downloadSpeedLimitState),
            category: DownloadConcurrencyCategory.Metadata);
        using var profileResponse = await executor.GetAsync(
            profileUrl,
            downloadSourcePreference,
            categoryHint: "Quilt",
            cancellationToken);
        profileResponse.Response.EnsureSuccessStatusCode();
        await using var profileStream = await profileResponse.Response.Content.ReadAsStreamAsync(cancellationToken);
        var profileNode = await JsonNode.ParseAsync(profileStream, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException($"Quilt profile metadata is empty: {minecraftVersion} {loaderVersion}");
        return profileNode.AsObject();
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
