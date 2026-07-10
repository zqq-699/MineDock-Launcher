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
using System.Text.Json;
using Launcher.Infrastructure.Minecraft;
using Launcher.Tests.Helpers;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class QuiltLoaderProviderTests : TestTempDirectory
{
    [Fact]
    public async Task QuiltLoaderProviderReturnsEmptyWhenMetadataEndpointReturnsNotFound()
    {
        var provider = new QuiltLoaderProvider(new HttpClient(new NotFoundHandler()));

        var versions = await provider.GetLoaderVersionsAsync("1.99.99");

        Assert.Empty(versions);
    }

    [Fact]
    public async Task QuiltLoaderProviderReturnsEmptyWhenMetadataEndpointReturnsBadRequest()
    {
        var provider = new QuiltLoaderProvider(new HttpClient(new BadRequestHandler()));

        var versions = await provider.GetLoaderVersionsAsync("1.7.2");

        Assert.Empty(versions);
    }

    [Fact]
    public async Task QuiltLoaderProviderLoadsVersionsFromMetadataEndpoint()
    {
        var provider = new QuiltLoaderProvider(new HttpClient(new QuiltMetadataHandler()));

        var versions = await provider.GetLoaderVersionsAsync("1.20.2");

        Assert.Equal(["0.29.2", "0.29.2-beta.5"], versions.Select(version => version.Version));
        Assert.True(versions[0].IsStable);
        Assert.False(versions[1].IsStable);
    }

    [Fact]
    public async Task QuiltVersionComposerCreatesOnlyFinalVersionDirectory()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var finalVersionName = await QuiltVersionComposer.CreateFinalVersionAsync(
            new HttpClient(new QuiltInstallHandler()),
            "1.20.2",
            "0.29.2",
            "1.20.2-quilt-0.29.2",
            minecraftDirectory,
            DownloadSourcePreference.Auto);

        var versionsDirectory = Path.Combine(minecraftDirectory, "versions");
        var versionDirectories = Directory.GetDirectories(versionsDirectory)
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.Equal("1.20.2-quilt-0.29.2", finalVersionName);
        Assert.Equal(["1.20.2-quilt-0.29.2"], versionDirectories);
        Assert.True(File.Exists(Path.Combine(versionsDirectory, "1.20.2-quilt-0.29.2", "1.20.2-quilt-0.29.2.json")));
        Assert.True(File.Exists(Path.Combine(versionsDirectory, "1.20.2-quilt-0.29.2", "1.20.2-quilt-0.29.2.jar")));
        using var quiltJson = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(versionsDirectory, "1.20.2-quilt-0.29.2", "1.20.2-quilt-0.29.2.json")));
        Assert.Equal("1.20.2", quiltJson.RootElement.GetProperty("launcher").GetProperty("minecraftVersion").GetString());
        Assert.Contains(
            quiltJson.RootElement.GetProperty("libraries").EnumerateArray(),
            library => string.Equals(
                "org.quiltmc:quilt-loader:0.29.2",
                library.GetProperty("name").GetString(),
                StringComparison.Ordinal));
    }

    private sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                RequestMessage = request
            });
        }
    }

    private sealed class BadRequestHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                RequestMessage = request
            });
        }
    }

    private sealed class QuiltMetadataHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = request.RequestUri?.AbsoluteUri switch
            {
                "https://meta.quiltmc.org/v3/versions/loader/1.20.2" => """
                    [
                      {
                        "loader": {
                          "maven": "org.quiltmc:quilt-loader:0.29.2-beta.5",
                          "version": "0.29.2-beta.5"
                        }
                      },
                      {
                        "loader": {
                          "maven": "org.quiltmc:quilt-loader:0.29.2",
                          "version": "0.29.2"
                        }
                      }
                    ]
                    """,
                _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(content)
            });
        }
    }

    private sealed class QuiltInstallHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = request.RequestUri?.AbsoluteUri switch
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
                _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
            };

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request
            };

            response.Content = content is null
                ? new ByteArrayContent("fake jar"u8.ToArray())
                : new StringContent(content);

            return Task.FromResult(response);
        }
    }
}
