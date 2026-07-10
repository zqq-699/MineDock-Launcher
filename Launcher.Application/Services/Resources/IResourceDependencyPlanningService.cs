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

public enum ResourceDependencyRequirementState
{
    Installed,
    UpdateRequired,
    Missing
}

public sealed record ResourceDependencyInstallCandidate(
    ResourceProjectDependency Dependency,
    ResourceProjectVersion? MinimumVersion,
    ResourceProjectVersion? InstallVersion,
    ResourceDependencyRequirementState State);

public sealed record ResourceDependencyInstallPlan(
    IReadOnlyList<ResourceDependencyInstallCandidate> Requirements,
    IReadOnlyList<ResourceDependencyInstallCandidate> MissingDependencies);

public sealed record ResourceDependencyInstallProgress(string DependencyTitle);

public interface IResourceDependencyPlanningService
{
    Task<ResourceDependencyInstallPlan> CreatePlanAsync(
        ResourceProjectVersion version,
        GameInstance instance,
        CancellationToken cancellationToken = default);

    Task InstallRequiredDependenciesAsync(
        IReadOnlyList<ResourceDependencyInstallCandidate> dependencies,
        GameInstance instance,
        IProgress<ResourceDependencyInstallProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class ResourceDependencyInstallException : Exception
{
    public ResourceDependencyInstallException(ResourceProject dependency)
        : base($"Required dependency cannot be installed automatically: {dependency.ProjectId}")
    {
        DependencyProjectId = dependency.ProjectId;
        DependencyTitle = string.IsNullOrWhiteSpace(dependency.Title) ? dependency.Slug : dependency.Title;
    }

    public string DependencyProjectId { get; }

    public string DependencyTitle { get; }
}
