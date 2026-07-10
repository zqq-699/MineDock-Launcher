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

public interface IModpackExportService
{
    Task<ModpackExportResult> ExportAsync(
        ModpackExportRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ModpackExportRequest(
    GameInstance Instance,
    ModpackExportKind Kind,
    string Name,
    string Author,
    string Version,
    string OutputArchivePath,
    bool IncludeMods,
    bool IncludeDisabledMods,
    bool IncludeResourcePacks,
    bool IncludeShaderPacks,
    bool IncludeConfig);

public sealed record ModpackExportResult(
    bool IsSuccess,
    ModpackExportFailureReason FailureReason = ModpackExportFailureReason.None,
    string? OutputArchivePath = null,
    int ManifestFileCount = 0,
    int OverrideFileCount = 0);

public enum ModpackExportKind
{
    CurseForge,
    Modrinth
}

public enum ModpackExportFailureReason
{
    None,
    UnsupportedType,
    InvalidRequest,
    MissingCurseForgeApiKey,
    MissingLoaderVersion,
    CurseForgeApiFailed,
    ModrinthApiFailed,
    FileSystemError,
    UnexpectedError
}
