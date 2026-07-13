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
private string? TryGetCachedIcon(
        RemoteIconCacheIndex index,
        string alias,
        DateTimeOffset now,
        bool allowStale,
        bool updateLastUsed,
        out bool isStale)
    {
        isStale = false;
        if (!index.Aliases.TryGetValue(alias, out var entryKey)
            || !index.Entries.TryGetValue(entryKey, out var entry))
        {
            return null;
        }

        return TryGetCachedIconCore(entry, now, allowStale, updateLastUsed, out isStale);
    }

    private string? TryGetCachedIconByEntryKey(
        RemoteIconCacheIndex index,
        string entryKey,
        DateTimeOffset now,
        bool allowStale,
        bool updateLastUsed,
        out bool isStale)
    {
        isStale = false;
        if (!index.Entries.TryGetValue(entryKey, out var entry))
            return null;

        return TryGetCachedIconCore(entry, now, allowStale, updateLastUsed, out isStale);
    }

    private string? TryGetCachedIconCore(
        RemoteIconCacheEntry entry,
        DateTimeOffset now,
        bool allowStale,
        bool updateLastUsed,
        out bool isStale)
    {
        isStale = false;
        var path = Path.Combine(cacheDirectory, entry.FileName);
        if (!File.Exists(path))
            return null;

        if (updateLastUsed)
            entry.LastUsedAt = now;
        isStale = now - entry.CachedAt > RefreshAfter;
        return isStale && !allowStale ? null : new Uri(path).AbsoluteUri;
    }
}
