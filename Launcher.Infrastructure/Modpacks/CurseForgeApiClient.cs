using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Modpacks;

public sealed class CurseForgeApiClient
{
    private const string BaseUrl = "https://api.curseforge.com/v1";
    private readonly HttpClient httpClient;
    private readonly ILogger logger;

    public CurseForgeApiClient(
        HttpClient? httpClient = null,
        ILogger<CurseForgeApiClient>? logger = null)
    {
        this.httpClient = httpClient ?? new HttpClient();
        this.logger = logger ?? NullLogger<CurseForgeApiClient>.Instance;
    }

    internal async Task<CurseForgeFileDownload> GetFileDownloadAsync(
        long projectId,
        long fileId,
        string apiKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/mods/{projectId}/files/{fileId}");
        request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new ModpackImportException(
                ModpackImportFailureReason.CurseForgeFileUnavailable,
                $"CurseForge file {projectId}/{fileId} was not found.");
        }

        response.EnsureSuccessStatusCode();
        var fileResponse = await response.Content.ReadFromJsonAsync<CurseForgeFileResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var file = fileResponse?.Data;
        if (file is null || string.IsNullOrWhiteSpace(file.FileName))
        {
            throw new ModpackImportException(
                ModpackImportFailureReason.InvalidManifest,
                $"CurseForge file metadata is missing for {projectId}/{fileId}.");
        }

        var downloadUrl = file.DownloadUrl;
        if (string.IsNullOrWhiteSpace(downloadUrl))
            downloadUrl = await GetDownloadUrlAsync(projectId, fileId, apiKey, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            throw new ModpackImportException(
                ModpackImportFailureReason.CurseForgeFileUnavailable,
                $"CurseForge file {projectId}/{fileId} did not expose a download URL.");
        }

        logger.LogDebug(
            "Resolved CurseForge file download. ProjectId={ProjectId} FileId={FileId} FileName={FileName}",
            projectId,
            fileId,
            file.FileName);

        return new CurseForgeFileDownload(file.FileName, downloadUrl);
    }

    private async Task<string?> GetDownloadUrlAsync(
        long projectId,
        long fileId,
        string apiKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{BaseUrl}/mods/{projectId}/files/{fileId}/download-url");
        request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new ModpackImportException(
                ModpackImportFailureReason.CurseForgeFileUnavailable,
                $"CurseForge download URL for file {projectId}/{fileId} was not found.");
        }

        response.EnsureSuccessStatusCode();
        var downloadUrlResponse = await response.Content.ReadFromJsonAsync<CurseForgeDownloadUrlResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return downloadUrlResponse?.Data;
    }

    internal sealed record CurseForgeFileDownload(string FileName, string DownloadUrl);

    private sealed class CurseForgeFileResponse
    {
        [JsonPropertyName("data")]
        public CurseForgeFile? Data { get; init; }
    }

    private sealed class CurseForgeFile
    {
        [JsonPropertyName("fileName")]
        public string FileName { get; init; } = string.Empty;

        [JsonPropertyName("downloadUrl")]
        public string? DownloadUrl { get; init; }
    }

    private sealed class CurseForgeDownloadUrlResponse
    {
        [JsonPropertyName("data")]
        public string? Data { get; init; }
    }
}
