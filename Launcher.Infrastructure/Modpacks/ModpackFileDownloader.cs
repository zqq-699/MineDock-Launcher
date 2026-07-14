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

using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;

namespace Launcher.Infrastructure.Modpacks;

internal sealed class ModpackFileDownloader
{
    private static readonly HashSet<string> CurseForgeDownloadHosts =
    [
        "api.curseforge.com",
        "edge.forgecdn.net",
        "mediafilez.forgecdn.net"
    ];

    private readonly HttpClient httpClient;
    private readonly IDownloadSpeedLimitState? downloadSpeedLimitState;
    private readonly IImportConcurrencyLimiter limiter;

    public ModpackFileDownloader(
        HttpClient httpClient,
        IDownloadSpeedLimitState? downloadSpeedLimitState,
        IImportConcurrencyLimiter limiter)
    {
        this.httpClient = httpClient;
        this.downloadSpeedLimitState = downloadSpeedLimitState;
        this.limiter = limiter;
    }

    public async Task DownloadToTemporaryFileAsync(
        string sourceUrl,
        string tempFilePath,
        string? curseForgeApiKey,
        DownloadSourcePreference downloadSourcePreference,
        string? expectedSha1,
        int downloadSpeedLimitMbPerSecond,
        CancellationToken cancellationToken)
    {
        // Public provider URLs use the common controller. Third-party candidates
        // are intentionally not mirror-rewritten by MinecraftDownloadSourceResolver.
        if (string.IsNullOrWhiteSpace(curseForgeApiKey)
            || !Uri.TryCreate(sourceUrl, UriKind.Absolute, out var candidateUri)
            || !CurseForgeDownloadHosts.Contains(candidateUri.Host))
        {
            var unifiedBandwidthLimiter = DownloadBandwidthLimiter.Create(downloadSpeedLimitMbPerSecond, downloadSpeedLimitState);
            var executor = new MinecraftDownloadRequestExecutor(
                httpClient,
                bandwidthLimiter: unifiedBandwidthLimiter,
                limiter: limiter,
                category: DownloadConcurrencyCategory.Modpack);
            try
            {
                await executor.DownloadFileAsync(
                    sourceUrl,
                    downloadSourcePreference,
                    categoryHint: "ThirdParty",
                    tempFilePath,
                    expectedSha1,
                    expectedSize: null,
                    reportDownloadedBytes: null,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (MinecraftDownloadRequestExecutor.DownloadSourceRequestException exception)
                when (exception.InnerException is DownloadHashMismatchException)
            {
                throw new ModpackImportException(ModpackImportFailureReason.HashMismatch, "Downloaded modpack file did not match its SHA-1.");
            }
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, sourceUrl);
        if (!string.IsNullOrWhiteSpace(curseForgeApiKey)
            && Uri.TryCreate(sourceUrl, UriKind.Absolute, out var sourceUri)
            && CurseForgeDownloadHosts.Contains(sourceUri.Host))
        {
            request.Headers.TryAddWithoutValidation("x-api-key", curseForgeApiKey);
        }

        await using var lease = await limiter.AcquireModpackDownloadSlotAsync(cancellationToken).ConfigureAwait(false);
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var bandwidthLimiter = DownloadBandwidthLimiter.Create(downloadSpeedLimitMbPerSecond, downloadSpeedLimitState);
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var destination = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await CopyWithThrottleAsync(source, destination, bandwidthLimiter, cancellationToken).ConfigureAwait(false);
    }

    public async Task VerifyHashAsync(
        string filePath,
        string expectedHash,
        HashAlgorithmName algorithmName,
        CancellationToken cancellationToken)
    {
        await using var lease = await limiter.AcquireHashSlotAsync(cancellationToken).ConfigureAwait(false);
        await using var stream = File.OpenRead(filePath);
        var actualHashBytes = algorithmName.Name switch
        {
            "SHA1" => await SHA1.HashDataAsync(stream, cancellationToken).ConfigureAwait(false),
            "SHA512" => await SHA512.HashDataAsync(stream, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unsupported hash algorithm: {algorithmName.Name}")
        };

        var actualHash = Convert.ToHexString(actualHashBytes).ToLowerInvariant();
        if (!string.Equals(actualHash, expectedHash.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ModpackImportException(
                ModpackImportFailureReason.HashMismatch,
                $"Hash mismatch for {filePath}.");
        }
    }

    public static void DeleteFileIfExists(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static async Task CopyWithThrottleAsync(
        Stream source,
        Stream destination,
        DownloadBandwidthLimiter? bandwidthLimiter,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
                break;

            if (bandwidthLimiter is not null)
                await bandwidthLimiter.ThrottleAsync(read, cancellationToken).ConfigureAwait(false);

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }
}
