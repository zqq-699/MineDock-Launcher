using System.Net;
using Launcher.Application;
using Launcher.Application.Services;
using Launcher.Infrastructure.Updates;

namespace Launcher.Tests.Infrastructure.Updates;

public sealed class GitHubLauncherUpdateServiceTests
{
    [Theory]
    [InlineData("v1.0.1")]
    [InlineData("1.0.1")]
    public async Task CheckForUpdatesParsesReleaseTag(string tagName)
    {
        var handler = new GitHubReleasesHandler(
            $$"""
            [
              {
                "tag_name": "{{tagName}}",
                "html_url": "{{LauncherProjectLinks.GitHubReleasesUrl}}/tag/{{tagName}}",
                "body": "notes",
                "draft": false,
                "assets": [
                  {
                    "name": "MineDock_Launcher_x64.exe",
                    "browser_download_url": "https://example.test/MineDock_Launcher_x64.exe"
                  }
                ]
              }
            ]
            """);
        var service = new GitHubLauncherUpdateService(new HttpClient(handler));

        var result = await service.CheckForUpdatesAsync("1.0.0");

        Assert.False(result.IsFailed);
        Assert.True(result.IsUpdateAvailable);
        Assert.NotNull(result.Update);
        Assert.Equal("1.0.1", result.Update.Version);
        Assert.Equal("https://example.test/MineDock_Launcher_x64.exe", result.Update.DownloadUrl);
        Assert.Equal("MineDock_Launcher_x64.exe", result.Update.DownloadFileName);
        Assert.Equal(LauncherUpdateAssetKind.WindowsX64Executable, result.Update.AssetKind);
        Assert.True(result.Update.CanAutoInstall);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(LauncherProjectLinks.GitHubReleasesApiUrl, request.RequestUri!.ToString());
        Assert.Equal("api.github.com", request.RequestUri!.Host);
        Assert.Contains(request.Headers.UserAgent, value => value.Product?.Name == LauncherProjectLinks.GitHubUserAgent);
        Assert.True(request.Headers.TryGetValues("X-GitHub-Api-Version", out var apiVersions));
        Assert.Equal("2022-11-28", Assert.Single(apiVersions));
    }

    [Fact]
    public async Task CheckForUpdatesIgnoresDraftAndOlderReleases()
    {
        var handler = new GitHubReleasesHandler(
            """
            [
              {
                "tag_name": "v9.0.0",
                "html_url": "https://example.test/draft",
                "draft": true,
                "assets": []
              },
              {
                "tag_name": "v1.0.0",
                "html_url": "https://example.test/equal",
                "draft": false,
                "assets": []
              },
              {
                "tag_name": "v0.9.9",
                "html_url": "https://example.test/old",
                "draft": false,
                "assets": []
              }
            ]
            """);
        var service = new GitHubLauncherUpdateService(new HttpClient(handler));

        var result = await service.CheckForUpdatesAsync("1.0.0");

        Assert.False(result.IsFailed);
        Assert.False(result.IsUpdateAvailable);
        Assert.Null(result.Update);
    }

    [Fact]
    public async Task CheckForUpdatesSelectsLatestVersionAboveCurrent()
    {
        var handler = new GitHubReleasesHandler(
            """
            [
              {
                "tag_name": "v1.0.1",
                "html_url": "https://example.test/1.0.1",
                "draft": false,
                "assets": []
              },
              {
                "tag_name": "v1.0.2",
                "html_url": "https://example.test/1.0.2",
                "draft": false,
                "assets": []
              }
            ]
            """);
        var service = new GitHubLauncherUpdateService(new HttpClient(handler));

        var result = await service.CheckForUpdatesAsync("1.0.0");

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("1.0.2", result.Update?.Version);
        Assert.Equal("https://example.test/1.0.2", result.Update?.ReleasePageUrl);
        Assert.Equal("https://example.test/1.0.2", result.Update?.DownloadUrl);
    }

    [Fact]
    public async Task CheckForUpdatesPrefersX64ExecutableAsset()
    {
        var handler = new GitHubReleasesHandler(
            """
            [
              {
                "tag_name": "v1.0.1",
                "html_url": "https://example.test/release",
                "draft": false,
                "assets": [
                  {
                    "name": "MineDock_Launcher_ARM64.exe",
                    "browser_download_url": "https://example.test/MineDock_Launcher_ARM64.exe"
                  },
                  {
                    "name": "MineDock_Launcher_x64.exe.asc",
                    "browser_download_url": "https://example.test/MineDock_Launcher_x64.exe.asc"
                  },
                  {
                    "name": "Source code (zip)",
                    "browser_download_url": "https://example.test/source.zip"
                  },
                  {
                    "name": "MineDock_Launcher_x64.exe",
                    "browser_download_url": "https://example.test/MineDock_Launcher_x64.exe"
                  }
                ]
              }
            ]
            """);
        var service = new GitHubLauncherUpdateService(new HttpClient(handler));

        var result = await service.CheckForUpdatesAsync("1.0.0");

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("https://example.test/MineDock_Launcher_x64.exe", result.Update?.DownloadUrl);
        Assert.Equal(LauncherUpdateAssetKind.WindowsX64Executable, result.Update?.AssetKind);
    }

    [Fact]
    public async Task CheckForUpdatesMarksNonX64ExecutableAsNotAutoInstallable()
    {
        var handler = new GitHubReleasesHandler(
            """
            [
              {
                "tag_name": "v1.0.1",
                "html_url": "https://example.test/release",
                "draft": false,
                "assets": [
                  {
                    "name": "MineDock_Launcher_ARM64.exe",
                    "browser_download_url": "https://example.test/MineDock_Launcher_ARM64.exe"
                  },
                  {
                    "name": "MineDock_Launcher_x64.exe.asc",
                    "browser_download_url": "https://example.test/MineDock_Launcher_x64.exe.asc"
                  }
                ]
              }
            ]
            """);
        var service = new GitHubLauncherUpdateService(new HttpClient(handler));

        var result = await service.CheckForUpdatesAsync("1.0.0");

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("https://example.test/MineDock_Launcher_ARM64.exe", result.Update?.DownloadUrl);
        Assert.Equal(LauncherUpdateAssetKind.OtherExecutable, result.Update?.AssetKind);
        Assert.False(result.Update?.CanAutoInstall);
    }

    [Fact]
    public async Task CheckForUpdatesReturnsFailedWhenRequestFails()
    {
        var handler = new GitHubReleasesHandler("{}", HttpStatusCode.InternalServerError);
        var service = new GitHubLauncherUpdateService(new HttpClient(handler));

        var result = await service.CheckForUpdatesAsync("1.0.0");

        Assert.True(result.IsFailed);
        Assert.False(result.IsUpdateAvailable);
    }

    private sealed class GitHubReleasesHandler : HttpMessageHandler
    {
        private readonly string response;
        private readonly HttpStatusCode statusCode;

        public GitHubReleasesHandler(string response, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            this.response = response;
            this.statusCode = statusCode;
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(response)
            });
        }
    }
}
