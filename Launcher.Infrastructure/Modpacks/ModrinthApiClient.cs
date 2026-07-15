/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Launcher.Application.Services;
using Launcher.Infrastructure.Minecraft;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Modpacks;

public sealed class ModrinthApiClient
{
    private const string BaseUrl = "https://api.modrinth.com/v2";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient httpClient;
    private readonly IImportConcurrencyLimiter limiter;
    private readonly ILogger logger;

    public ModrinthApiClient(
        HttpClient? httpClient = null,
        IImportConcurrencyLimiter? limiter = null,
        ILogger<ModrinthApiClient>? logger = null)
    {
        this.httpClient = httpClient ?? MinecraftHttpClientFactory.CreateTransportClient();
        this.limiter = limiter ?? ImportConcurrencyLimiter.Shared;
        this.logger = logger ?? NullLogger<ModrinthApiClient>.Instance;
    }

    internal async Task<IReadOnlyDictionary<string, ModrinthVersionFileMatch>> GetVersionFileMatchesAsync(
        IReadOnlyList<string> sha1Hashes,
        CancellationToken cancellationToken)
    {
        var hashes = sha1Hashes
            .Where(hash => !string.IsNullOrWhiteSpace(hash))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (hashes.Length == 0)
            return new Dictionary<string, ModrinthVersionFileMatch>(StringComparer.OrdinalIgnoreCase);

        await using var lease = await limiter.AcquireMetadataSlotAsync(cancellationToken).ConfigureAwait(false);
        using var response = await httpClient.PostAsJsonAsync(
                $"{BaseUrl}/version_files",
                new ModrinthVersionFilesRequest(hashes, "sha1"),
                JsonOptions,
                cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var versions = await response.Content.ReadFromJsonAsync<Dictionary<string, ModrinthVersionMatch>>(
                JsonOptions,
                cancellationToken)
            .ConfigureAwait(false)
            ?? new Dictionary<string, ModrinthVersionMatch>(StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, ModrinthVersionFileMatch>(StringComparer.OrdinalIgnoreCase);
        foreach (var (hash, version) in versions)
        {
            var file = version.Files.FirstOrDefault(file =>
                    string.Equals(file.Hashes.Sha1, hash, StringComparison.OrdinalIgnoreCase))
                ?? version.Files.FirstOrDefault(file => file.IsPrimary)
                ?? version.Files.FirstOrDefault();
            if (file is null
                || string.IsNullOrWhiteSpace(file.Url)
                || string.IsNullOrWhiteSpace(file.Hashes.Sha1)
                || file.Size <= 0)
            {
                continue;
            }

            result[hash] = new ModrinthVersionFileMatch(
                file.Url,
                file.Hashes.Sha1,
                file.Hashes.Sha512,
                file.Size);
        }

        logger.LogInformation(
            "Modrinth version file lookup completed. RequestedCount={RequestedCount} MatchedCount={MatchedCount}",
            hashes.Length,
            result.Count);
        return result;
    }

    internal sealed record ModrinthVersionFileMatch(
        string Url,
        string Sha1,
        string? Sha512,
        long Size);

    private sealed record ModrinthVersionFilesRequest(
        [property: JsonPropertyName("hashes")] IReadOnlyList<string> Hashes,
        [property: JsonPropertyName("algorithm")] string Algorithm);

    private sealed class ModrinthVersionMatch
    {
        [JsonPropertyName("files")]
        public List<ModrinthVersionFile> Files { get; init; } = [];
    }

    private sealed class ModrinthVersionFile
    {
        [JsonPropertyName("hashes")]
        public ModrinthFileHashes Hashes { get; init; } = new();

        [JsonPropertyName("url")]
        public string Url { get; init; } = string.Empty;

        [JsonPropertyName("primary")]
        public bool IsPrimary { get; init; }

        [JsonPropertyName("size")]
        public long Size { get; init; }
    }

    private sealed class ModrinthFileHashes
    {
        [JsonPropertyName("sha1")]
        public string Sha1 { get; init; } = string.Empty;

        [JsonPropertyName("sha512")]
        public string? Sha512 { get; init; }
    }
}
