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

using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.CurseForge;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.FileSystem;

public sealed partial class LocalModIconEnrichmentService
{
    private async Task<string?> TryCacheRemoteIconAsync(
        ModIconLookupCandidate lookup,
        RemoteIconCandidate icon,
        CancellationToken cancellationToken)
    {
        try
        {
            return await CacheRemoteIconAsync(lookup, icon, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to cache remote local mod icon. Source={Source} ProjectId={ProjectId}",
                icon.Source,
                icon.ProjectId);
            return null;
        }
    }

    /// <summary>
    /// 限制下载大小、验证并规范化图像，然后在缓存锁内提交文件和索引别名。
    /// </summary>
    private async Task<string?> CacheRemoteIconAsync(
        ModIconLookupCandidate lookup,
        RemoteIconCandidate icon,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(icon.IconUrl, UriKind.Absolute, out _))
            return null;

        byte[] imageBytes;
        try
        {
            imageBytes = await thumbnailDownloader
                .DownloadAsync(icon.IconUrl, MaxIconBytes, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to download remote local mod icon. Source={Source} ProjectId={ProjectId}",
                icon.Source,
                icon.ProjectId);
            return null;
        }

        await cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(cacheDirectory);
            var index = await cacheIndexStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;
            var entryKey = $"{icon.Source}:{icon.ProjectId}";
            var cachePath = GetIconCachePath(entryKey);

            try
            {
                RemoteIconImageEncoder.SaveAsPng(imageBytes, cachePath);
            }
            catch (Exception exception) when (
                exception is NotSupportedException
                or InvalidDataException
                or ArgumentException
                or IOException)
            {
                logger.LogWarning(
                    exception,
                    "Remote local mod icon was invalid. Source={Source} ProjectId={ProjectId}",
                    icon.Source,
                    icon.ProjectId);
                return null;
            }

            var sizeBytes = new FileInfo(cachePath).Length;
            index.Entries[entryKey] = new RemoteIconCacheEntry
            {
                Source = icon.Source,
                ProjectId = icon.ProjectId,
                IconUrl = icon.IconUrl,
                FileName = Path.GetFileName(cachePath),
                CachedAt = now,
                LastUsedAt = now,
                SizeBytes = sizeBytes
            };
            index.Aliases[lookup.Sha1Alias] = entryKey;
            index.FileAliases[lookup.FileAlias] = entryKey;
            await cacheIndexStore.SaveAsync(index, cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "Remote local mod icon cached. Source={Source} ProjectId={ProjectId} SizeBytes={SizeBytes}",
                icon.Source,
                icon.ProjectId,
                sizeBytes);
            return new Uri(cachePath).AbsoluteUri;
        }
        finally
        {
            cacheLock.Release();
        }
    }
}
