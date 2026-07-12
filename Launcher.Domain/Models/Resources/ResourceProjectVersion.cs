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

namespace Launcher.Domain.Models;

public sealed class ResourceProjectVersion
{
    public ResourceProjectKind Kind { get; init; } = ResourceProjectKind.Mod;

    public string VersionId { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string VersionNumber { get; init; } = string.Empty;

    public string VersionType { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    public string PrimaryDownloadUrl { get; init; } = string.Empty;

    public IReadOnlyList<string> FallbackDownloadUrls { get; init; } = [];

    public long? ExpectedFileSize { get; init; }

    public IReadOnlyList<ResourceFileHash> FileHashes { get; init; } = [];

    public long Downloads { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }

    public IReadOnlyList<string> GameVersions { get; init; } = [];

    public IReadOnlyList<string> Loaders { get; init; } = [];

    public IReadOnlyList<ResourceProjectDependency> RequiredDependencies { get; init; } = [];
}
