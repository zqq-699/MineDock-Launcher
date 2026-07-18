/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.FileSystem;
using Microsoft.Extensions.Logging;

namespace Launcher.Infrastructure.Resources;

internal sealed class ResourceThumbnailCacheService
{
    private const long MaximumThumbnailBytes = 1024L * 1024L;
    private const long MaximumCacheBytes = 100L * 1024L * 1024L;
    private const long TargetCacheBytes = 80L * 1024L * 1024L;
    private static readonly TimeSpan RefreshAfter = TimeSpan.FromDays(30);

    private readonly ConcurrentDictionary<string, SemaphoreSlim> entryLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim commitLock = new(1, 1);
    private readonly string cacheDirectory;
    private readonly RemoteThumbnailDownloadClient downloader;
    private readonly ILogger logger;
    private long cacheSizeBytes = -1;

    public ResourceThumbnailCacheService(
        LauncherPathProvider pathProvider,
        HttpClient httpClient,
        IImportConcurrencyLimiter? limiter,
        IDownloadSpeedLimitState? downloadSpeedLimitState,
        ILogger logger)
    {
        cacheDirectory = Path.Combine(pathProvider.DefaultDataDirectory, "cache", "resources", "thumbnails");
        downloader = new RemoteThumbnailDownloadClient(httpClient, limiter, downloadSpeedLimitState, logger);
        this.logger = logger;
    }

    public string? TryGetCachedThumbnailSource(ResourceProject project)
    {
        try
        {
            var path = TryGetCachePath(project);
            return path is not null && File.Exists(path) ? ToFileSource(path) : null;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            logger.LogWarning(
                exception,
                "Failed to inspect resource project thumbnail cache. Kind={Kind} Source={Source} ProjectId={ProjectId}",
                project.Kind,
                project.Source,
                project.ProjectId);
            return null;
        }
    }

    public async Task<string?> GetOrCreateThumbnailSourceAsync(
        ResourceProject project,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        var cachePath = TryGetCachePath(project);
        if (cachePath is null)
            return null;

        var entryLock = entryLocks.GetOrAdd(cachePath, static _ => new SemaphoreSlim(1, 1));
        await entryLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsFresh(cachePath))
                return ToFileSource(cachePath);

            var bytes = await downloader.DownloadAsync(
                project.IconUrl!,
                MaximumThumbnailBytes,
                cancellationToken).ConfigureAwait(false);
            var temporaryPath = $"{cachePath}.{Guid.NewGuid():N}.tmp";
            try
            {
                RemoteIconImageEncoder.SaveAsPng(bytes, temporaryPath);
                await commitLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    Directory.CreateDirectory(cacheDirectory);
                    var previousLength = File.Exists(cachePath) ? new FileInfo(cachePath).Length : 0;
                    File.Move(temporaryPath, cachePath, overwrite: true);
                    TrackCacheWriteAndTrim(cachePath, previousLength);
                }
                finally
                {
                    commitLock.Release();
                }
            }
            finally
            {
                TryDelete(temporaryPath);
            }

            logger.LogDebug(
                "Resource project thumbnail cached. Kind={Kind} Source={Source} ProjectId={ProjectId}",
                project.Kind,
                project.Source,
                project.ProjectId);
            return ToFileSource(cachePath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to cache resource project thumbnail. Kind={Kind} Source={Source} ProjectId={ProjectId}",
                project.Kind,
                project.Source,
                project.ProjectId);
            return TryGetCachedThumbnailSource(project);
        }
        finally
        {
            entryLock.Release();
        }
    }

    private string? TryGetCachePath(ResourceProject project)
    {
        if (string.IsNullOrWhiteSpace(project.IconUrl)
            || !Uri.TryCreate(project.IconUrl, UriKind.Absolute, out var uri)
            || (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var key = string.Create(
            CultureInfo.InvariantCulture,
            $"{project.Source}:{project.ProjectId}|{uri.AbsoluteUri}");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
        return Path.Combine(cacheDirectory, $"{hash}.png");
    }

    private static bool IsFresh(string path)
    {
        if (!File.Exists(path))
            return false;
        return DateTime.UtcNow - File.GetLastWriteTimeUtc(path) <= RefreshAfter;
    }

    private static string ToFileSource(string path)
    {
        var version = File.GetLastWriteTimeUtc(path).Ticks;
        return $"{new Uri(path).AbsoluteUri}?v={version.ToString(CultureInfo.InvariantCulture)}";
    }

    private void TrackCacheWriteAndTrim(string cachePath, long previousLength)
    {
        var currentLength = new FileInfo(cachePath).Length;
        if (cacheSizeBytes < 0)
        {
            cacheSizeBytes = new DirectoryInfo(cacheDirectory)
                .EnumerateFiles("*.png", SearchOption.TopDirectoryOnly)
                .Sum(file => file.Length);
        }
        else
        {
            cacheSizeBytes += currentLength - previousLength;
        }

        if (cacheSizeBytes <= MaximumCacheBytes)
            return;

        var files = new DirectoryInfo(cacheDirectory)
            .EnumerateFiles("*.png", SearchOption.TopDirectoryOnly)
            .OrderBy(file => string.Equals(file.FullName, cachePath, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(file => file.LastWriteTimeUtc)
            .ToList();
        foreach (var file in files)
        {
            if (cacheSizeBytes <= TargetCacheBytes)
                break;
            var length = file.Length;
            TryDelete(file.FullName);
            if (!File.Exists(file.FullName))
                cacheSizeBytes -= length;
        }
    }

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
}
