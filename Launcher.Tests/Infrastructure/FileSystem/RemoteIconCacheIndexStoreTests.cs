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

using Launcher.Infrastructure.FileSystem;
using Launcher.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Tests.Infrastructure.FileSystem;

public sealed class RemoteIconCacheIndexStoreTests : TestTempDirectory
{
    [Fact]
    public async Task SavesAndLoadsCacheIndexWithoutChangingAliases()
    {
        var cacheDirectory = Path.Combine(TempRoot, "remote-icons");
        var store = new RemoteIconCacheIndexStore(cacheDirectory, NullLogger.Instance);
        var cachedAt = DateTimeOffset.Parse("2026-01-02T03:04:05Z");
        var index = new RemoteIconCacheIndex();
        index.Entries["modrinth:project"] = new RemoteIconCacheEntry
        {
            Source = "modrinth",
            ProjectId = "project",
            IconUrl = "https://example.test/icon.png",
            FileName = "icon.png",
            CachedAt = cachedAt,
            LastUsedAt = cachedAt,
            SizeBytes = 42
        };
        index.Aliases["sha1:abc"] = "modrinth:project";
        index.FileAliases["file:test"] = "modrinth:project";

        await store.SaveAsync(index, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Equal("modrinth:project", loaded.Aliases["sha1:abc"]);
        Assert.Equal("modrinth:project", loaded.FileAliases["file:test"]);
        Assert.Equal(42, loaded.Entries["modrinth:project"].SizeBytes);
        Assert.Empty(Directory.GetFiles(cacheDirectory, "*.tmp"));
    }
}
