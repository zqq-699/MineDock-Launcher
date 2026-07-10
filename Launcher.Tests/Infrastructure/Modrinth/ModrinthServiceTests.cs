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
using Launcher.Infrastructure.Modrinth;

namespace Launcher.Tests.Infrastructure.Modrinth;

public sealed class ModrinthServiceTests
{
    [Fact]
    public async Task ModrinthSearchAddsMinecraftVersionAndLoaderFacets()
    {
        var handler = new CaptureHandler("""
            {"hits":[{"project_id":"p1","slug":"sodium","title":"Sodium","description":"Fast","icon_url":null,"downloads":42}]}
            """);
        var service = new ModrinthService(new HttpClient(handler));

        var results = await service.SearchModsAsync("sodium", "1.20.1", LoaderKind.Fabric);

        Assert.Single(results);
        Assert.Contains("query=sodium", handler.LastRequest!.Query);
        Assert.Contains("versions%3A1.20.1", handler.LastRequest.Query);
        Assert.Contains("categories%3Afabric", handler.LastRequest.Query);
    }

    [Fact]
    public async Task ModrinthServiceInstallsFabricApiUsingFabricApiProjectSlug()
    {
        var handler = new SequencedHandler(
            """
            [{"version_number":"0.120.0+1.20.1","files":[{"filename":"fabric-api.jar","url":"https://cdn.modrinth.com/data/fabric-api.jar","primary":true}]}]
            """,
            "fabric api bytes");
        var service = new ModrinthService(new HttpClient(handler));
        var instanceDirectory = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"));
        var instance = new GameInstance
        {
            Name = "Fabric Pack",
            MinecraftVersion = "1.20.1",
            Loader = LoaderKind.Fabric,
            InstanceDirectory = instanceDirectory
        };

        var path = await service.InstallFabricApiAsync(instance, null);

        Assert.Contains("/project/fabric-api/version", handler.RequestUris[0].AbsoluteUri);
        Assert.Contains("game_versions=%5B%221.20.1%22%5D", handler.RequestUris[0].Query);
        Assert.True(File.Exists(path));
        Assert.Equal(Path.Combine(instanceDirectory, "mods", "fabric-api.jar"), path);
    }

    [Fact]
    public async Task ModrinthServiceGetsFabricApiVersions()
    {
        var handler = new CaptureHandler("""
            [
              {"id":"fabric-api-new","project_id":"P7dR8mSH","name":"Fabric API 0.92.2","version_number":"0.92.2+1.20.2","version_type":"release","files":[{"filename":"fabric-api-new.jar","url":"https://cdn.modrinth.com/data/fabric-api-new.jar","primary":true}]},
              {"id":"fabric-api-beta","project_id":"P7dR8mSH","name":"Fabric API 0.93.0-beta","version_number":"0.93.0-beta+1.20.2","version_type":"beta","files":[{"filename":"fabric-api-beta.jar","url":"https://cdn.modrinth.com/data/fabric-api-beta.jar","primary":true}]}
            ]
            """);
        var service = new ModrinthService(new HttpClient(handler));

        var versions = await service.GetFabricApiVersionsAsync("1.20.2");

        Assert.Contains("/project/fabric-api/version", handler.LastRequest!.AbsoluteUri);
        Assert.Contains("loaders=%5B%22fabric%22%5D", handler.LastRequest.Query);
        Assert.Contains("game_versions=%5B%221.20.2%22%5D", handler.LastRequest.Query);
        Assert.Equal(2, versions.Count);
        Assert.Equal("fabric-api-new", versions[0].VersionId);
        Assert.Equal("0.92.2+1.20.2", versions[0].VersionNumber);
        Assert.True(versions[0].IsStable);
        Assert.Equal("fabric-api-beta", versions[1].VersionId);
        Assert.False(versions[1].IsStable);
    }

