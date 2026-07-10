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

using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
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
            currentExecutablePath: Path.Combine(tempRoot, "BlockHelm-Launcher.exe"),
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
            "https://example.test/BlockHelm_Launcher_x64.exe",
            null,
            "BlockHelm_Launcher_x64.exe",
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
        Assert.Contains(Path.Combine(tempRoot, "BlockHelm-Launcher.exe"), startedProcess.ArgumentList);
        Assert.Contains("--restart", startedProcess.ArgumentList);
    }

    [Fact]
    public async Task StartUpdateRetriesNextDownloadUrlWhenFirstFails()
    {
        Directory.CreateDirectory(tempRoot);
        var handler = new DownloadHandler();
        handler.Respond("https://example.test/primary.exe", HttpStatusCode.InternalServerError, string.Empty);
        handler.Respond("https://example.test/fallback.exe", HttpStatusCode.OK, "new launcher");
        var service = new LauncherSelfUpdateService(
            new HttpClient(handler),
            logger: null,
            baseDirectory: tempRoot,
            currentExecutablePath: Path.Combine(tempRoot, "BlockHelm-Launcher.exe"),
            currentProcessId: 1234,
            startProcess: _ => true);
        var update = new LauncherUpdateInfo(
            "1.0.1",
            "1.0.1",
            "https://example.test/release",
            "https://example.test/primary.exe",
            null,
            "BlockHelm_Launcher_x64.exe",
            LauncherUpdateAssetKind.WindowsX64Executable,
            DownloadUrls:
            [
                new LauncherUpdateDownloadUrl("primary", "https://example.test/primary.exe", 1),
                new LauncherUpdateDownloadUrl("fallback", "https://example.test/fallback.exe", 2)
            ]);

        var result = await service.StartUpdateAsync(update);

        Assert.True(result.Succeeded);
        Assert.Equal(
            ["https://example.test/primary.exe", "https://example.test/fallback.exe"],
            handler.Requests.Select(request => request.RequestUri!.ToString()));
    }

    [Fact]
    public async Task StartUpdateVerifiesSha256WhenManifestProvidesChecksum()
    {
        Directory.CreateDirectory(tempRoot);
        var payload = "new launcher";
        var handler = new DownloadHandler();
        handler.Respond("https://example.test/BlockHelm_Launcher_x64.exe", HttpStatusCode.OK, payload);
        var service = new LauncherSelfUpdateService(
            new HttpClient(handler),
            logger: null,
            baseDirectory: tempRoot,
            currentExecutablePath: Path.Combine(tempRoot, "BlockHelm-Launcher.exe"),
            currentProcessId: 1234,
            startProcess: _ => true);
        var update = new LauncherUpdateInfo(
            "1.0.1",
            "1.0.1",
            "https://example.test/release",
            "https://example.test/BlockHelm_Launcher_x64.exe",
            null,
            "BlockHelm_Launcher_x64.exe",
            LauncherUpdateAssetKind.WindowsX64Executable,
            Sha256: Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload))));

        var result = await service.StartUpdateAsync(update);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task StartUpdateFailsWhenSha256DoesNotMatch()
    {
        Directory.CreateDirectory(tempRoot);
        var service = new LauncherSelfUpdateService(
            new HttpClient(new DownloadHandler("new launcher")),
            logger: null,
            baseDirectory: tempRoot,
            currentExecutablePath: Path.Combine(tempRoot, "BlockHelm-Launcher.exe"),
            currentProcessId: 1234,
            startProcess: _ => true);
        var update = new LauncherUpdateInfo(
            "1.0.1",
            "1.0.1",
            "https://example.test/release",
            "https://example.test/BlockHelm_Launcher_x64.exe",
            null,
            "BlockHelm_Launcher_x64.exe",
            LauncherUpdateAssetKind.WindowsX64Executable,
            Sha256: new string('0', 64));

        var result = await service.StartUpdateAsync(update);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task StartUpdateFailsWhenSizeDoesNotMatch()
    {
        Directory.CreateDirectory(tempRoot);
        var service = new LauncherSelfUpdateService(
            new HttpClient(new DownloadHandler("new launcher")),
            logger: null,
            baseDirectory: tempRoot,
            currentExecutablePath: Path.Combine(tempRoot, "BlockHelm-Launcher.exe"),
            currentProcessId: 1234,
            startProcess: _ => true);
        var update = new LauncherUpdateInfo(
            "1.0.1",
            "1.0.1",
            "https://example.test/release",
            "https://example.test/BlockHelm_Launcher_x64.exe",
            null,
            "BlockHelm_Launcher_x64.exe",
            LauncherUpdateAssetKind.WindowsX64Executable,
            SizeBytes: 999);

        var result = await service.StartUpdateAsync(update);

        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData("ftp://example.test/BlockHelm_Launcher_x64.exe")]
    [InlineData("https://example.test/BlockHelm_Launcher_x64.exe.asc")]
    [InlineData("https://example.test/BlockHelm_Launcher_x64.zip")]
    public async Task StartUpdateRejectsInvalidDownloadUrl(string downloadUrl)
    {
        Directory.CreateDirectory(tempRoot);
        var service = new LauncherSelfUpdateService(
            new HttpClient(new DownloadHandler("new launcher")),
            logger: null,
            baseDirectory: tempRoot,
            currentExecutablePath: Path.Combine(tempRoot, "BlockHelm-Launcher.exe"),
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
            currentExecutablePath: Path.Combine(tempRoot, "BlockHelm-Launcher.exe"),
            currentProcessId: 1234,
            startProcess: _ => true);
        var update = new LauncherUpdateInfo(
            "1.0.1",
            "1.0.1",
            "https://example.test/release",
            "https://example.test/BlockHelm_Launcher_x64.exe",
            null,
            "BlockHelm_Launcher_x64.exe",
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
        private readonly Dictionary<string, (HttpStatusCode StatusCode, string Response)> responses = new(StringComparer.OrdinalIgnoreCase);
        private readonly string? defaultResponse;
        private readonly HttpStatusCode defaultStatusCode;

        public DownloadHandler(string response, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            defaultResponse = response;
            defaultStatusCode = statusCode;
        }

        public DownloadHandler()
        {
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        public void Respond(string url, HttpStatusCode statusCode, string response)
        {
            responses[url] = (statusCode, response);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (!responses.TryGetValue(request.RequestUri!.ToString(), out var response))
                response = (defaultStatusCode, defaultResponse ?? string.Empty);

            return Task.FromResult(new HttpResponseMessage(response.StatusCode)
            {
                Content = new StringContent(response.Response)
            });
        }
    }
}
