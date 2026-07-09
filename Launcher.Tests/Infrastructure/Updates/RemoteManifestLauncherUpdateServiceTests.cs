using System.Net;
using Launcher.Application;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Updates;

namespace Launcher.Tests.Infrastructure.Updates;

public sealed class RemoteManifestLauncherUpdateServiceTests
{
    [Fact]
    public async Task CheckForUpdatesRequestsGitHubReleaseManifestFirst()
    {
        var handler = new ManifestHandler();
        handler.Respond(
            "https://github.test/update/release/latest.json",
            CreateManifest(versionCode: 1000199, channel: "release"));
        var service = CreateService(handler);

        var result = await service.CheckForUpdatesAsync("1.0.0", LauncherUpdateChannel.Release);

        Assert.False(result.IsFailed);
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("1.0.1", result.Update?.Version);
        Assert.Equal(1000199, result.Update?.VersionCode);
        Assert.Equal("https://download.test/MineDock_Launcher_x64.exe", result.Update?.DownloadUrl);
        Assert.Equal("MineDock_Launcher_x64.exe", result.Update?.DownloadFileName);
        Assert.Equal(LauncherUpdateAssetKind.WindowsX64Executable, result.Update?.AssetKind);
        Assert.True(result.Update?.CanAutoInstall);
        Assert.Equal(
            ["https://github.test/update/release/latest.json"],
            handler.Requests.Select(request => request.RequestUri!.ToString()));
        Assert.Contains(handler.Requests[0].Headers.UserAgent, value => value.Product?.Name == LauncherProjectLinks.GitHubUserAgent);
    }

    [Fact]
    public async Task CheckForUpdatesUsesBetaChannelInManifestUrls()
    {
        var handler = new ManifestHandler();
        handler.Respond(
            "https://github.test/update/beta/latest.json",
            CreateManifest(versionCode: 1000201, channel: "beta", versionName: "1.0.2-beta.1"));
        var service = CreateService(handler);

        var result = await service.CheckForUpdatesAsync("1.0.0", LauncherUpdateChannel.Beta);

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("1.0.2-beta.1", result.Update?.DisplayVersion);
        Assert.Equal(
            ["https://github.test/update/beta/latest.json"],
            handler.Requests.Select(request => request.RequestUri!.ToString()));
    }

    [Theory]
    [InlineData(ManifestFailureMode.Timeout)]
    [InlineData(ManifestFailureMode.NonSuccessStatusCode)]
    [InlineData(ManifestFailureMode.InvalidJson)]
    [InlineData(ManifestFailureMode.InvalidSchema)]
    [InlineData(ManifestFailureMode.InvalidAppId)]
    [InlineData(ManifestFailureMode.InvalidChannel)]
    public async Task CheckForUpdatesFallsBackToGiteeWhenGitHubFails(ManifestFailureMode failureMode)
    {
        var handler = new ManifestHandler();
        handler.Respond(
            "https://github.test/update/release/latest.json",
            CreateFailureResponse(failureMode));
        handler.Respond(
            "https://gitee.test/update/release/latest.json",
            CreateManifest(versionCode: 1000399, channel: "release", versionName: "1.0.3"));
        var service = CreateService(handler);

        var result = await service.CheckForUpdatesAsync("1.0.0", LauncherUpdateChannel.Release);

        Assert.False(result.IsFailed);
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("1.0.3", result.Update?.Version);
        Assert.Equal(
            [
                "https://github.test/update/release/latest.json",
                "https://gitee.test/update/release/latest.json"
            ],
            handler.Requests.Select(request => request.RequestUri!.ToString()));
    }

