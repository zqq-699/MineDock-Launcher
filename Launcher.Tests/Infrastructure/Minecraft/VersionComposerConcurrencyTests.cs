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

using System.Net;
using System.Net.Http;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;
using Launcher.Tests.Helpers;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class VersionComposerConcurrencyTests : TestTempDirectory
{
    [Fact]
    public async Task FabricAndQuiltComposersReleaseManifestLeaseBeforeRequestingVersionMetadata()
    {
        var fabricMinecraftDirectory = Path.Combine(TempRoot, "fabric", ".minecraft");
        var quiltMinecraftDirectory = Path.Combine(TempRoot, "quilt", ".minecraft");
        var handler = new CoordinatedComposerInstallHandler();
        using var httpClient = new HttpClient(handler);

        var fabricTask = FabricVersionComposer.CreateFinalVersionAsync(
            httpClient,
            "1.20.2",
            "0.19.3",
            "1.20.2-fabric-0.19.3",
            fabricMinecraftDirectory,
            DownloadSourcePreference.Auto);
        var quiltTask = QuiltVersionComposer.CreateFinalVersionAsync(
            httpClient,
            "1.20.2",
            "0.29.2",
            "1.20.2-quilt-0.29.2",
            quiltMinecraftDirectory,
            DownloadSourcePreference.Auto);

        await Task.WhenAll(fabricTask, quiltTask).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(File.Exists(Path.Combine(
            fabricMinecraftDirectory,
            "versions",
            "1.20.2-fabric-0.19.3",
            "1.20.2-fabric-0.19.3.json")));
        Assert.True(File.Exists(Path.Combine(
            quiltMinecraftDirectory,
            "versions",
            "1.20.2-quilt-0.29.2",
            "1.20.2-quilt-0.29.2.json")));
    }

    private sealed class CoordinatedComposerInstallHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource bothManifestRequestsArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int manifestRequestCount;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var absoluteUri = request.RequestUri?.AbsoluteUri ?? string.Empty;
            if (string.Equals(
                absoluteUri,
                "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json",
                StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref manifestRequestCount) == 2)
                    bothManifestRequestsArrived.TrySetResult();

                await bothManifestRequestsArrived.Task.WaitAsync(cancellationToken);
            }

            var content = absoluteUri switch
            {
                "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json" => """
                    {
                      "versions": [
                        {
                          "id": "1.20.2",
                          "url": "https://example.test/mojang/1.20.2.json"
                        }
                      ]
                    }
                    """,
                "https://example.test/mojang/1.20.2.json" => """
                    {
                      "id": "1.20.2",
                      "type": "release",
                      "downloads": {
                        "client": {
                          "url": "https://example.test/mojang/1.20.2.jar"
                        }
                      },
                      "libraries": [
                        { "name": "com.mojang:patchy:2.2.10" }
                      ],
                      "arguments": {
                        "game": [ "--username", "${auth_player_name}" ],
                        "jvm": [ "-Djava.library.path=${natives_directory}" ]
                      }
                    }
                    """,
                "https://meta.fabricmc.net/v2/versions/loader/1.20.2/0.19.3/profile/json" => """
                    {
                      "id": "fabric-loader-0.19.3-1.20.2",
                      "inheritsFrom": "1.20.2",
                      "mainClass": "net.fabricmc.loader.impl.launch.knot.KnotClient",
                      "libraries": [
                        { "name": "net.fabricmc:fabric-loader:0.19.3" }
                      ],
                      "arguments": {
                        "game": [ "--fabric", "1" ],
                        "jvm": [ "-Dfabric.skipMcProvider=true" ]
                      }
                    }
                    """,
                "https://meta.quiltmc.org/v3/versions/loader/1.20.2/0.29.2/profile/json" => """
                    {
                      "id": "quilt-loader-0.29.2-1.20.2",
                      "inheritsFrom": "1.20.2",
                      "mainClass": "org.quiltmc.loader.impl.launch.knot.KnotClient",
                      "libraries": [
                        { "name": "org.quiltmc:quilt-loader:0.29.2", "url": "https://maven.quiltmc.org/repository/release/" },
                        { "name": "org.quiltmc:hashed:1.20.2", "url": "https://maven.quiltmc.org/repository/release/" },
                        { "name": "net.fabricmc:intermediary:1.20.2", "url": "https://maven.fabricmc.net/" }
                      ],
                      "arguments": {
                        "game": [],
                        "jvm": []
                      }
                    }
                    """,
                "https://example.test/mojang/1.20.2.jar" => null,
                _ => throw new InvalidOperationException($"Unexpected request: {absoluteUri}")
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = content is null
                    ? new ByteArrayContent("fake jar"u8.ToArray())
                    : new StringContent(content)
            };
        }
    }
}