    [Fact]
    public async Task ModrinthServiceInstallsSelectedFabricApiVersion()
    {
        var handler = new SequencedHandler(
            """
            {"id":"fabric-api-new","project_id":"P7dR8mSH","name":"Fabric API 0.92.2","version_number":"0.92.2+1.20.2","version_type":"release","files":[{"filename":"fabric-api-secondary.jar","url":"https://cdn.modrinth.com/data/fabric-api-secondary.jar","primary":false},{"filename":"fabric-api-primary.jar","url":"https://cdn.modrinth.com/data/fabric-api-primary.jar","primary":true}]}
            """,
            "fabric api bytes");
        var service = new ModrinthService(new HttpClient(handler));
        var instanceDirectory = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"));
        var instance = new GameInstance
        {
            Name = "Fabric Pack",
            MinecraftVersion = "1.20.2",
            Loader = LoaderKind.Fabric,
            InstanceDirectory = instanceDirectory
        };

        var path = await service.InstallFabricApiAsync(instance, "fabric-api-new", null);

        Assert.Contains("/version/fabric-api-new", handler.RequestUris[0].AbsoluteUri);
        Assert.Contains("fabric-api-primary.jar", handler.RequestUris[1].AbsoluteUri);
        Assert.True(File.Exists(path));
        Assert.Equal(Path.Combine(instanceDirectory, "mods", "fabric-api-primary.jar"), path);
    }

    [Fact]
    public async Task ModrinthServiceGetsQuiltStandardLibraryVersions()
    {
        var handler = new CaptureHandler("""
            [
              {"id":"qsl-new","project_id":"qvIfYCYJ","name":"QFAPI / QSL 8.0.0","version_number":"8.0.0","version_type":"release","files":[{"filename":"qsl-new.jar","url":"https://cdn.modrinth.com/data/qsl-new.jar","primary":true}]},
              {"id":"qsl-beta","project_id":"qvIfYCYJ","name":"QFAPI / QSL 8.0.0-beta","version_number":"8.0.0-beta","version_type":"beta","files":[{"filename":"qsl-beta.jar","url":"https://cdn.modrinth.com/data/qsl-beta.jar","primary":true}]}
            ]
            """);
        var service = new ModrinthService(new HttpClient(handler));

        var versions = await service.GetQuiltStandardLibraryVersionsAsync("1.20.2");

        Assert.Contains("/project/qsl/version", handler.LastRequest!.AbsoluteUri);
        Assert.Contains("loaders=%5B%22quilt%22%5D", handler.LastRequest.Query);
        Assert.Contains("game_versions=%5B%221.20.2%22%5D", handler.LastRequest.Query);
        Assert.Equal(2, versions.Count);
        Assert.Equal("qsl-new", versions[0].VersionId);
        Assert.Equal("8.0.0", versions[0].VersionNumber);
        Assert.True(versions[0].IsStable);
        Assert.Equal("qsl-beta", versions[1].VersionId);
        Assert.False(versions[1].IsStable);
    }

    [Fact]
    public async Task ModrinthServiceInstallsSelectedQuiltStandardLibraryVersion()
    {
        var handler = new SequencedHandler(
            """
            {"id":"qsl-new","project_id":"qvIfYCYJ","name":"QFAPI / QSL 8.0.0","version_number":"8.0.0","version_type":"release","files":[{"filename":"qsl-secondary.jar","url":"https://cdn.modrinth.com/data/qsl-secondary.jar","primary":false},{"filename":"qsl-primary.jar","url":"https://cdn.modrinth.com/data/qsl-primary.jar","primary":true}]}
            """,
            "qsl bytes");
        var service = new ModrinthService(new HttpClient(handler));
        var instanceDirectory = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"));
        var instance = new GameInstance
        {
            Name = "Quilt Pack",
            MinecraftVersion = "1.20.2",
            Loader = LoaderKind.Quilt,
            InstanceDirectory = instanceDirectory
        };

        var path = await service.InstallQuiltStandardLibraryAsync(instance, "qsl-new", null);

        Assert.Contains("/version/qsl-new", handler.RequestUris[0].AbsoluteUri);
        Assert.Contains("qsl-primary.jar", handler.RequestUris[1].AbsoluteUri);
        Assert.True(File.Exists(path));
        Assert.Equal(Path.Combine(instanceDirectory, "mods", "qsl-primary.jar"), path);
    }

    private sealed class SequencedHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses;

        public SequencedHandler(string versionsResponseBody, string fileResponseBody)
        {
            responses = new Queue<HttpResponseMessage>(
            [
                CreateJsonResponse(versionsResponseBody),
                CreateBinaryResponse(fileResponseBody)
            ]);
        }

        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            return Task.FromResult(responses.Dequeue());
        }

        private static HttpResponseMessage CreateJsonResponse(string body)
        {
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            };
        }

        private static HttpResponseMessage CreateBinaryResponse(string body)
        {
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            };
        }
    }
}

