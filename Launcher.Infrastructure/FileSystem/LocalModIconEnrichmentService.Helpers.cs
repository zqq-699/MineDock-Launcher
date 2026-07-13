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
private void CacheFileAlias(RemoteIconCacheIndex index, ModIconLookupCandidate lookup)
    {
        if (index.Aliases.TryGetValue(lookup.Sha1Alias, out var entryKey))
            index.FileAliases[lookup.FileAlias] = entryKey;
    }

    private static string? TryCreateFileAlias(string path)
    {
        try
        {
            var fileInfo = new FileInfo(path);
            if (!fileInfo.Exists)
                return null;

            return string.Create(
                CultureInfo.InvariantCulture,
                $"file:{Path.GetFullPath(fileInfo.FullName)}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}");
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            return null;
        }
    }

    private string GetIconCachePath(string entryKey)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entryKey))).ToLowerInvariant();
        return Path.Combine(cacheDirectory, $"{hash}.png");
    }

    private static void DeleteFileIfExists(string path)
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

    private static long ComputeCurseForgeMurmurHash2(ReadOnlySpan<byte> data)
    {
        const uint seed = 1;
        const uint m = 0x5bd1e995;
        const int r = 24;

        var length = data.Length;
        var hash = seed ^ (uint)length;
        var current = data;
        while (current.Length >= 4)
        {
            var value = BinaryPrimitives.ReadUInt32LittleEndian(current);
            value *= m;
            value ^= value >> r;
            value *= m;

            hash *= m;
            hash ^= value;
            current = current[4..];
        }

        switch (current.Length)
        {
            case 3:
                hash ^= (uint)current[2] << 16;
                goto case 2;
            case 2:
                hash ^= (uint)current[1] << 8;
                goto case 1;
            case 1:
                hash ^= current[0];
                hash *= m;
                break;
        }

        hash ^= hash >> 13;
        hash *= m;
        hash ^= hash >> 15;
        return hash;
    }
}
