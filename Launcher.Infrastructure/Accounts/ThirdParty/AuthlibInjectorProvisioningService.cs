/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Launcher.Application.Accounts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Accounts.ThirdParty;

internal sealed class AuthlibInjectorProvisioningService : IAuthlibInjectorProvisioningService
{
    private static readonly Uri LatestArtifactUri = new("https://authlib-injector.yushi.moe/artifact/latest.json");
    private const long MaximumArtifactBytes = 16 * 1024 * 1024;
    private readonly HttpClient httpClient;
    private readonly string cacheDirectory;
    private readonly ILogger<AuthlibInjectorProvisioningService> logger;
    private readonly SemaphoreSlim provisioningLock = new(1, 1);

    public AuthlibInjectorProvisioningService(
        LauncherPathProvider pathProvider,
        ILogger<AuthlibInjectorProvisioningService>? logger = null)
        : this(
            new HttpClient { Timeout = TimeSpan.FromSeconds(20) },
            Path.Combine(pathProvider.DefaultDataDirectory, "cache", "authlib-injector"),
            logger)
    {
    }

    internal AuthlibInjectorProvisioningService(
        HttpClient httpClient,
        string cacheDirectory,
        ILogger<AuthlibInjectorProvisioningService>? logger = null)
    {
        this.httpClient = httpClient;
        this.cacheDirectory = Path.GetFullPath(cacheDirectory);
        this.logger = logger ?? NullLogger<AuthlibInjectorProvisioningService>.Instance;
    }

    public async Task<AuthlibInjectorArtifact> EnsureAvailableAsync(CancellationToken cancellationToken = default)
    {
        await provisioningLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(cacheDirectory);
            try
            {
                var latest = await GetLatestArtifactAsync(cancellationToken).ConfigureAwait(false);
                var cached = await TryUseArtifactAsync(latest, cancellationToken).ConfigureAwait(false);
                if (cached is not null)
                    return cached;
                return await DownloadArtifactAsync(latest, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Unable to prepare the latest authlib-injector build; checking verified cache.");
                var fallback = await FindVerifiedFallbackAsync(cancellationToken).ConfigureAwait(false);
                if (fallback is not null)
                    return fallback;
                throw new InvalidOperationException("No verified authlib-injector artifact is available.", exception);
            }
        }
        finally
        {
            provisioningLock.Release();
        }
    }

