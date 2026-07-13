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
using System.Net.Http;
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
/// <summary>
    /// 每个服务生命周期只清理一次：先删除过期项，超限时再按最近最少使用淘汰到目标大小。
    /// </summary>
    private async Task CleanupCacheOnceAsync(CancellationToken cancellationToken)
    {
        if (cleanupCompleted)
            return;

        await cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (cleanupCompleted)
                return;

            Directory.CreateDirectory(cacheDirectory);
            var index = await cacheIndexStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;
            var removed = 0;

            foreach (var pair in index.Entries.ToArray())
            {
                var path = Path.Combine(cacheDirectory, pair.Value.FileName);
                if (!File.Exists(path) || now - pair.Value.LastUsedAt > UnusedExpiration)
                {
                    DeleteFileIfExists(path);
                    index.Entries.Remove(pair.Key);
                    removed++;
                }
                else
                {
                    pair.Value.SizeBytes = new FileInfo(path).Length;
                }
            }

            var totalBytes = index.Entries.Values.Sum(entry => entry.SizeBytes);
            if (totalBytes > MaxCacheBytes)
            {
                foreach (var pair in index.Entries.OrderBy(pair => pair.Value.LastUsedAt).ToArray())
                {
                    if (totalBytes <= TargetCacheBytes)
                        break;

                    var path = Path.Combine(cacheDirectory, pair.Value.FileName);
                    DeleteFileIfExists(path);
                    totalBytes -= pair.Value.SizeBytes;
                    index.Entries.Remove(pair.Key);
                    removed++;
                }
            }

            foreach (var alias in index.Aliases
                         .Where(pair => !index.Entries.ContainsKey(pair.Value))
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                index.Aliases.Remove(alias);
            }

            foreach (var alias in index.FileAliases
                         .Where(pair => !index.Entries.ContainsKey(pair.Value))
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                index.FileAliases.Remove(alias);
            }

            await cacheIndexStore.SaveAsync(index, cancellationToken).ConfigureAwait(false);
            cleanupCompleted = true;
            logger.LogInformation(
                "Remote local mod icon cache cleanup completed. RemovedCount={RemovedCount} TotalBytes={TotalBytes}",
                removed,
                index.Entries.Values.Sum(entry => entry.SizeBytes));
        }
        finally
        {
            cacheLock.Release();
        }
    }

    /// <summary>
    /// 单次流式读取 Mod 文件，同时计算 SHA-1 和 CurseForge 去空白 MurmurHash2 指纹。
    /// </summary>
    private async Task<ModIconLookupCandidate?> CreateLookupCandidateAsync(
        LocalMod mod,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                mod.FullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 81920,
                useAsync: true);
            using var sha1 = SHA1.Create();
            using var fingerprintBytes = new MemoryStream();
            var buffer = new byte[81920];
            while (true)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    break;

                sha1.TransformBlock(buffer, 0, read, null, 0);
                for (var i = 0; i < read; i++)
                {
                    var value = buffer[i];
                    if (value is not (0x09 or 0x0a or 0x0d or 0x20))
                        fingerprintBytes.WriteByte(value);
                }
            }

            sha1.TransformFinalBlock([], 0, 0);
            var sha1Text = Convert.ToHexString(sha1.Hash!).ToLowerInvariant();
            var fileAlias = TryCreateFileAlias(mod.FullPath);
            if (fileAlias is null)
                return null;

            return new ModIconLookupCandidate(
                mod.FullPath,
                sha1Text,
                fileAlias,
                ComputeCurseForgeMurmurHash2(fingerprintBytes.GetBuffer().AsSpan(0, (int)fingerprintBytes.Length)));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or CryptographicException)
        {
            logger.LogWarning(
                exception,
                "Failed to hash local mod for remote icon lookup. FileName={FileName}",
                mod.FileName);
            return null;
        }
    }
}
