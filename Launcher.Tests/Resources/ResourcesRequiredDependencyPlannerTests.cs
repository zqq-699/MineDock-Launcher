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

namespace Launcher.Tests.Resources;

public sealed class ResourcesRequiredDependencyPlannerTests
{
    [Theory]
    [InlineData("1.2.0", "1.1.9", true)]
    [InlineData("fabric-mc1.20.1-2.0.0", "1.9.0", true)]
    [InlineData("1.0.0-beta", "1.0.0", false)]
    [InlineData("not-a-version", "1.0.0", false)]
    public void DependencyVersionComparer_HandlesCommonModVersionForms(
        string installedVersion,
        string minimumVersion,
        bool expected)
    {
        Assert.Equal(
            expected,
            ResourceDependencyVersionComparer.IsGreaterThanOrEqual(installedVersion, minimumVersion));
    }

    [Fact]
    public void ResolveDependencyRequirementState_DetectsInstalledUpdateAndMissingStates()
    {
        var dependency = new ResourceProjectDependency
        {
            Project = new ResourceProject
            {
                ProjectId = "sodium",
                Slug = "sodium"
            }
        };
        var candidate = new RequiredDependencyInstallCandidate(
            dependency,
            new ResourceProjectVersion { VersionNumber = "1.2.0" },
            new ResourceProjectVersion { VersionNumber = "1.2.0" });

        var installed = new InstalledDependencyCatalog(new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["sodium"] = ["1.2.1"]
        });
        var updateRequired = new InstalledDependencyCatalog(new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["sodium"] = ["1.1.0"]
        });
        var missing = new InstalledDependencyCatalog([]);

        Assert.Equal(
            RequiredDependencyRequirementState.Installed,
            ResourcesRequiredDependencyPlanner.ResolveDependencyRequirementState(candidate, installed));
        Assert.Equal(
            RequiredDependencyRequirementState.UpdateRequired,
            ResourcesRequiredDependencyPlanner.ResolveDependencyRequirementState(candidate, updateRequired));
        Assert.Equal(
            RequiredDependencyRequirementState.Missing,
            ResourcesRequiredDependencyPlanner.ResolveDependencyRequirementState(candidate, missing));
    }
}
