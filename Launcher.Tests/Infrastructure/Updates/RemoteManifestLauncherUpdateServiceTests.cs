using System.Net;
using System.Text;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Updates;

namespace Launcher.Tests.Infrastructure.Updates;

public sealed class RemoteManifestLauncherUpdateServiceTests
{
    private const string KeyId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Sha256 = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string GiteeManifest = "https://gitee.com/zqq-699/BlockHelm-Launcher/raw/update-manifests/update/release/latest.json";
    private const string GitHubManifest = "https://raw.githubusercontent.com/zqq-699/BlockHelm-Launcher/update-manifests/update/release/latest.json";
    private static readonly string ValidSignature = Convert.ToBase64String(new byte[64]);

    [Fact]
    public async Task ValidSignedManifestIsAccepted()
    {
        var manifest = CreateManifest();
        var service = CreateService((GiteeManifest, HttpStatusCode.OK, manifest), (GiteeManifest + ".sig", HttpStatusCode.OK, ValidSignature));

        var result = await service.CheckForUpdatesAsync("1.0.0", LauncherUpdateChannel.Release);

        Assert.False(result.IsFailed);
        Assert.True(result.IsUpdateAvailable);
        Assert.True(result.Update?.CanAutoInstall);
        Assert.Equal(KeyId, result.Update?.KeyId);
        Assert.Equal(12, result.Update?.SizeBytes);
        Assert.Equal(Sha256, result.Update?.Sha256);
    }

    [Fact]
    public async Task OneUnavailableMirrorAllowsOtherValidMirror()
    {
        var manifest = CreateManifest();
        var service = CreateService(
            new[]
            {
                new LauncherUpdateManifestSource("gitee", GiteeManifest.Replace("/release/latest.json", "/{0}/latest.json"), 1),
                new LauncherUpdateManifestSource("github", GitHubManifest.Replace("/release/latest.json", "/{0}/latest.json"), 2)
            },
            (GiteeManifest, HttpStatusCode.ServiceUnavailable, ""),
            (GiteeManifest + ".sig", HttpStatusCode.ServiceUnavailable, ""),
            (GitHubManifest, HttpStatusCode.OK, manifest),
            (GitHubManifest + ".sig", HttpStatusCode.OK, ValidSignature));

        var result = await service.CheckForUpdatesAsync("1.0.0", LauncherUpdateChannel.Release);

        Assert.False(result.IsFailed);
        Assert.True(result.IsUpdateAvailable);
    }

    [Fact]
    public async Task InvalidSignatureFallsBackToNextValidMirror()
    {
        var manifest = CreateManifest();
        var invalidSignature = Convert.ToBase64String(Enumerable.Repeat((byte)0xff, 64).ToArray());
        var service = CreateService(DefaultSources,
            (GiteeManifest, HttpStatusCode.OK, manifest),
            (GiteeManifest + ".sig", HttpStatusCode.OK, invalidSignature),
            (GitHubManifest, HttpStatusCode.OK, manifest),
            (GitHubManifest + ".sig", HttpStatusCode.OK, ValidSignature));

        var result = await service.CheckForUpdatesAsync("1.0.0", LauncherUpdateChannel.Release);

        Assert.False(result.IsFailed);
        Assert.True(result.IsUpdateAvailable);
    }

    [Fact]
    public async Task FirstValidManifestWinsWithoutLoadingEveryMirror()
    {
        var service = CreateService(DefaultSources,
            (GiteeManifest, HttpStatusCode.OK, CreateManifest("1.1.0")),
            (GiteeManifest + ".sig", HttpStatusCode.OK, ValidSignature),
            (GitHubManifest, HttpStatusCode.OK, CreateManifest("1.1.1")),
            (GitHubManifest + ".sig", HttpStatusCode.OK, ValidSignature));

        var result = await service.CheckForUpdatesAsync("1.0.0", LauncherUpdateChannel.Release);

        Assert.False(result.IsFailed);
        Assert.Equal("1.1.0", result.Update?.Version);
    }

    [Fact]
    public async Task LaterMirrorSignatureDoesNotNeedToBeComparedAfterValidSource()
    {
        var manifest = CreateManifest();
        var otherCanonicalSignature = Convert.ToBase64String(Enumerable.Repeat((byte)1, 64).ToArray());
        var service = CreateService(DefaultSources,
            (GiteeManifest, HttpStatusCode.OK, manifest),
            (GiteeManifest + ".sig", HttpStatusCode.OK, ValidSignature),
            (GitHubManifest, HttpStatusCode.OK, manifest),
            (GitHubManifest + ".sig", HttpStatusCode.OK, otherCanonicalSignature));

        var result = await service.CheckForUpdatesAsync("1.0.0", LauncherUpdateChannel.Release);

        Assert.False(result.IsFailed);
    }

