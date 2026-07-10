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

public sealed class ResourcesAvailableVersionListBuilderTests
{
    [Fact]
    public void MatchesFilters_UsesSearchVersionAndLoader()
    {
        var builder = new ResourcesAvailableVersionListBuilder(CreateOptions(showsLoaderFilters: true));
        var version = new ResourceProjectVersion
        {
            Name = "Sodium",
            VersionNumber = "0.5.8",
            FileName = "sodium-fabric-mc1.20.1.jar",
            VersionType = "release",
            GameVersions = ["1.20.1"],
            Loaders = ["fabric"]
        };

        Assert.True(builder.MatchesFilters(version, "1.20.1", "fabric", "sod"));
        Assert.False(builder.MatchesFilters(version, "1.19.4", "fabric", "sod"));
        Assert.False(builder.MatchesFilters(version, "1.20.1", "forge", "sod"));
        Assert.False(builder.MatchesFilters(version, "1.20.1", "fabric", "iris"));
    }

    [Fact]
    public void Build_GroupsVersionsByCompatibility()
    {
        var builder = new ResourcesAvailableVersionListBuilder(CreateOptions(showsLoaderFilters: true));
        var versions = new[]
        {
            new ResourceProjectVersion
            {
                Name = "Fabric build",
                GameVersions = ["1.20.1"],
                Loaders = ["fabric"]
            },
            new ResourceProjectVersion
            {
                Name = "Forge build",
                GameVersions = ["1.20.1"],
                Loaders = ["forge"]
            }
        };

        var result = builder.Build(
            versions,
            "All versions",
            selectedProject: null,
            fallbackIconKey: "fallback",
            selectedVersionId: "all",
            selectedLoaderId: "all",
            searchQuery: string.Empty);

        var headers = result.Items.OfType<ResourcesModVersionListHeaderItem>().Select(item => item.Title).ToArray();
        Assert.Equal(2, result.VisibleVersionCount);
        Assert.Contains("1.20.1-fabric", headers);
        Assert.Contains("1.20.1-forge", headers);
    }

    private static ResourcesOnlineProjectPageOptions CreateOptions(bool showsLoaderFilters)
    {
        return new ResourcesOnlineProjectPageOptions(
            ResourceProjectKind.Mod,
            "Mods",
            "fallback",
            showsLoaderFilters,
            "All versions",
            "All loaders",
            "Loading",
            "Empty",
            "Load error",
            "Loading more",
            "No more",
            "Load more error",
            "Missing API key",
            "Info",
            "Target",
            "Local",
            "Targets loading",
            "Targets error",
            "Versions loading",
            "Versions empty",
            "Versions local empty",
            "Versions filter empty",
            "Versions load error",
            "Versions loading more",
            "Versions no more",
            "Versions load more error",
            "All versions",
            "Pick folder",
            "Downloading",
            "Downloading {0}",
            "Downloaded {0}",
            "Download failed",
            "Installed {0}",
            "Install failed",
            "File exists {0}",
            []);
    }
}
