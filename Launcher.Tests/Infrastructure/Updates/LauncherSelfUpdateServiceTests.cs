using System.Diagnostics;
using System.Net;
using Launcher.Application.Services;
using Launcher.Infrastructure.Updates;

namespace Launcher.Tests.Infrastructure.Updates;

public sealed class LauncherSelfUpdateServiceTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), "launcher-self-update-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task StartUpdateDownloadsExecutableAndStartsDownloadedLauncherInUpdateMode()
    {
        Directory.CreateDirectory(tempRoot);
        ProcessStartInfo? startedProcess = null;
        var service = new LauncherSelfUpdateService(
            new HttpClient(new DownloadHandler("new launcher")),
            logger: null,
            baseDirectory: tempRoot,
            currentExecutablePath: Path.Combine(tempRoot, "MineDock Launcher.exe"),
            currentProcessId: 1234,
            startProcess: startInfo =>
            {
                startedProcess = startInfo;
                return true;
            });
        var update = new LauncherUpdateInfo(
            "1.0.1",
            "1.0.1",
            "https://example.test/release",
            "https://example.test/MineDock_Launcher_x64.exe",
            null,
            "MineDock_Launcher_x64.exe",
            LauncherUpdateAssetKind.WindowsX64Executable);

        var result = await service.StartUpdateAsync(update);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.DownloadedFilePath);
        Assert.True(File.Exists(result.DownloadedFilePath));
        Assert.Equal("new launcher", File.ReadAllText(result.DownloadedFilePath));
        Assert.NotNull(startedProcess);
        Assert.Equal(result.DownloadedFilePath, startedProcess.FileName);
        Assert.Contains("--apply-update", startedProcess.ArgumentList);
        Assert.Contains("--pid", startedProcess.ArgumentList);
        Assert.Contains("1234", startedProcess.ArgumentList);
        Assert.Contains("--source", startedProcess.ArgumentList);
        Assert.Contains(result.DownloadedFilePath, startedProcess.ArgumentList);
        Assert.Contains("--target", startedProcess.ArgumentList);
        Assert.Contains(Path.Combine(tempRoot, "MineDock Launcher.exe"), startedProcess.ArgumentList);
        Assert.Contains("--restart", startedProcess.ArgumentList);
    }

    [Theory]
    [InlineData("ftp://example.test/MineDock_Launcher_x64.exe")]
    [InlineData("https://example.test/MineDock_Launcher_x64.exe.asc")]
    [InlineData("https://example.test/MineDock_Launcher_x64.zip")]
    public async Task StartUpdateRejectsInvalidDownloadUrl(string downloadUrl)
    {
        Directory.CreateDirectory(tempRoot);
        var service = new LauncherSelfUpdateService(
            new HttpClient(new DownloadHandler("new launcher")),
            logger: null,
            baseDirectory: tempRoot,
            currentExecutablePath: Path.Combine(tempRoot, "MineDock Launcher.exe"),
            currentProcessId: 1234,
            startProcess: _ => true);
        var update = new LauncherUpdateInfo(
            "1.0.1",
            "1.0.1",
            "https://example.test/release",
            downloadUrl,
            null,
            Path.GetFileName(downloadUrl),
            LauncherUpdateAssetKind.WindowsX64Executable);

        var result = await service.StartUpdateAsync(update);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task StartUpdateFailsWhenDownloadedExecutableIsEmpty()
    {
        Directory.CreateDirectory(tempRoot);
        var service = new LauncherSelfUpdateService(
            new HttpClient(new DownloadHandler(string.Empty)),
            logger: null,
            baseDirectory: tempRoot,
            currentExecutablePath: Path.Combine(tempRoot, "MineDock Launcher.exe"),
            currentProcessId: 1234,
            startProcess: _ => true);
        var update = new LauncherUpdateInfo(
            "1.0.1",
            "1.0.1",
            "https://example.test/release",
            "https://example.test/MineDock_Launcher_x64.exe",
            null,
            "MineDock_Launcher_x64.exe",
            LauncherUpdateAssetKind.WindowsX64Executable);

        var result = await service.StartUpdateAsync(update);

        Assert.False(result.Succeeded);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, recursive: true);
    }

    private sealed class DownloadHandler : HttpMessageHandler
    {
        private readonly string response;
        private readonly HttpStatusCode statusCode;

        public DownloadHandler(string response, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            this.response = response;
            this.statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(response)
            });
        }
    }
}