    [Fact]
    public async Task CheckForUpdatesReturnsFailedWhenAllSourcesFail()
    {
        var handler = new ManifestHandler();
        handler.Respond(
            "https://github.test/update/release/latest.json",
            new ManifestResponse(HttpStatusCode.InternalServerError, "{}"));
        handler.Respond(
            "https://gitee.test/update/release/latest.json",
            new ManifestResponse(HttpStatusCode.NotFound, "{}"));
        var service = CreateService(handler);

        var result = await service.CheckForUpdatesAsync("1.0.0", LauncherUpdateChannel.Release);

        Assert.True(result.IsFailed);
        Assert.False(result.IsUpdateAvailable);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task CheckForUpdatesReturnsLatestWhenRemoteVersionCodeIsNotNewer()
    {
        var handler = new ManifestHandler();
        handler.Respond(
            "https://github.test/update/release/latest.json",
            CreateManifest(versionCode: 1000099, channel: "release", versionName: "1.0.0"));
        var service = CreateService(handler);

        var result = await service.CheckForUpdatesAsync("1.0.0", LauncherUpdateChannel.Release);

        Assert.False(result.IsFailed);
        Assert.False(result.IsUpdateAvailable);
        Assert.Null(result.Update);
    }

    [Fact]
    public async Task CheckForUpdatesSortsDownloadUrlsAndMapsMandatoryState()
    {
        var handler = new ManifestHandler();
        handler.Respond(
            "https://github.test/update/release/latest.json",
            """
            {
              "schemaVersion": 1,
              "appId": "MineDock-Launcher",
              "channel": "release",
              "versionName": "1.0.4",
              "versionCode": 1000499,
              "publishedAt": "2026-01-01T00:00:00+08:00",
              "mandatory": false,
              "minSupportedVersionCode": 1000199,
              "releaseNotes": "notes",
              "assets": [
                {
                  "platform": "windows",
                  "arch": "x64",
                  "packageType": "exe",
                  "fileName": "MineDock_Launcher_x64.exe",
                  "size": 123,
                  "sha256": "ABCDEF",
                  "urls": [
                    { "name": "github", "url": "https://download.test/github.exe", "priority": 1 },
                    { "name": "oss", "url": "https://download.test/oss.exe", "priority": 2 },
                    { "name": "ftp", "url": "ftp://download.test/file.exe", "priority": 0 }
                  ]
                }
              ]
            }
            """);
        var service = CreateService(handler);

        var result = await service.CheckForUpdatesAsync("1.0.0", LauncherUpdateChannel.Release);

        Assert.True(result.IsUpdateAvailable);
        Assert.NotNull(result.Update);
        Assert.True(result.Update.IsMandatory);
        Assert.Equal(1000199, result.Update.MinSupportedVersionCode);
        Assert.Equal(123, result.Update.SizeBytes);
        Assert.Equal("ABCDEF", result.Update.Sha256);
        Assert.Equal("https://download.test/github.exe", result.Update.DownloadUrl);
        Assert.Equal(
            ["github", "oss"],
            result.Update.DownloadUrls!.Select(url => url.Name));
    }

    [Theory]
    [InlineData("0.9.1", 90199)]
    [InlineData("0.9.1-beta.1", 90101)]
    [InlineData("1.1.0", 1010099)]
    [InlineData("1.1.0-beta.1", 1010001)]
    public void TryCalculateVersionCodeUsesReleaseAndBetaSuffixRules(
        string version,
        int expectedVersionCode)
    {
        Assert.True(RemoteManifestLauncherUpdateService.TryCalculateVersionCode(version, out var versionCode));
        Assert.Equal(expectedVersionCode, versionCode);
    }

    private static RemoteManifestLauncherUpdateService CreateService(ManifestHandler handler)
    {
        return new RemoteManifestLauncherUpdateService(
            new HttpClient(handler),
            logger: null,
            manifestSources:
            [
                new LauncherUpdateManifestSource("github", "https://github.test/update/{0}/latest.json", 1),
                new LauncherUpdateManifestSource("gitee", "https://gitee.test/update/{0}/latest.json", 2)
            ]);
    }

    private static string CreateManifest(
        int versionCode,
        string channel,
        string versionName = "1.0.1")
    {
        return $$"""
        {
          "schemaVersion": 1,
          "appId": "MineDock-Launcher",
          "channel": "{{channel}}",
          "versionName": "{{versionName}}",
          "versionCode": {{versionCode}},
          "publishedAt": "2026-01-01T00:00:00+08:00",
          "mandatory": false,
          "minSupportedVersionCode": 0,
          "releaseNotes": "notes",
          "assets": [
            {
              "platform": "windows",
              "arch": "x64",
              "packageType": "exe",
              "fileName": "MineDock_Launcher_x64.exe",
              "size": 0,
              "sha256": "",
              "urls": [
                { "name": "github", "url": "https://download.test/MineDock_Launcher_x64.exe", "priority": 1 }
              ]
            }
          ]
        }
        """;
    }

    private static ManifestResponse CreateFailureResponse(ManifestFailureMode failureMode)
    {
        return failureMode switch
        {
            ManifestFailureMode.Timeout => ManifestResponse.Timeout,
            ManifestFailureMode.NonSuccessStatusCode => new ManifestResponse(HttpStatusCode.BadGateway, "{}"),
            ManifestFailureMode.InvalidJson => new ManifestResponse(HttpStatusCode.OK, "{"),
            ManifestFailureMode.InvalidSchema => new ManifestResponse(HttpStatusCode.OK, CreateManifest(1000199, "release").Replace("\"schemaVersion\": 1", "\"schemaVersion\": 99")),
            ManifestFailureMode.InvalidAppId => new ManifestResponse(HttpStatusCode.OK, CreateManifest(1000199, "release").Replace("MineDock-Launcher", "Other")),
            ManifestFailureMode.InvalidChannel => new ManifestResponse(HttpStatusCode.OK, CreateManifest(1000199, "beta")),
            _ => throw new ArgumentOutOfRangeException(nameof(failureMode), failureMode, null)
        };
    }

    public enum ManifestFailureMode
    {
        Timeout,
        NonSuccessStatusCode,
        InvalidJson,
        InvalidSchema,
        InvalidAppId,
        InvalidChannel
    }

    private sealed class ManifestHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, ManifestResponse> responses = new(StringComparer.OrdinalIgnoreCase);

        public List<HttpRequestMessage> Requests { get; } = [];

        public void Respond(string url, string response)
        {
            responses[url] = new ManifestResponse(HttpStatusCode.OK, response);
        }

        public void Respond(string url, ManifestResponse response)
        {
            responses[url] = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var url = request.RequestUri!.ToString();
            if (!responses.TryGetValue(url, out var response))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            if (response.IsTimeout)
                throw new TaskCanceledException("timeout");

            return Task.FromResult(new HttpResponseMessage(response.StatusCode)
            {
                Content = new StringContent(response.Content)
            });
        }
    }

    private sealed record ManifestResponse(HttpStatusCode StatusCode, string Content)
    {
        public static ManifestResponse Timeout { get; } = new(HttpStatusCode.OK, string.Empty)
        {
            IsTimeout = true
        };

        public bool IsTimeout { get; init; }
    }
}
