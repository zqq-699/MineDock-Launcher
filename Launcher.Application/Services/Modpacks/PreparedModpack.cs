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

using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public enum ModpackPackageKind
{
    Modrinth,
    CurseForge
}

public sealed class PreparedModpack
{
    public ModpackPackageKind PackageKind { get; init; }

    public string SourceArchivePath { get; init; } = string.Empty;

    public string WorkingDirectory { get; init; } = string.Empty;

    public string? EmbeddedModrinthEntryName { get; init; }

    public string PackageName { get; init; } = string.Empty;

    public string MinecraftVersion { get; init; } = string.Empty;

    public LoaderKind Loader { get; init; } = LoaderKind.Vanilla;

    public string? LoaderVersion { get; init; }

    public bool HasOverrides { get; init; }

    public IReadOnlyList<PreparedModpackDownload> Files { get; init; } = [];

    public IReadOnlyList<ManualModpackDownload> ManualDownloads { get; set; } = [];

    public string? ManualDownloadsFilePath { get; set; }
}

public sealed class PreparedModpackDownload
{
    public string FileName { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string RelativePath { get; init; } = string.Empty;

    public string TargetDirectory { get; init; } = string.Empty;

    public string SourceUrl { get; init; } = string.Empty;

    public long? ProjectId { get; init; }

    public long? FileId { get; init; }

    public string? Sha1 { get; init; }

    public string? Sha512 { get; init; }
}
