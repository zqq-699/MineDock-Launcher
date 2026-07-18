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

namespace Launcher.Infrastructure.Resources;

internal interface IResourceProviderClient
{
    ResourceProjectSource Source { get; }

    bool Supports(ResourceProjectKind kind);

    Task<ResourceProject?> GetProjectAsync(
        ResourceProjectReference reference,
        CancellationToken cancellationToken);

    Task<ResourceProviderSearchResult> SearchAsync(
        ResourceCatalogSearchRequest request,
        CancellationToken cancellationToken);

    Task<ResourceProjectVersionsResult> GetVersionsAsync(
        ResourceProjectVersionsRequest request,
        CancellationToken cancellationToken);

    Task<ResourceProjectDependenciesResult> GetDependenciesAsync(
        ResourceProjectDependenciesRequest request,
        CancellationToken cancellationToken);
}

internal sealed record ResourceProviderSearchResult(
    IReadOnlyList<ResourceProject> Projects,
    bool HasMore,
    bool IsUnavailable = false,
    bool IsApiKeyMissing = false);
