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

using System.Text.Json.Nodes;
using Launcher.Infrastructure.Minecraft;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class ManagedLibraryArtifactResolverTests
{
    [Theory]
    [InlineData("com.example:sample:1.2.3", null, "com/example/sample/1.2.3/sample-1.2.3.jar")]
    [InlineData("com.example:sample:1.2.3:natives-windows", null, "com/example/sample/1.2.3/sample-1.2.3-natives-windows.jar")]
    [InlineData("com.example:sample:1.2.3@zip", "client", "com/example/sample/1.2.3/sample-1.2.3-client.zip")]
    public void BuildsMavenPaths(string coordinate, string? classifier, string expectedPath)
    {
        var succeeded = ManagedLibraryArtifactResolver.TryBuildMavenPath(
            coordinate,
            classifier,
            out var relativePath);

        Assert.True(succeeded);
        Assert.Equal(expectedPath, relativePath);
    }

    [Fact]
    public void ResolvesLegacyLibraryWithoutDownloadMetadata()
    {
        var library = JsonNode.Parse("""
            {
              "name": "net.fabricmc:fabric-loader:0.16.10"
            }
            """)!.AsObject();

        var artifact = Assert.Single(ManagedLibraryArtifactResolver.EnumerateDownloads(library));

        Assert.Equal("net/fabricmc/fabric-loader/0.16.10/fabric-loader-0.16.10.jar", artifact.RelativePath);
        Assert.StartsWith("https://maven.fabricmc.net/", artifact.Url, StringComparison.Ordinal);
    }

    [Fact]
    public void AppliesMatchingRulesInDeclarationOrder()
    {
        var osName = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        var library = JsonNode.Parse($$"""
            {
              "rules": [
                { "action": "allow", "os": { "name": "{{osName}}" } },
                { "action": "disallow", "os": { "name": "{{osName}}" } }
              ]
            }
            """)!.AsObject();

        Assert.False(ManagedLibraryArtifactResolver.IsAllowed(library));
    }
}
