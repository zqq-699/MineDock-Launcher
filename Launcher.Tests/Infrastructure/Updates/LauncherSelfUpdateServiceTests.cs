using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Launcher.Application;
using Launcher.Application.Services;
using Launcher.Infrastructure.Updates;

namespace Launcher.Tests.Infrastructure.Updates;

public sealed class LauncherSelfUpdateServiceTests : IDisposable
{
    private const string GiteeUrl = "https://gitee.com/zqq-699/BlockHelm-Launcher/releases/download/v1.0.1/BlockHelm_Launcher_x64.exe";
    private const string GitHubUrl = "https://github.com/zqq-699/BlockHelm-Launcher/releases/download/v1.0.1/BlockHelm_Launcher_x64.exe";
    private const string KeyId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("new launcher");
    private static readonly string PayloadHash = Convert.ToHexString(SHA256.HashData(Payload)).ToLowerInvariant();
    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), "launcher-self-update-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task VerifiedExecutableStartsUpdateMode()
    {
        ProcessStartInfo? startedProcess = null;
        var handler = new DownloadHandler().Respond(GitHubUrl, HttpStatusCode.OK, Payload);
        var service = CreateService(handler, startInfo => { startedProcess = startInfo; return true; });

        var result = await service.StartUpdateAsync(CreateUpdate());

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(Payload, await File.ReadAllBytesAsync(result.DownloadedFilePath!));
        Assert.NotNull(startedProcess);
        Assert.Contains("--apply-update", startedProcess.ArgumentList);
        Assert.Contains("--pid", startedProcess.ArgumentList);
        Assert.Contains("1234", startedProcess.ArgumentList);
        Assert.Contains("--target", startedProcess.ArgumentList);
        Assert.Contains(Path.Combine(tempRoot, "BlockHelm-Launcher.exe"), startedProcess.ArgumentList);
        Assert.Contains("--restart", startedProcess.ArgumentList);
    }

    [Fact]
    public async Task FailedUpdateProcessStartDeletesDownloadedCache()
    {
        var handler = new DownloadHandler().Respond(GitHubUrl, HttpStatusCode.OK, Payload);

        var result = await CreateService(handler, _ => false).StartUpdateAsync(CreateUpdate());

        Assert.False(result.Succeeded);
        var updatesRoot = Path.Combine(
            tempRoot,
            LauncherApplicationIdentity.StorageDirectoryName,
            "cache",
            "updates");
        Assert.False(Directory.Exists(updatesRoot));
    }

    [Fact]
    public async Task NetworkFailureFallsBackToSecondOfficialMirror()
    {
        var handler = new DownloadHandler()
            .Respond(GiteeUrl, HttpStatusCode.ServiceUnavailable, [])
            .Respond(GitHubUrl, HttpStatusCode.OK, Payload);
        var update = CreateUpdate([
            new LauncherUpdateDownloadUrl("gitee", GiteeUrl, 1),
            new LauncherUpdateDownloadUrl("github", GitHubUrl, 2)]);

        var result = await CreateService(handler, _ => true).StartUpdateAsync(update);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal([GiteeUrl, GitHubUrl], handler.Requests);
    }

    [Fact]
    public async Task HashFailureDoesNotFallbackOrStartAndDeletesTemporaryFile()
    {
        var starts = 0;
        var handler = new DownloadHandler()
            .Respond(GiteeUrl, HttpStatusCode.OK, Payload)
            .Respond(GitHubUrl, HttpStatusCode.OK, Payload);
        var update = CreateUpdate([
            new LauncherUpdateDownloadUrl("gitee", GiteeUrl, 1),
            new LauncherUpdateDownloadUrl("github", GitHubUrl, 2)]) with { Sha256 = new string('0', 64) };

        var result = await CreateService(handler, _ => { starts++; return true; }).StartUpdateAsync(update);

        Assert.False(result.Succeeded);
        Assert.Equal([GiteeUrl], handler.Requests);
        Assert.Equal(0, starts);
        Assert.Empty(Directory.EnumerateFiles(tempRoot, "*.download", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(999)]
    public async Task SizeMismatchNeverStartsAndDeletesTemporaryFile(long signedSize)
    {
        var starts = 0;
        var handler = new DownloadHandler().Respond(GitHubUrl, HttpStatusCode.OK, Payload);
        var result = await CreateService(handler, _ => { starts++; return true; })
            .StartUpdateAsync(CreateUpdate() with { SizeBytes = signedSize });

        Assert.False(result.Succeeded);
        Assert.Equal(0, starts);
        Assert.Empty(Directory.EnumerateFiles(tempRoot, "*.download", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(0, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData(12, "abcd")]
    public async Task MissingSignedIntegrityMetadataNeverDownloads(long size, string sha256)
    {
        var handler = new DownloadHandler().Respond(GitHubUrl, HttpStatusCode.OK, Payload);
        var update = CreateUpdate() with { SizeBytes = size, Sha256 = sha256 };

        var result = await CreateService(handler, _ => true).StartUpdateAsync(update);

        Assert.False(result.Succeeded);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("http://github.com/zqq-699/BlockHelm-Launcher/test.exe")]
    [InlineData("https://example.test/BlockHelm_Launcher_x64.exe")]
    public async Task InvalidExecutableUrlNeverDownloads(string url)
    {
        var handler = new DownloadHandler();
        var result = await CreateService(handler, _ => true).StartUpdateAsync(CreateUpdate() with
        {
            DownloadUrl = url,
            DownloadUrls = [new LauncherUpdateDownloadUrl("invalid", url, 1)]
        });

        Assert.False(result.Succeeded);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CrossProviderHttpsRedirectIsAllowedAndFollowed()
    {
        var handler = new DownloadHandler().Redirect(GiteeUrl, GitHubUrl).Respond(GitHubUrl, HttpStatusCode.OK, Payload);
        var result = await CreateService(handler, _ => true).StartUpdateAsync(CreateUpdate([
            new LauncherUpdateDownloadUrl("gitee", GiteeUrl, 1)]));

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal([GiteeUrl, GitHubUrl], handler.Requests);
    }

    private LauncherSelfUpdateService CreateService(DownloadHandler handler, Func<ProcessStartInfo, bool> startProcess)
    {
        Directory.CreateDirectory(tempRoot);
        return new LauncherSelfUpdateService(
            new HttpClient(handler), null, tempRoot, Path.Combine(tempRoot, "BlockHelm-Launcher.exe"), 1234, startProcess);
    }

    private static LauncherUpdateInfo CreateUpdate(IReadOnlyList<LauncherUpdateDownloadUrl>? urls = null) => new(
        "1.0.1", "1.0.1", "https://github.com/zqq-699/BlockHelm-Launcher/releases/tag/v1.0.1",
        GitHubUrl, null, "BlockHelm_Launcher_x64.exe", LauncherUpdateAssetKind.WindowsX64Executable,
        Payload.Length, PayloadHash, KeyId, DownloadUrls: urls);

    public void Dispose()
    {
        if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
    }

    private sealed class DownloadHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> responses = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Requests { get; } = [];

        public DownloadHandler Respond(string url, HttpStatusCode status, byte[] content)
        {
            responses[url] = request => new HttpResponseMessage(status)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(content)
            };
            return this;
        }

        public DownloadHandler Redirect(string url, string location)
        {
            responses[url] = request =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.Redirect) { RequestMessage = request };
                response.Headers.Location = new Uri(location);
                return response;
            };
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            Requests.Add(url);
            return Task.FromResult(responses.TryGetValue(url, out var response)
                ? response(request)
                : new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });
        }
    }
}
