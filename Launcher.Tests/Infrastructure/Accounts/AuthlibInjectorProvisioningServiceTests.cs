/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Net;
using System.Security.Cryptography;
using System.Text;
using Launcher.Infrastructure.Accounts.ThirdParty;
using Launcher.Infrastructure.Minecraft;

namespace Launcher.Tests.Infrastructure.Accounts;

public sealed class AuthlibInjectorProvisioningServiceTests : TestTempDirectory
{
    [Fact]
    public async Task DownloadsVerifiesAndReusesLatestArtifact()
    {
        var bytes = Encoding.UTF8.GetBytes("verified-jar");
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var artifactRequests = 0;
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("latest.json", StringComparison.Ordinal))
                return Task.FromResult(Json(Metadata(hash)));
            artifactRequests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        });
        var service = new AuthlibInjectorProvisioningService(
            new HttpClient(handler),
            Path.Combine(TempRoot, "authlib"));

        var first = await service.EnsureAvailableAsync();
        var second = await service.EnsureAvailableAsync();

        Assert.Equal(first, second);
        Assert.Equal(55, first.BuildNumber);
        Assert.Equal("1.2.7", first.Version);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(first.FilePath));
        Assert.Equal(1, artifactRequests);
    }

    [Fact]
    public async Task UsesPreviouslyVerifiedCacheWhenLatestMetadataFails()
    {
        var bytes = Encoding.UTF8.GetBytes("verified-jar");
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var cache = Path.Combine(TempRoot, "authlib");
        var initial = new AuthlibInjectorProvisioningService(
            new HttpClient(new StubHttpMessageHandler(request => Task.FromResult(
                request.RequestUri!.AbsolutePath.EndsWith("latest.json", StringComparison.Ordinal)
                    ? Json(Metadata(hash))
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }))),
            cache);
        var downloaded = await initial.EnsureAvailableAsync();
        var offline = new AuthlibInjectorProvisioningService(
            new HttpClient(new StubHttpMessageHandler(_ => throw new HttpRequestException("offline"))),
            cache);

        var fallback = await offline.EnsureAvailableAsync();

        Assert.Equal(downloaded, fallback);
    }

    [Fact]
    public async Task RejectsChecksumMismatchWhenNoCacheExists()
    {
        var service = new AuthlibInjectorProvisioningService(
            new HttpClient(new StubHttpMessageHandler(request => Task.FromResult(
                request.RequestUri!.AbsolutePath.EndsWith("latest.json", StringComparison.Ordinal)
                    ? Json(Metadata(new string('0', 64)))
                    : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(Encoding.UTF8.GetBytes("tampered"))
                    }))),
            Path.Combine(TempRoot, "authlib"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnsureAvailableAsync());
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(TempRoot, "authlib"), "*.jar"));
    }

    [Fact]
    public async Task RejectsPrivateArtifactAddressBeforeArtifactRequest()
    {
        var artifactRequests = 0;
        var service = new AuthlibInjectorProvisioningService(
            new HttpClient(new StubHttpMessageHandler(request =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("latest.json", StringComparison.Ordinal))
                    return Task.FromResult(Json(Metadata(new string('0', 64))));
                artifactRequests++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            })),
            Path.Combine(TempRoot, "authlib"),
            logger: null,
            addressPolicy: new DownloadAddressPolicy((_, _) =>
                Task.FromResult(new[] { IPAddress.Parse("10.0.0.8") })));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnsureAvailableAsync());

        Assert.Equal(0, artifactRequests);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(TempRoot, "authlib"), "*.jar"));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"sha256\":null}")]
    public async Task RejectsNullChecksumMetadataBeforeArtifactRequest(string checksumsJson)
    {
        var artifactRequests = 0;
        var metadata =
            $"{{\"build_number\":55,\"version\":\"1.2.7\",\"download_url\":\"https://download.example.test/authlib-injector.jar\",\"checksums\":{checksumsJson}}}";
        var service = new AuthlibInjectorProvisioningService(
            new HttpClient(new StubHttpMessageHandler(request =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("latest.json", StringComparison.Ordinal))
                    return Task.FromResult(Json(metadata));
                artifactRequests++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            })),
            Path.Combine(TempRoot, "authlib"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnsureAvailableAsync());

        Assert.Equal(0, artifactRequests);
    }

    private static string Metadata(string hash) =>
        $"{{\"build_number\":55,\"version\":\"1.2.7\",\"download_url\":\"https://download.example.test/authlib-injector.jar\",\"checksums\":{{\"sha256\":\"{hash}\"}}}}";

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => handler(request);
    }
}
