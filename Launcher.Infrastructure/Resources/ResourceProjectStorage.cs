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
using Microsoft.Extensions.Logging;

namespace Launcher.Infrastructure.Resources;

internal sealed class ResourceProjectStorage
{
    private readonly HttpClient httpClient;
    private readonly ILocalSaveService localSaveService;
    private readonly ILogger logger;

    public ResourceProjectStorage(
        HttpClient httpClient,
        ILocalSaveService localSaveService,
        ILogger logger)
    {
        this.httpClient = httpClient;
        this.localSaveService = localSaveService;
        this.logger = logger;
    }

    public async Task<string> InstallAsync(
        ResourceProjectVersion version,
        GameInstance instance,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(instance.InstanceDirectory))
            throw new InvalidOperationException("The target instance directory is empty.");
        if (version.Kind is ResourceProjectKind.World)
            return await InstallWorldAsync(version, instance, cancellationToken).ConfigureAwait(false);

        var installDirectory = ResolveInstallDirectory(instance, version.Kind);
        Directory.CreateDirectory(installDirectory);
        return await DownloadCoreAsync(version, installDirectory, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> DownloadAsync(
        ResourceProjectVersion version,
        string targetDirectory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(targetDirectory))
            throw new InvalidOperationException("The target download directory is empty.");
        Directory.CreateDirectory(targetDirectory);
        return await DownloadCoreAsync(version, targetDirectory, cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> DownloadExistsAsync(
        ResourceProjectVersion version,
        string targetDirectory,
        CancellationToken cancellationToken)
    {
        return string.IsNullOrWhiteSpace(targetDirectory)
            ? Task.FromResult(false)
            : ExistingFileMatchesAsync(
                version,
                Path.Combine(targetDirectory, ResolveFileName(version)),
                cancellationToken);
    }

    public Task<bool> InstallExistsAsync(
        ResourceProjectVersion version,
        GameInstance instance,
        CancellationToken cancellationToken)
    {
        return version.Kind is ResourceProjectKind.World
            || string.IsNullOrWhiteSpace(instance.InstanceDirectory)
            ? Task.FromResult(false)
            : ExistingFileMatchesAsync(
                version,
                Path.Combine(
                    ResolveInstallDirectory(instance, version.Kind),
                    ResolveFileName(version)),
                cancellationToken);
    }

    private async Task<string> DownloadCoreAsync(
        ResourceProjectVersion version,
        string targetDirectory,
        CancellationToken cancellationToken)
    {
        var target = Path.Combine(targetDirectory, ResolveFileName(version));
        var expectation = ResolveIntegrityExpectation(version);
        var urls = new[] { version.PrimaryDownloadUrl }
            .Concat(version.FallbackDownloadUrls)
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (urls.Length == 0)
            throw new InvalidOperationException($"Resource project version has no download URL: {version.VersionId}");

        Exception? lastException = null;
        for (var candidateIndex = 0; candidateIndex < urls.Length; candidateIndex++)
        {
            var tempPath = Path.Combine(targetDirectory, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.download");
            try
            {
                await DownloadAndVerifyAsync(
                    version,
                    urls[candidateIndex],
                    tempPath,
                    expectation,
                    cancellationToken).ConfigureAwait(false);
                File.Move(tempPath, target, overwrite: true);
                return target;
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                lastException = exception;
                logger.LogWarning(
                    exception,
                    "Resource project download candidate timed out. VersionId={VersionId} Candidate={Candidate} CandidateCount={CandidateCount}",
                    version.VersionId,
                    candidateIndex + 1,
                    urls.Length);
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or ResourceProjectIntegrityException)
            {
                lastException = exception;
                logger.LogWarning(
                    exception,
                    "Failed to verify resource project download candidate. VersionId={VersionId} Candidate={Candidate} CandidateCount={CandidateCount}",
                    version.VersionId,
                    candidateIndex + 1,
                    urls.Length);
            }
            finally
            {
                TryDeleteTemporaryFile(tempPath, version.VersionId);
            }
        }

        if (lastException is ResourceProjectIntegrityException integrityException)
            throw integrityException;
        throw new InvalidOperationException($"Failed to download resource project version: {version.VersionId}", lastException);
    }

    private static async Task<bool> ExistingFileMatchesAsync(
        ResourceProjectVersion version,
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(path))
            return false;

        var expectation = ResolveIntegrityExpectation(version);
        if (expectation.FileSize is null && expectation.Hash is null)
            return true;

        try
        {
            var fileInfo = new FileInfo(path);
            if (expectation.FileSize.HasValue && fileInfo.Length != expectation.FileSize.Value)
                return false;
            if (expectation.Hash is null)
                return true;

            await using var source = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hasher = IncrementalHash.CreateHash(ToHashAlgorithmName(expectation.Hash.Algorithm));
            var buffer = new byte[81920];
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    break;
                hasher.AppendData(buffer.AsSpan(0, read));
            }
            return CryptographicOperations.FixedTimeEquals(hasher.GetHashAndReset(), expectation.Hash.Value);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task DownloadAndVerifyAsync(
        ResourceProjectVersion version,
        string url,
        string tempPath,
        IntegrityExpectation expectation,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var destination = new FileStream(
            tempPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hasher = expectation.Hash is null
            ? null
            : IncrementalHash.CreateHash(ToHashAlgorithmName(expectation.Hash.Algorithm));
        var buffer = new byte[81920];
        long totalBytes = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            totalBytes += read;
            if (expectation.FileSize.HasValue && totalBytes > expectation.FileSize.Value)
                throw CreateIntegrityException(version, ResourceProjectIntegrityFailureReason.LengthMismatch, expectation.Hash?.Algorithm);
            hasher?.AppendData(buffer.AsSpan(0, read));
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);

        if (expectation.FileSize.HasValue && totalBytes != expectation.FileSize.Value)
            throw CreateIntegrityException(version, ResourceProjectIntegrityFailureReason.LengthMismatch, expectation.Hash?.Algorithm);
        if (expectation.Hash is not null
            && !CryptographicOperations.FixedTimeEquals(hasher!.GetHashAndReset(), expectation.Hash.Value))
        {
            throw CreateIntegrityException(version, ResourceProjectIntegrityFailureReason.HashMismatch, expectation.Hash.Algorithm);
        }
    }

    private static IntegrityExpectation ResolveIntegrityExpectation(ResourceProjectVersion version)
    {
        if (version.ExpectedFileSize < 0)
            throw CreateIntegrityException(version, ResourceProjectIntegrityFailureReason.InvalidMetadata);

        var hashes = new Dictionary<ResourceFileHashAlgorithm, byte[]>();
        foreach (var group in version.FileHashes.GroupBy(hash => hash.Algorithm))
        {
            var values = group
                .Select(hash => hash.Value?.Trim() ?? string.Empty)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (values.Length != 1 || !TryParseHash(group.Key, values[0], out var value))
                throw CreateIntegrityException(version, ResourceProjectIntegrityFailureReason.InvalidMetadata, group.Key);
            hashes[group.Key] = value;
        }

        ExpectedHash? expectedHash = null;
        foreach (var algorithm in new[]
                 {
                     ResourceFileHashAlgorithm.Sha512,
                     ResourceFileHashAlgorithm.Sha1,
                     ResourceFileHashAlgorithm.Md5
                 })
        {
            if (hashes.TryGetValue(algorithm, out var value))
            {
                expectedHash = new ExpectedHash(algorithm, value);
                break;
            }
        }

        var requiresTrustedHash = version.Kind is ResourceProjectKind.Mod
            || string.Equals(Path.GetExtension(ResolveFileName(version)), ".jar", StringComparison.OrdinalIgnoreCase);
        if (requiresTrustedHash
            && expectedHash?.Algorithm is not ResourceFileHashAlgorithm.Sha512 and not ResourceFileHashAlgorithm.Sha1)
        {
            throw CreateIntegrityException(version, ResourceProjectIntegrityFailureReason.MissingTrustedHash, expectedHash?.Algorithm);
        }
        return new IntegrityExpectation(version.ExpectedFileSize, expectedHash);
    }

    private static bool TryParseHash(ResourceFileHashAlgorithm algorithm, string value, out byte[] result)
    {
        var expectedLength = algorithm switch
        {
            ResourceFileHashAlgorithm.Sha512 => 128,
            ResourceFileHashAlgorithm.Sha1 => 40,
            ResourceFileHashAlgorithm.Md5 => 32,
            _ => 0
        };
        if (value.Length != expectedLength)
        {
            result = [];
            return false;
        }
        try
        {
            result = Convert.FromHexString(value);
            return true;
        }
        catch (FormatException)
        {
            result = [];
            return false;
        }
    }

    private static HashAlgorithmName ToHashAlgorithmName(ResourceFileHashAlgorithm algorithm)
    {
        return algorithm switch
        {
            ResourceFileHashAlgorithm.Sha512 => HashAlgorithmName.SHA512,
            ResourceFileHashAlgorithm.Sha1 => HashAlgorithmName.SHA1,
            ResourceFileHashAlgorithm.Md5 => HashAlgorithmName.MD5,
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm))
        };
    }

    private static ResourceProjectIntegrityException CreateIntegrityException(
        ResourceProjectVersion version,
        ResourceProjectIntegrityFailureReason reason,
        ResourceFileHashAlgorithm? algorithm = null)
    {
        return new ResourceProjectIntegrityException(version.VersionId, reason, algorithm);
    }

    private void TryDeleteTemporaryFile(string path, string versionId)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                exception,
                "Failed to delete temporary resource project download. VersionId={VersionId} FileName={FileName}",
                versionId,
                Path.GetFileName(path));
        }
    }

    private async Task<string> InstallWorldAsync(
        ResourceProjectVersion version,
        GameInstance instance,
        CancellationToken cancellationToken)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"launcher-world-install-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var archivePath = await DownloadCoreAsync(version, tempDirectory, cancellationToken).ConfigureAwait(false);
            var result = await localSaveService.ImportFromArchiveAsync(instance, archivePath, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess || result.ImportedSave is null)
                throw new InvalidOperationException($"Failed to import world archive. FailureReason={result.FailureReason}");
            return result.ImportedSave.FullPath;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDirectory))
                    Directory.Delete(tempDirectory, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(
                    exception,
                    "Failed to delete temporary resource world directory. Directory={Directory}",
                    tempDirectory);
            }
        }
    }

    private static string ResolveFileName(ResourceProjectVersion version)
    {
        var fileName = Path.GetFileName(version.FileName);
        return string.IsNullOrWhiteSpace(fileName)
            ? $"{version.VersionId}{ResolveDefaultExtension(version.Kind)}"
            : fileName;
    }

    private static string ResolveDefaultExtension(ResourceProjectKind kind)
    {
        return kind switch
        {
            ResourceProjectKind.Modpack => ".mrpack",
            ResourceProjectKind.ResourcePack or ResourceProjectKind.ShaderPack or ResourceProjectKind.World => ".zip",
            _ => ".jar"
        };
    }

    private static string ResolveInstallDirectory(GameInstance instance, ResourceProjectKind kind)
    {
        var directoryName = kind switch
        {
            ResourceProjectKind.ResourcePack => "resourcepacks",
            ResourceProjectKind.ShaderPack => "shaderpacks",
            ResourceProjectKind.World => "saves",
            _ => "mods"
        };
        return Path.Combine(instance.InstanceDirectory, directoryName);
    }

    private sealed record IntegrityExpectation(long? FileSize, ExpectedHash? Hash);

    private sealed record ExpectedHash(ResourceFileHashAlgorithm Algorithm, byte[] Value);
}