    private async Task<ArtifactMetadata> GetLatestArtifactAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(LatestArtifactUri, cancellationToken).ConfigureAwait(false);
        EnsureHttpsResponse(response);
        response.EnsureSuccessStatusCode();
        var metadata = await response.Content.ReadFromJsonAsync<ArtifactMetadata>(cancellationToken: cancellationToken)
            .ConfigureAwait(false)
            ?? throw new JsonException("The authlib-injector metadata response is empty.");
        ValidateMetadata(metadata);
        return metadata;
    }

    private async Task<AuthlibInjectorArtifact?> TryUseArtifactAsync(
        ArtifactMetadata metadata,
        CancellationToken cancellationToken)
    {
        var path = GetArtifactPath(metadata);
        if (!File.Exists(path))
            return null;
        if (!await HashMatchesAsync(path, metadata.Checksums.Sha256, cancellationToken).ConfigureAwait(false))
        {
            TryDelete(path);
            TryDelete(GetManifestPath(path));
            return null;
        }
        await SaveManifestAsync(path, metadata, cancellationToken).ConfigureAwait(false);
        return ToArtifact(path, metadata);
    }

    private async Task<AuthlibInjectorArtifact> DownloadArtifactAsync(
        ArtifactMetadata metadata,
        CancellationToken cancellationToken)
    {
        var destination = GetArtifactPath(metadata);
        var temporaryPath = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using var response = await httpClient.GetAsync(
                metadata.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            EnsureHttpsResponse(response);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaximumArtifactBytes)
                throw new InvalidDataException("The authlib-injector artifact is too large.");

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var target = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[81920];
                long total = 0;
                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                        break;
                    total += read;
                    if (total > MaximumArtifactBytes)
                        throw new InvalidDataException("The authlib-injector artifact is too large.");
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
                await target.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (!await HashMatchesAsync(temporaryPath, metadata.Checksums.Sha256, cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException("The authlib-injector SHA-256 checksum does not match.");
            File.Move(temporaryPath, destination, overwrite: true);
            await SaveManifestAsync(destination, metadata, cancellationToken).ConfigureAwait(false);
            logger.LogInformation(
                "Authlib-injector prepared. Version={Version} BuildNumber={BuildNumber}",
                metadata.Version,
                metadata.BuildNumber);
            return ToArtifact(destination, metadata);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private async Task<AuthlibInjectorArtifact?> FindVerifiedFallbackAsync(CancellationToken cancellationToken)
    {
        var candidates = new List<(ArtifactMetadata Metadata, string Path)>();
        foreach (var manifestPath in Directory.EnumerateFiles(cacheDirectory, "*.manifest.json"))
        {
            try
            {
                var metadata = JsonSerializer.Deserialize<ArtifactMetadata>(
                    await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false));
                if (metadata is null)
                    continue;
                ValidateMetadata(metadata);
                var path = manifestPath[..^".manifest.json".Length];
                if (File.Exists(path))
                    candidates.Add((metadata, path));
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException or InvalidDataException)
            {
                logger.LogDebug(exception, "Ignoring invalid authlib-injector cache manifest. ManifestPath={ManifestPath}", manifestPath);
            }
        }

        foreach (var candidate in candidates.OrderByDescending(item => item.Metadata.BuildNumber))
        {
            if (await HashMatchesAsync(candidate.Path, candidate.Metadata.Checksums.Sha256, cancellationToken).ConfigureAwait(false))
            {
                logger.LogInformation(
                    "Using cached authlib-injector. Version={Version} BuildNumber={BuildNumber}",
                    candidate.Metadata.Version,
                    candidate.Metadata.BuildNumber);
                return ToArtifact(candidate.Path, candidate.Metadata);
            }
        }
        return null;
    }

    private async Task SaveManifestAsync(
        string artifactPath,
        ArtifactMetadata metadata,
        CancellationToken cancellationToken)
    {
        var manifestPath = GetManifestPath(artifactPath);
        var temporaryPath = manifestPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                JsonSerializer.Serialize(metadata),
                cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, manifestPath, overwrite: true);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private string GetArtifactPath(ArtifactMetadata metadata)
    {
        var safeVersion = string.Concat(metadata.Version.Select(character =>
            char.IsLetterOrDigit(character) || character is '.' or '-' or '_' ? character : '_'));
        return Path.Combine(cacheDirectory, $"authlib-injector-{safeVersion}-{metadata.BuildNumber}.jar");
    }

    private static string GetManifestPath(string artifactPath) => artifactPath + ".manifest.json";

    private static void ValidateMetadata(ArtifactMetadata metadata)
    {
        if (metadata.BuildNumber <= 0
            || string.IsNullOrWhiteSpace(metadata.Version)
            || metadata.DownloadUrl is null
            || !metadata.DownloadUrl.IsAbsoluteUri
            || !string.Equals(metadata.DownloadUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || metadata.Checksums is null
            || metadata.Checksums.Sha256.Length != 64
            || !metadata.Checksums.Sha256.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("The authlib-injector metadata is invalid.");
        }
    }

    private static async Task<bool> HashMatchesAsync(
        string path,
        string expectedHash,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return string.Equals(Convert.ToHexString(hash), expectedHash, StringComparison.OrdinalIgnoreCase);
    }

    private static AuthlibInjectorArtifact ToArtifact(string path, ArtifactMetadata metadata) =>
        new(path, metadata.Version, metadata.BuildNumber);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void EnsureHttpsResponse(HttpResponseMessage response)
    {
        if (response.RequestMessage?.RequestUri is { } finalUri
            && !string.Equals(finalUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The authlib-injector download redirected to an insecure address.");
        }
    }

    internal sealed record ArtifactMetadata(
        [property: JsonPropertyName("build_number")] int BuildNumber,
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("download_url")] Uri DownloadUrl,
        [property: JsonPropertyName("checksums")] ArtifactChecksums Checksums);

    internal sealed record ArtifactChecksums(
        [property: JsonPropertyName("sha256")] string Sha256);
}
