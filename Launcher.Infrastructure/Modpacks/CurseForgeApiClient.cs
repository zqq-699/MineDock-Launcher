using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
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
    private readonly IImportConcurrencyLimiter limiter;
    private readonly ILogger logger;

    public CurseForgeApiClient(
        HttpClient? httpClient = null,
        IImportConcurrencyLimiter? limiter = null,
        ILogger<CurseForgeApiClient>? logger = null)
    {
        this.httpClient = httpClient ?? new HttpClient();
        this.limiter = limiter ?? ImportConcurrencyLimiter.Shared;
        this.logger = logger ?? NullLogger<CurseForgeApiClient>.Instance;
    }

    internal async Task<CurseForgeResolvedFileDownload> GetFileDownloadAsync(
        long projectId,
        long fileId,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var downloadUrlResult = await TryGetDownloadUrlAsync(projectId, fileId, apiKey, cancellationToken).ConfigureAwait(false);
        var file = await GetFileMetadataAsync(projectId, fileId, apiKey, cancellationToken).ConfigureAwait(false);

        var fallbackUrls = new List<string>();
        var primaryUrl = string.Empty;
        if (!string.IsNullOrWhiteSpace(downloadUrlResult.DownloadUrl))
        {
            primaryUrl = downloadUrlResult.DownloadUrl;
            AddDistinctUrl(fallbackUrls, file.DownloadUrl, primaryUrl);
        }
        else if (!string.IsNullOrWhiteSpace(file.DownloadUrl))
        {
            primaryUrl = file.DownloadUrl;
        }

        var edgeUrl = BuildCdnUrl("edge.forgecdn.net", fileId, file.FileName);
        var mediafilezUrl = BuildCdnUrl("mediafilez.forgecdn.net", fileId, file.FileName);

        if (string.IsNullOrWhiteSpace(primaryUrl))
            primaryUrl = edgeUrl;

        AddDistinctUrl(fallbackUrls, edgeUrl, primaryUrl);
        AddDistinctUrl(fallbackUrls, mediafilezUrl, primaryUrl);

        var hashes = ResolveHashes(file.Hashes);
        logger.LogInformation(
            "Resolved CurseForge file download candidates. ProjectId={ProjectId} FileId={FileId} FileName={FileName} DownloadUrlStatusCode={DownloadUrlStatusCode} HasDirectDownloadUrl={HasDirectDownloadUrl} FallbackUrlCount={FallbackUrlCount}",
            projectId,
            fileId,
            file.FileName,
            (int?)downloadUrlResult.StatusCode,
            !string.IsNullOrWhiteSpace(file.DownloadUrl),
            fallbackUrls.Count);

        return new CurseForgeResolvedFileDownload(
            projectId,
            fileId,
            file.FileName,
            string.IsNullOrWhiteSpace(file.DisplayName) ? file.FileName : file.DisplayName,
            primaryUrl,
            fallbackUrls,
            hashes.Sha1,
            hashes.Sha512);
    }

    internal async Task<IReadOnlyDictionary<long, CurseForgeFingerprintMatch>> GetFingerprintMatchesAsync(
        IReadOnlyList<long> fingerprints,
        string apiKey,
        CancellationToken cancellationToken)
    {
        if (fingerprints.Count == 0)
            return new Dictionary<long, CurseForgeFingerprintMatch>();

        using var request = CreateRequest(HttpMethod.Post, $"{BaseUrl}/fingerprints/432", apiKey);
        request.Content = JsonContent.Create(
            new CurseForgeFingerprintRequest(fingerprints.Distinct().ToArray()));

        await using var lease = await limiter.AcquireMetadataSlotAsync(cancellationToken).ConfigureAwait(false);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await ParseFingerprintMatchesAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CurseForgeFile> GetFileMetadataAsync(
        long projectId,
        long fileId,
        string apiKey,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest($"{BaseUrl}/mods/{projectId}/files/{fileId}", apiKey);
        await using var lease = await limiter.AcquireMetadataSlotAsync(cancellationToken).ConfigureAwait(false);
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

        return file;
    }

    private async Task<DownloadUrlResult> TryGetDownloadUrlAsync(
        long projectId,
        long fileId,
        string apiKey,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest($"{BaseUrl}/mods/{projectId}/files/{fileId}/download-url", apiKey);
        await using var lease = await limiter.AcquireMetadataSlotAsync(cancellationToken).ConfigureAwait(false);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            logger.LogInformation(
                "CurseForge download-url endpoint did not provide a direct URL. ProjectId={ProjectId} FileId={FileId} StatusCode={StatusCode}",
                projectId,
                fileId,
                (int)response.StatusCode);
            return new DownloadUrlResult(response.StatusCode, null);
        }

        response.EnsureSuccessStatusCode();
        var downloadUrlResponse = await response.Content.ReadFromJsonAsync<CurseForgeDownloadUrlResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return new DownloadUrlResult(response.StatusCode, downloadUrlResponse?.Data);
    }

    private static HttpRequestMessage CreateRequest(string requestUri, string apiKey)
    {
        return CreateRequest(HttpMethod.Get, requestUri, apiKey);
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string requestUri, string apiKey)
    {
        var request = new HttpRequestMessage(method, requestUri);
        request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        return request;
    }

    private static async Task<IReadOnlyDictionary<long, CurseForgeFingerprintMatch>> ParseFingerprintMatchesAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<long, CurseForgeFingerprintMatch>();
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("exactMatches", out var exactMatches)
            || exactMatches.ValueKind is not JsonValueKind.Array)
        {
            return result;
        }

        foreach (var match in exactMatches.EnumerateArray())
        {
            JsonElement? file = match.TryGetProperty("file", out var fileElement) ? fileElement : null;
            var projectId = file is null
                ? null
                : TryReadLong(file.Value, "modId") ?? TryReadLong(file.Value, "projectId");
            projectId ??= TryReadLong(match, "modId") ?? TryReadLong(match, "projectId") ?? TryReadLong(match, "id");

            var fileId = file is null
                ? null
                : TryReadLong(file.Value, "id") ?? TryReadLong(file.Value, "fileId");
            fileId ??= TryReadLong(match, "fileId") ?? TryReadLong(match, "fileID");

            var fingerprint = TryReadLong(match, "fileFingerprint") ?? TryReadLong(match, "fingerprint");
            if (fingerprint is null && file is not null)
            {
                fingerprint = TryReadLong(file.Value, "fileFingerprint")
                              ?? TryReadLong(file.Value, "fingerprint")
                              ?? TryReadFirstFingerprint(file.Value);
            }

            if (fingerprint is not null && projectId is not null && fileId is not null)
                result[fingerprint.Value] = new CurseForgeFingerprintMatch(projectId.Value, fileId.Value);
        }

        return result;
    }

    private static long? TryReadFirstFingerprint(JsonElement element)
    {
        if (!element.TryGetProperty("fingerprints", out var fingerprints)
            || fingerprints.ValueKind is not JsonValueKind.Array)
        {
            return null;
        }

        foreach (var fingerprint in fingerprints.EnumerateArray())
        {
            var value = fingerprint.ValueKind is JsonValueKind.Object
                ? TryReadLong(fingerprint, "value")
                : TryReadLong(fingerprint);
            if (value is not null)
                return value;
        }

        return null;
    }

    private static long? TryReadLong(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            ? TryReadLong(property)
            : null;
    }

    private static long? TryReadLong(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetInt64(out var value) => value,
            JsonValueKind.String when long.TryParse(element.GetString(), out var value) => value,
            _ => null
        };
    }

    private static (string? Sha1, string? Sha512) ResolveHashes(IReadOnlyList<CurseForgeFileHash>? hashes)
    {
        string? sha1 = null;
        string? sha512 = null;
        if (hashes is null)
            return (null, null);

        foreach (var hash in hashes)
        {
            if (string.IsNullOrWhiteSpace(hash.Value))
                continue;

            if (hash.Algo == 1 || hash.Value.Length == 40)
                sha1 ??= hash.Value;
            else if (hash.Value.Length == 128)
                sha512 ??= hash.Value;
        }

        return (sha1, sha512);
    }

    private static string BuildCdnUrl(string host, long fileId, string fileName)
    {
        var part1 = fileId / 1000;
        var part2 = fileId % 1000;
        var encodedFileName = Uri.EscapeDataString(fileName);
        return $"https://{host}/files/{part1}/{part2}/{encodedFileName}";
    }

    private static void AddDistinctUrl(ICollection<string> urls, string? candidateUrl, string primaryUrl)
    {
        if (string.IsNullOrWhiteSpace(candidateUrl)
            || string.Equals(candidateUrl, primaryUrl, StringComparison.OrdinalIgnoreCase)
            || urls.Contains(candidateUrl, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        urls.Add(candidateUrl);
    }

    internal sealed record CurseForgeResolvedFileDownload(
        long ProjectId,
        long FileId,
        string FileName,
        string DisplayName,
        string PrimaryUrl,
        IReadOnlyList<string> FallbackUrls,
        string? Sha1,
        string? Sha512);

    internal sealed record CurseForgeFingerprintMatch(long ProjectId, long FileId);

    private sealed record DownloadUrlResult(HttpStatusCode StatusCode, string? DownloadUrl);

    private sealed record CurseForgeFingerprintRequest(
        [property: JsonPropertyName("fingerprints")] IReadOnlyList<long> Fingerprints);

    private sealed class CurseForgeFileResponse
    {
        [JsonPropertyName("data")]
        public CurseForgeFile? Data { get; init; }
    }

    private sealed class CurseForgeFile
    {
        [JsonPropertyName("displayName")]
        public string DisplayName { get; init; } = string.Empty;

        [JsonPropertyName("fileName")]
        public string FileName { get; init; } = string.Empty;

        [JsonPropertyName("downloadUrl")]
        public string? DownloadUrl { get; init; }

        [JsonPropertyName("hashes")]
        public IReadOnlyList<CurseForgeFileHash>? Hashes { get; init; }
    }

    private sealed class CurseForgeFileHash
    {
        [JsonPropertyName("algo")]
        public int Algo { get; init; }

        [JsonPropertyName("value")]
        public string Value { get; init; } = string.Empty;
    }

    private sealed class CurseForgeDownloadUrlResponse
    {
        [JsonPropertyName("data")]
        public string? Data { get; init; }
    }
}