    [Fact]
    public async Task OversizedManifestIsRejectedBeforeParsing()
    {
        var oversized = new string(' ', 1024 * 1024 + 1);
        var service = CreateService(
            (GiteeManifest, HttpStatusCode.OK, oversized),
            (GiteeManifest + ".sig", HttpStatusCode.OK, ValidSignature));

        var result = await service.CheckForUpdatesAsync("1.0.0", LauncherUpdateChannel.Release);

        Assert.True(result.IsFailed);
    }

    [Theory]
    [InlineData(0, Sha256)]
    [InlineData(12, "abcd")]
    public async Task MissingRequiredExecutableIntegrityMetadataIsRejected(long size, string sha256)
    {
        var manifest = CreateManifest(size: size, sha256: sha256);
        var service = CreateService((GiteeManifest, HttpStatusCode.OK, manifest), (GiteeManifest + ".sig", HttpStatusCode.OK, ValidSignature));

        var result = await service.CheckForUpdatesAsync("1.0.0", LauncherUpdateChannel.Release);

        Assert.True(result.IsFailed);
    }

    [Fact]
    public async Task HttpExecutableUrlIsRejectedAfterSignatureVerification()
    {
        var manifest = CreateManifest(downloadUrl: "http://github.com/zqq-699/BlockHelm-Launcher/test.exe");
        var service = CreateService((GiteeManifest, HttpStatusCode.OK, manifest), (GiteeManifest + ".sig", HttpStatusCode.OK, ValidSignature));

        var result = await service.CheckForUpdatesAsync("1.0.0", LauncherUpdateChannel.Release);

        Assert.True(result.IsFailed);
    }

    [Fact]
    public void VersionCodeCalculationRemainsCompatible()
    {
        Assert.True(RemoteManifestLauncherUpdateService.TryCalculateVersionCode("0.9.3-beta.1", out var beta));
        Assert.Equal(90301, beta);
        Assert.True(RemoteManifestLauncherUpdateService.TryCalculateVersionCode("0.9.3", out var release));
        Assert.Equal(90399, release);
    }

    private static IReadOnlyList<LauncherUpdateManifestSource> DefaultSources =>
    [
        new("gitee", GiteeManifest.Replace("/release/latest.json", "/{0}/latest.json"), 1),
        new("github", GitHubManifest.Replace("/release/latest.json", "/{0}/latest.json"), 2)
    ];

    private static RemoteManifestLauncherUpdateService CreateService(params (string Url, HttpStatusCode Status, string Content)[] responses) =>
        CreateService([DefaultSources[0]], responses);

    private static RemoteManifestLauncherUpdateService CreateService(
        IReadOnlyList<LauncherUpdateManifestSource> sources,
        params (string Url, HttpStatusCode Status, string Content)[] responses)
    {
        var handler = new ResponseHandler(responses);
        return new RemoteManifestLauncherUpdateService(new HttpClient(handler), null, sources, new TestVerifier());
    }

    private static string CreateManifest(
        string version = "1.1.0",
        long size = 12,
        string sha256 = Sha256,
        string downloadUrl = "https://github.com/zqq-699/BlockHelm-Launcher/releases/download/v1.1.0/BlockHelm_Launcher_x64.exe") => $$"""
    {
      "schemaVersion": 1,
      "appId": "BlockHelm-Launcher",
      "keyId": "{{KeyId}}",
      "channel": "release",
      "versionName": "{{version}}",
      "versionCode": 1010099,
      "publishedAt": "2026-07-12T00:00:00Z",
      "mandatory": false,
      "minSupportedVersionCode": 0,
      "releaseNotes": "test",
      "assets": [{
        "platform": "windows", "arch": "x64", "packageType": "exe",
        "fileName": "BlockHelm_Launcher_x64.exe", "size": {{size}}, "sha256": "{{sha256}}",
        "urls": [{ "name": "github", "url": "{{downloadUrl}}", "priority": 1 }]
      }]
    }
    """;

    private sealed class TestVerifier : IUpdateManifestSignatureVerifier
    {
        public string KeyId => RemoteManifestLauncherUpdateServiceTests.KeyId;
        public bool Verify(ReadOnlySpan<byte> manifestBytes, ReadOnlySpan<byte> signatureBytes) =>
            signatureBytes.Length == 64 && signatureBytes[0] != 0xff;
    }

    private sealed class ResponseHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Status, string Content)> responses;
        public ResponseHandler(IEnumerable<(string Url, HttpStatusCode Status, string Content)> values) =>
            responses = values.ToDictionary(value => value.Url, value => (value.Status, value.Content), StringComparer.OrdinalIgnoreCase);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var value = responses.TryGetValue(request.RequestUri!.AbsoluteUri, out var configured)
                ? configured
                : (Status: HttpStatusCode.NotFound, Content: string.Empty);
            return Task.FromResult(new HttpResponseMessage(value.Status)
            {
                RequestMessage = request,
                Content = new StringContent(value.Content, Encoding.UTF8)
            });
        }
    }
}
