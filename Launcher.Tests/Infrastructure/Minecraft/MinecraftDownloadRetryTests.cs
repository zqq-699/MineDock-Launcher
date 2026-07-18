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
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using CmlLib.Core.Files;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;
using Launcher.Infrastructure.Modpacks;
using Microsoft.Extensions.Logging;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class MinecraftDownloadRetryTests
{
    private const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    private const string BmclManifestUrl = "https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json";

    [Theory]
    [InlineData(DownloadSourcePreference.Official, ManifestUrl, BmclManifestUrl)]
    [InlineData(DownloadSourcePreference.BmclApi, BmclManifestUrl, ManifestUrl)]
    public void ManualPreferenceOrdersPreferredSourceBeforeFallback(
        DownloadSourcePreference preference,
        string expectedPrimaryUrl,
        string expectedFallbackUrl)
    {
        var requests = MinecraftDownloadSourceResolver
            .EnumerateRequests(ManifestUrl, preference, categoryHint: "Mojang")
            .ToArray();

        Assert.Equal(2, requests.Length);
        Assert.Equal(expectedPrimaryUrl, requests[0].ActualUrl);
        Assert.Equal(expectedFallbackUrl, requests[1].ActualUrl);
        Assert.All(requests, request => Assert.Equal(preference, request.RequestedSourcePreference));
    }

    [Theory]
    [InlineData("Mojang", ManifestUrl, BmclManifestUrl)]
    [InlineData(
        "Mojang",
        "https://piston-meta.mojang.com/v1/packages/abc/version.json",
        "https://bmclapi2.bangbang93.com/v1/packages/abc/version.json")]
    [InlineData(
        "Mojang",
        "https://piston-data.mojang.com/v1/objects/abc/client.jar",
        "https://bmclapi2.bangbang93.com/v1/objects/abc/client.jar")]
    [InlineData(
        "Mojang",
        "https://libraries.minecraft.net/com/example/library/1.0/library-1.0.jar",
        "https://bmclapi2.bangbang93.com/maven/com/example/library/1.0/library-1.0.jar")]
    [InlineData(
        "Mojang",
        "https://resources.download.minecraft.net/ab/abcdef",
        "https://bmclapi2.bangbang93.com/assets/ab/abcdef")]
    [InlineData(
        "Forge",
        "https://maven.minecraftforge.net/net/minecraftforge/forge/1.20.1-47.4.20/forge-1.20.1-47.4.20-installer.jar",
        "https://bmclapi2.bangbang93.com/maven/net/minecraftforge/forge/1.20.1-47.4.20/forge-1.20.1-47.4.20-installer.jar")]
    [InlineData(
        "Forge",
        "https://files.minecraftforge.net/net/minecraftforge/forge/index_1.20.1.html",
        "https://bmclapi2.bangbang93.com/forge/minecraft/1.20.1")]
    [InlineData(
        "Fabric",
        "https://meta.fabricmc.net/v2/versions/loader/1.20.1",
        "https://bmclapi2.bangbang93.com/fabric-meta/v2/versions/loader/1.20.1")]
    [InlineData(
        "Fabric",
        "https://maven.fabricmc.net/net/fabricmc/fabric-loader/0.16.14/fabric-loader-0.16.14.jar",
        "https://bmclapi2.bangbang93.com/maven/net/fabricmc/fabric-loader/0.16.14/fabric-loader-0.16.14.jar")]
    [InlineData(
        "NeoForge",
        "https://maven.neoforged.net/releases/net/neoforged/neoforge/21.1.234/neoforge-21.1.234-installer.jar",
        "https://bmclapi2.bangbang93.com/maven/net/neoforged/neoforge/21.1.234/neoforge-21.1.234-installer.jar")]
    public void CanonicalSourceMappingsRoundTrip(
        string categoryHint,
        string officialUrl,
        string bmclUrl)
    {
        var resolvedBmcl = MinecraftDownloadSourceResolver.ResolveRequest(
            officialUrl,
            DownloadSourcePreference.BmclApi,
            useBmclApi: true,
            categoryHint: categoryHint);
        var resolvedOfficial = MinecraftDownloadSourceResolver.ResolveRequest(
            bmclUrl,
            DownloadSourcePreference.Official,
            useBmclApi: false,
            categoryHint);

        Assert.Equal(bmclUrl, resolvedBmcl.ActualUrl);
        Assert.Equal(officialUrl, resolvedOfficial.ActualUrl);
    }

    [Theory]
    [InlineData(
        "https://files.minecraftforge.net/maven/net/minecraftforge/forge/1.12.2-14.23.5.2860/forge-1.12.2-14.23.5.2860-installer.jar",
        "Forge",
        "https://bmclapi2.bangbang93.com/maven/net/minecraftforge/forge/1.12.2-14.23.5.2860/forge-1.12.2-14.23.5.2860-installer.jar")]
    [InlineData(
        "http://files.minecraftforge.net/maven/net/minecraftforge/forge/1.7.10-10.13.4.1614/forge-1.7.10-10.13.4.1614-installer.jar",
        "Forge",
        "https://bmclapi2.bangbang93.com/maven/net/minecraftforge/forge/1.7.10-10.13.4.1614/forge-1.7.10-10.13.4.1614-installer.jar")]
    [InlineData(
        "https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/neoforge",
        "NeoForge",
        "https://bmclapi2.bangbang93.com/neoforge/meta/api/maven/details/releases/net/neoforged/neoforge")]
    public void LegacyAndMetadataMappingsUseCanonicalBmclPrefixes(
        string originalUrl,
        string categoryHint,
        string expectedBmclUrl)
    {
        var resolved = MinecraftDownloadSourceResolver.ResolveRequest(
            originalUrl,
            DownloadSourcePreference.BmclApi,
            useBmclApi: true,
            categoryHint: categoryHint);

        Assert.Equal(expectedBmclUrl, resolved.ActualUrl);
    }

    [Theory]
    [InlineData(
        "https://launcher.mojang.com/v1/objects/abc/client.jar",
        true,
        "Mojang",
        "https://bmclapi2.bangbang93.com/v1/objects/abc/client.jar")]
    [InlineData(
        "https://bmclapi2.bangbang93.com/v1/objects/abc/client.jar",
        false,
        "Mojang",
        "https://piston-data.mojang.com/v1/objects/abc/client.jar")]
    [InlineData(
        "https://bmclapi2.bangbang93.com/forge/minecraft/1.20.1",
        false,
        "Forge",
        "https://files.minecraftforge.net/net/minecraftforge/forge/index_1.20.1.html")]
    [InlineData(
        "https://bmclapi2.bangbang93.com/maven/net/neoforged/neoforge/21.1.234/neoforge-21.1.234-installer.jar",
        false,
        "NeoForge",
        "https://maven.neoforged.net/releases/net/neoforged/neoforge/21.1.234/neoforge-21.1.234-installer.jar")]
    public void KnownOfficialAndMirrorUrlsAreClassifiedWithoutHints(
        string originalUrl,
        bool useBmclApi,
        string expectedCategory,
        string expectedUrl)
    {
        var resolved = MinecraftDownloadSourceResolver.ResolveRequest(
            originalUrl,
            DownloadSourcePreference.Official,
            useBmclApi,
            categoryHint: null);

        Assert.Equal(expectedCategory, resolved.ResourceCategory);
        Assert.Equal(expectedUrl, resolved.ActualUrl);
    }

    [Theory]
    [InlineData("https://libraries.minecraft.net/com/example/library/1.0/library-1.0.jar", "Mojang")]
    [InlineData("https://meta.fabricmc.net/v2/versions/loader", "Fabric")]
    [InlineData("https://maven.minecraftforge.net/net/minecraftforge/forge/1.20.1/forge-1.20.1.jar", "Forge")]
    [InlineData("https://maven.neoforged.net/releases/net/neoforged/neoforge/20.4.1/neoforge-20.4.1.jar", "NeoForge")]
    public void MappedResourceCategoriesKeepOfficialFallback(string url, string categoryHint)
    {
        var requests = MinecraftDownloadSourceResolver
            .EnumerateRequests(url, DownloadSourcePreference.BmclApi, categoryHint)
            .ToArray();

        Assert.Equal(2, requests.Length);
        Assert.StartsWith("BmclApi", requests[0].ResolvedSourceKind, StringComparison.Ordinal);
        Assert.DoesNotContain("BmclApi", requests[1].ResolvedSourceKind, StringComparison.Ordinal);
        Assert.NotEqual(requests[0].ActualUrl, requests[1].ActualUrl);
    }

    [Theory]
    [InlineData(DownloadSourcePreference.Official)]
    [InlineData(DownloadSourcePreference.BmclApi)]
    public void ThirdPartyAddressProducesSingleCandidate(DownloadSourcePreference preference)
    {
        const string url = "https://downloads.example.test/files/archive.zip";

        var requests = MinecraftDownloadSourceResolver
            .EnumerateRequests(url, preference, categoryHint: "ThirdParty")
            .ToArray();

        var request = Assert.Single(requests);
        Assert.Equal(url, request.ActualUrl);
        Assert.Equal("ThirdParty", request.ResolvedSourceKind);
    }

    [Theory]
    [InlineData(DownloadSourcePreference.Official, "piston-meta.mojang.com", "bmclapi2.bangbang93.com")]
    [InlineData(DownloadSourcePreference.BmclApi, "bmclapi2.bangbang93.com", "piston-meta.mojang.com")]
    public async Task ManualPreferenceFallsBackAfterPreferredSourceFails(
        DownloadSourcePreference preference,
        string expectedPrimaryHost,
        string expectedFallbackHost)
    {
        var handler = new CallbackRequestHandler((requestNumber, request, _) =>
            Task.FromResult(CreateResponse(
                requestNumber == 1 ? HttpStatusCode.Forbidden : HttpStatusCode.OK,
                "{}",
                request)));
        using var httpClient = CreateClient(handler);
        var executor = CreateExecutor(httpClient);

        var result = await executor.ExecuteAsync(
            ManifestUrl,
            preference,
            categoryHint: "Mojang",
            async (context, token) => await context.Response.Content.ReadAsStringAsync(token),
            CancellationToken.None);

        Assert.Equal("{}", result);
        Assert.Equal([expectedPrimaryHost, expectedFallbackHost], handler.RequestUris.Select(uri => uri.Host));
    }

    [Fact]
    public async Task SuccessfulFallbackWritesOneRecoveryWarningWithoutException()
    {
        var logger = new CollectingLogger();
        var handler = new CallbackRequestHandler((requestNumber, request, _) =>
            Task.FromResult(CreateResponse(
                requestNumber == 1 ? HttpStatusCode.Forbidden : HttpStatusCode.OK,
                "{}",
                request)));
        using var httpClient = CreateClient(handler);
        var executor = CreateExecutor(httpClient, logger: logger);

        await executor.ExecuteAsync(
            ManifestUrl,
            DownloadSourcePreference.Official,
            categoryHint: "Mojang",
            async (context, token) => await context.Response.Content.ReadAsStringAsync(token),
            CancellationToken.None);

        var warning = Assert.Single(logger.Entries.Where(entry => entry.Level == LogLevel.Warning));
        Assert.Contains("Download recovered after retry or source fallback", warning.Message, StringComparison.Ordinal);
        Assert.Null(warning.Exception);
        Assert.DoesNotContain(logger.Entries, entry =>
            entry.Level == LogLevel.Information
            && entry.Message.Contains("Download transport completed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FirstAttemptSuccessWritesOnlyDiagnosticDownloadEvents()
    {
        var logger = new CollectingLogger();
        var handler = new CallbackRequestHandler((_, request, _) =>
            Task.FromResult(CreateResponse(HttpStatusCode.OK, "{}", request)));
        using var httpClient = CreateClient(handler);
        var executor = CreateExecutor(httpClient, logger: logger);

        await executor.ExecuteAsync(
            ManifestUrl,
            DownloadSourcePreference.Official,
            categoryHint: "Mojang",
            async (context, token) => await context.Response.Content.ReadAsStringAsync(token),
            CancellationToken.None);

        Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Information);
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Trace
            && entry.Message.Contains("Download transport completed", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(DownloadSourcePreference.Official)]
    [InlineData(DownloadSourcePreference.BmclApi)]
    public async Task ManualPreferenceLookupFallsBackAfterPreferredSourceReportsNoResult(
        DownloadSourcePreference preference)
    {
        var handler = new CallbackRequestHandler((requestNumber, request, _) =>
            Task.FromResult(CreateResponse(
                requestNumber == 1 ? HttpStatusCode.NotFound : HttpStatusCode.OK,
                "[]",
                request)));
        using var httpClient = CreateClient(handler);
        var executor = CreateExecutor(httpClient);

        var result = await executor.ExecuteLookupAsync(
            ManifestUrl,
            preference,
            categoryHint: "Mojang",
            static (_, _) => Task.FromResult<IReadOnlyList<string>>(["value"]),
            statusCode => statusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound,
            CancellationToken.None);

        Assert.True(result.Found);
        Assert.Equal(["value"], result.Value);
        Assert.Equal(2, handler.RequestUris.Count);
        Assert.NotEqual(handler.RequestUris[0], handler.RequestUris[1]);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task TransientStatusesConsumeFourAttemptsPerPreferredAndFallbackSource(HttpStatusCode statusCode)
    {
        var handler = new CallbackRequestHandler((_, request, _) =>
            Task.FromResult(CreateResponse(statusCode, string.Empty, request)));
        using var httpClient = CreateClient(handler);
        var executor = CreateExecutor(httpClient);

        await Assert.ThrowsAsync<MinecraftDownloadRequestExecutor.DownloadSourceRequestException>(
            () => executor.ExecuteAsync(
                ManifestUrl,
                DownloadSourcePreference.Official,
                categoryHint: "Mojang",
                static (_, _) => Task.FromResult(true),
                CancellationToken.None));

        Assert.Equal(8, handler.RequestUris.Count);
        Assert.All(handler.RequestUris.Take(4), uri => Assert.Equal(ManifestUrl, uri.AbsoluteUri));
        Assert.All(handler.RequestUris.Skip(4), uri => Assert.Equal(BmclManifestUrl, uri.AbsoluteUri));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.MethodNotAllowed)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task PermanentStatusesSwitchSourceWithoutRetryingEitherSource(HttpStatusCode statusCode)
    {
        var handler = new CallbackRequestHandler((_, request, _) =>
            Task.FromResult(CreateResponse(statusCode, string.Empty, request)));
        using var httpClient = CreateClient(handler);
        var executor = CreateExecutor(httpClient);

        await Assert.ThrowsAsync<MinecraftDownloadRequestExecutor.DownloadSourceRequestException>(
            () => executor.ExecuteAsync(
                ManifestUrl,
                DownloadSourcePreference.Official,
                categoryHint: "Mojang",
                static (_, _) => Task.FromResult(true),
                CancellationToken.None));

        Assert.Equal([ManifestUrl, BmclManifestUrl], handler.RequestUris.Select(uri => uri.AbsoluteUri));
    }

    [Fact]
    public async Task TransientStatusRetriesFourTimesThenSwitchesSource()
    {
        var handler = new CallbackRequestHandler((requestNumber, request, _) =>
            Task.FromResult(CreateResponse(
                requestNumber <= 4 ? HttpStatusCode.InternalServerError : HttpStatusCode.OK,
                "{}",
                request)));
        using var httpClient = CreateClient(handler);
        var executor = CreateExecutor(httpClient);

        var result = await executor.ExecuteAsync(
            ManifestUrl,
            LauncherDefaults.DefaultDownloadSourcePreference,
            categoryHint: "Mojang",
            async (context, token) => await context.Response.Content.ReadAsStringAsync(token),
            CancellationToken.None);

        Assert.Equal("{}", result);
        Assert.Equal(5, handler.RequestUris.Count);
        Assert.All(handler.RequestUris.Take(4), uri => Assert.Equal(handler.RequestUris[0], uri));
        Assert.NotEqual(handler.RequestUris[0], handler.RequestUris[4]);
    }

    [Theory]
    [InlineData(1, 5000, false)]
    [InlineData(1, 5001, true)]
    [InlineData(6144, 6000, false)]
    [InlineData(6143, 6000, true)]
    public void SlowBodyThresholdMatchesPclReadRule(int bytesRead, int elapsedMilliseconds, bool expected)
    {
        Assert.Equal(
            expected,
            DownloadResponseThrottler.IsBodyReadTooSlow(
                bytesRead,
                TimeSpan.FromMilliseconds(elapsedMilliseconds),
                TimeSpan.FromSeconds(5),
                1024));
    }

    [Theory]
    [InlineData(DownloadSourcePreference.Official, "piston-meta.mojang.com", "bmclapi2.bangbang93.com")]
    [InlineData(DownloadSourcePreference.BmclApi, "bmclapi2.bangbang93.com", "piston-meta.mojang.com")]
    public async Task SlowBodyRetriesPreferredSourceOnceThenSwitchesFallback(
        DownloadSourcePreference preference,
        string expectedPrimaryHost,
        string expectedFallbackHost)
    {
        var clock = new ManualTimeProvider();
        var handler = new CallbackRequestHandler((requestNumber, request, _) =>
            Task.FromResult(requestNumber <= 2
                ? CreateSlowResponse(request, clock)
                : CreateResponse(HttpStatusCode.OK, "{}", request)));
        using var httpClient = CreateClient(handler);
        var executor = CreateExecutor(httpClient, CreateSlowBodyOptions(), timeProvider: clock);

        var result = await executor.ExecuteAsync(
            ManifestUrl,
            preference,
            categoryHint: "Mojang",
            async (context, token) => await context.Response.Content.ReadAsStringAsync(token),
            CancellationToken.None);

        Assert.Equal("{}", result);
        Assert.Equal(
            [expectedPrimaryHost, expectedPrimaryHost, expectedFallbackHost],
            handler.RequestUris.Select(uri => uri.Host));
    }

    [Fact]
    public async Task AutoPreferenceRetriesCurrentSourceOnceThenSwitchesFallback()
    {
        var clock = new ManualTimeProvider();
        var handler = new CallbackRequestHandler((requestNumber, request, _) =>
            Task.FromResult(requestNumber <= 2
                ? CreateSlowResponse(request, clock)
                : CreateResponse(HttpStatusCode.OK, "{}", request)));
        using var httpClient = CreateClient(handler);
        var executor = CreateExecutor(httpClient, CreateSlowBodyOptions(), timeProvider: clock);

        var result = await executor.ExecuteAsync(
            ManifestUrl,
            LauncherDefaults.DefaultDownloadSourcePreference,
            categoryHint: "Mojang",
            async (context, token) => await context.Response.Content.ReadAsStringAsync(token),
            CancellationToken.None);

        Assert.Equal("{}", result);
        Assert.Equal(handler.RequestUris[0], handler.RequestUris[1]);
        Assert.NotEqual(handler.RequestUris[1], handler.RequestUris[2]);
    }

    [Fact]
    public async Task FinalCandidateDisablesSlowWatchdogForLastFallbackAttempt()
    {
        var clock = new ManualTimeProvider();
        var handler = new CallbackRequestHandler((_, request, _) =>
            Task.FromResult(CreateSlowResponse(request, clock)));
        using var httpClient = CreateClient(handler);
        var executor = CreateExecutor(httpClient, CreateSlowBodyOptions(), timeProvider: clock);

        var result = await executor.ExecuteAsync(
            "https://downloads.example.test/files/archive.json",
            LauncherDefaults.DefaultDownloadSourcePreference,
            categoryHint: "ThirdParty",
            async (context, token) => await context.Response.Content.ReadAsStringAsync(token),
            CancellationToken.None);

        Assert.Equal("{}", result);
        Assert.Equal(3, handler.RequestUris.Count);
        Assert.All(handler.RequestUris, uri => Assert.Equal("downloads.example.test", uri.Host));
    }

    [Fact]
    public async Task SlowKnownFinalChunkCompletesWithoutDisconnecting()
    {
        var clock = new ManualTimeProvider();
        var handler = new CallbackRequestHandler((_, request, _) =>
        {
            var response = CreateSlowResponse(request, clock);
            response.Content.Headers.ContentLength = 2;
            return Task.FromResult(response);
        });
        using var httpClient = CreateClient(handler);
        var executor = CreateExecutor(httpClient, CreateSlowBodyOptions(), timeProvider: clock);

        var result = await executor.ExecuteAsync(
            ManifestUrl,
            DownloadSourcePreference.Official,
            categoryHint: "Mojang",
            async (context, token) => await context.Response.Content.ReadAsStringAsync(token),
            CancellationToken.None);

        Assert.Equal("{}", result);
        Assert.Single(handler.RequestUris);
    }

    [Fact]
    public async Task PermanentStatusSwitchesSourceWithoutRetry()
    {
        var handler = new CallbackRequestHandler((requestNumber, request, _) =>
            Task.FromResult(CreateResponse(
                requestNumber == 1 ? HttpStatusCode.Forbidden : HttpStatusCode.OK,
                "{}",
                request)));
        using var httpClient = CreateClient(handler);
        var executor = CreateExecutor(httpClient);

        await executor.ExecuteAsync(
            ManifestUrl,
            LauncherDefaults.DefaultDownloadSourcePreference,
            categoryHint: "Mojang",
            static (_, _) => Task.FromResult(true),
            CancellationToken.None);

        Assert.Equal(2, handler.RequestUris.Count);
        Assert.NotEqual(handler.RequestUris[0], handler.RequestUris[1]);
    }

    [Fact]
    public async Task LoaderLookupDoesNotReturnEmptyWhenAnotherSourceFails()
    {
        var handler = new CallbackRequestHandler((requestNumber, request, _) =>
            Task.FromResult(CreateResponse(
                requestNumber == 1 ? HttpStatusCode.NotFound : HttpStatusCode.InternalServerError,
                "[]",
                request)));
        using var httpClient = CreateClient(handler);
        var executor = CreateExecutor(httpClient);

        await Assert.ThrowsAsync<MinecraftDownloadRequestExecutor.DownloadSourceRequestException>(
            () => executor.ExecuteLookupAsync(
                ManifestUrl,
                LauncherDefaults.DefaultDownloadSourcePreference,
                categoryHint: "Mojang",
                static (_, _) => Task.FromResult<IReadOnlyList<string>>(["value"]),
                statusCode => statusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound,
                CancellationToken.None));

        Assert.Equal(5, handler.RequestUris.Count);
    }

    [Fact]
    public async Task LoaderLookupReturnsEmptyOnlyWhenEverySourceReportsNoResult()
    {
        var handler = new CallbackRequestHandler((_, request, _) =>
            Task.FromResult(CreateResponse(HttpStatusCode.NotFound, "[]", request)));
        using var httpClient = CreateClient(handler);
        var executor = CreateExecutor(httpClient);

        var result = await executor.ExecuteLookupAsync(
            ManifestUrl,
            LauncherDefaults.DefaultDownloadSourcePreference,
            categoryHint: "Mojang",
            static (_, _) => Task.FromResult<IReadOnlyList<string>>(["value"]),
            statusCode => statusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound,
            CancellationToken.None);

        Assert.False(result.Found);
        Assert.Equal(2, handler.RequestUris.Count);
    }

    [Fact]
    public async Task LoaderLookupDoesNotReturnEmptyAfterAnotherSourceHasInvalidContent()
    {
        var handler = new CallbackRequestHandler((requestNumber, request, _) =>
            Task.FromResult(CreateResponse(
                requestNumber == 1 ? HttpStatusCode.OK : HttpStatusCode.NotFound,
                requestNumber == 1 ? "<html>error</html>" : "[]",
                request)));
        using var httpClient = CreateClient(handler);
        var executor = CreateExecutor(httpClient);

        await Assert.ThrowsAsync<MinecraftDownloadRequestExecutor.DownloadSourceRequestException>(
            () => executor.ExecuteLookupAsync(
                ManifestUrl,
                LauncherDefaults.DefaultDownloadSourcePreference,
                categoryHint: "Mojang",
                async (context, token) =>
                {
                    await using var stream = await context.Response.Content.ReadAsStreamAsync(token);
                    using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
                    return (IReadOnlyList<string>)["value"];
                },
                statusCode => statusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound,
                CancellationToken.None));

        Assert.Equal(2, handler.RequestUris.Count);
    }

    [Theory]
    [InlineData(DownloadSourcePreference.Official)]
    [InlineData(DownloadSourcePreference.BmclApi)]
    public async Task InvalidJsonSwitchesFromPreferredToFallbackWithoutRetry(
        DownloadSourcePreference preference)
    {
        var handler = new CallbackRequestHandler((requestNumber, request, _) =>
            Task.FromResult(CreateResponse(
                HttpStatusCode.OK,
                requestNumber == 1 ? "<html>error</html>" : "{\"versions\":[]}",
                request)));
        using var httpClient = CreateClient(handler);
        var executor = CreateExecutor(httpClient);

        await executor.ExecuteAsync(
            ManifestUrl,
            preference,
            categoryHint: "Mojang",
            async (context, token) =>
            {
                await using var stream = await context.Response.Content.ReadAsStreamAsync(token);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
                return document.RootElement.GetProperty("versions").GetArrayLength();
            },
            CancellationToken.None);

        Assert.Equal(2, handler.RequestUris.Count);
        Assert.NotEqual(handler.RequestUris[0], handler.RequestUris[1]);
    }

    [Fact]
    public async Task InterruptedBodySwitchesSourceAndReplacesOnlyCompleteFile()
    {
        var handler = new CallbackRequestHandler((requestNumber, request, _) =>
        {
            HttpContent content = requestNumber == 1
                ? new StreamContent(new FaultingReadStream("partial"u8.ToArray()))
                : new ByteArrayContent("complete"u8.ToArray());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = content
            });
        });
        using var httpClient = CreateClient(handler);
        var executor = CreateExecutor(httpClient);
        var directory = CreateTempDirectory();
        var destination = Path.Combine(directory, "client.jar");

        try
        {
            await executor.DownloadFileAsync(
                ManifestUrl,
                DownloadSourcePreference.Official,
                categoryHint: "Mojang",
                destination,
                expectedSha1: null,
                expectedSize: 8,
                CancellationToken.None);

            Assert.Equal("complete", await File.ReadAllTextAsync(destination));
            Assert.Equal(2, handler.RequestUris.Count);
            Assert.NotEqual(handler.RequestUris[0], handler.RequestUris[1]);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task HashMismatchSwitchesSourceWithoutRetry()
    {
        var handler = new CallbackRequestHandler((requestNumber, request, _) =>
            Task.FromResult(CreateResponse(
                HttpStatusCode.OK,
                requestNumber == 1 ? "baad" : "good",
                request)));
        using var httpClient = CreateClient(handler);
        var executor = CreateExecutor(httpClient);
        var directory = CreateTempDirectory();
        var destination = Path.Combine(directory, "library.jar");
        var expectedSha1 = Convert.ToHexString(SHA1.HashData("good"u8.ToArray()));

        try
        {
            await executor.DownloadFileAsync(
                ManifestUrl,
                LauncherDefaults.DefaultDownloadSourcePreference,
                categoryHint: "Mojang",
                destination,
                expectedSha1,
                expectedSize: 4,
                CancellationToken.None);

            Assert.Equal("good", await File.ReadAllTextAsync(destination));
            Assert.Equal(2, handler.RequestUris.Count);
            Assert.NotEqual(handler.RequestUris[0], handler.RequestUris[1]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task HashMismatchRetiresFileSourcesAfterFirstRound()
    {
        var handler = new CallbackRequestHandler((_, request, _) =>
            Task.FromResult(CreateResponse(HttpStatusCode.OK, "baad", request)));
        using var httpClient = CreateClient(handler);
        var executor = CreateExecutor(httpClient);
        var directory = CreateTempDirectory();
        var destination = Path.Combine(directory, "library.jar");
        var expectedSha1 = Convert.ToHexString(SHA1.HashData("good"u8.ToArray()));

        try
        {
            await Assert.ThrowsAsync<MinecraftDownloadRequestExecutor.DownloadSourceRequestException>(
                () => executor.DownloadFileAsync(
                    ManifestUrl,
                    DownloadSourcePreference.Official,
                    categoryHint: "Mojang",
                    destination,
                    expectedSha1,
                    expectedSize: 4,
                    CancellationToken.None));

            Assert.Equal([ManifestUrl, BmclManifestUrl], handler.RequestUris.Select(uri => uri.AbsoluteUri));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task FileDownloadRotatesSourcesBeforeStartingRecoveryRound()
    {
        var handler = new CallbackRequestHandler((requestNumber, request, _) =>
            Task.FromResult(CreateResponse(
                requestNumber <= 2 ? HttpStatusCode.InternalServerError : HttpStatusCode.OK,
                requestNumber <= 2 ? string.Empty : "payload",
                request)));
        using var httpClient = CreateClient(handler);
        var executor = CreateExecutor(httpClient);
        var directory = CreateTempDirectory();
        var destination = Path.Combine(directory, "client.jar");

        try
        {
            await executor.DownloadFileAsync(
                ManifestUrl,
                DownloadSourcePreference.Official,
                categoryHint: "Mojang",
                destination,
                expectedSha1: null,
                expectedSize: 7,
                CancellationToken.None);

            Assert.Equal("payload", await File.ReadAllTextAsync(destination));
            Assert.Equal(
                [ManifestUrl, BmclManifestUrl, ManifestUrl],
                handler.RequestUris.Select(uri => uri.AbsoluteUri));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task FileDownloadStopsAfterThreeSourceRounds()
    {
        var handler = new CallbackRequestHandler((_, request, _) =>
            Task.FromResult(CreateResponse(HttpStatusCode.InternalServerError, string.Empty, request)));
        using var httpClient = CreateClient(handler);
        var executor = CreateExecutor(httpClient);
        var directory = CreateTempDirectory();

        try
        {
            await Assert.ThrowsAsync<MinecraftDownloadRequestExecutor.DownloadSourceRequestException>(
                () => executor.DownloadFileAsync(
                    ManifestUrl,
                    DownloadSourcePreference.Official,
                    categoryHint: "Mojang",
                    Path.Combine(directory, "client.jar"),
                    expectedSha1: null,
                    expectedSize: null,
                    CancellationToken.None));

            Assert.Equal(
                [ManifestUrl, BmclManifestUrl, ManifestUrl, BmclManifestUrl, ManifestUrl, BmclManifestUrl],
                handler.RequestUris.Select(uri => uri.AbsoluteUri));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SingleSourceFileDownloadStopsAfterThreeRounds()
    {
        const string url = "https://example.test/files/client.jar";
        var handler = new CallbackRequestHandler((_, request, _) =>
            Task.FromResult(CreateResponse(HttpStatusCode.InternalServerError, string.Empty, request)));
        using var httpClient = CreateClient(handler);
        var executor = CreateExecutor(httpClient);
        var directory = CreateTempDirectory();

        try
        {
            await Assert.ThrowsAsync<MinecraftDownloadRequestExecutor.DownloadSourceRequestException>(
                () => executor.DownloadFileAsync(
                    url,
                    DownloadSourcePreference.Official,
                    categoryHint: "ThirdParty",
                    Path.Combine(directory, "client.jar"),
                    expectedSha1: null,
                    expectedSize: null,
                    CancellationToken.None));

            Assert.Equal([url, url, url], handler.RequestUris.Select(uri => uri.AbsoluteUri));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PermanentFileSourceFailureIsExcludedFromRecoveryRounds()
    {
        var handler = new CallbackRequestHandler((_, request, _) =>
            Task.FromResult(CreateResponse(
                request.RequestUri!.Host == "piston-meta.mojang.com"
                    ? HttpStatusCode.NotFound
                    : HttpStatusCode.InternalServerError,
                string.Empty,
                request)));
        using var httpClient = CreateClient(handler);
        var executor = CreateExecutor(httpClient);
        var directory = CreateTempDirectory();

        try
        {
            await Assert.ThrowsAsync<MinecraftDownloadRequestExecutor.DownloadSourceRequestException>(
                () => executor.DownloadFileAsync(
                    ManifestUrl,
                    DownloadSourcePreference.Official,
                    categoryHint: "Mojang",
                    Path.Combine(directory, "client.jar"),
                    expectedSha1: null,
                    expectedSize: null,
                    CancellationToken.None));

            Assert.Equal(
                ["piston-meta.mojang.com", "bmclapi2.bangbang93.com", "bmclapi2.bangbang93.com", "bmclapi2.bangbang93.com"],
                handler.RequestUris.Select(uri => uri.Host));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task TemporarilyAvoidedFileSourceReturnsInRecoveryRound()
    {
        var official = MinecraftDownloadSourceResolver
            .EnumerateRequests(ManifestUrl, DownloadSourcePreference.Official, "Mojang")
            .First();
        var tracker = new DownloadHostHealthTracker();
        for (var failure = 0; failure < 3; failure++)
        {
            tracker.RecordFailure(
                official.ResolvedSourceKind,
                new Uri(official.ActualUrl).Host,
                DownloadFailureReason.Network);
        }

        var handler = new CallbackRequestHandler((requestNumber, request, _) =>
            Task.FromResult(CreateResponse(
                requestNumber == 1 ? HttpStatusCode.InternalServerError : HttpStatusCode.OK,
                requestNumber == 1 ? string.Empty : "payload",
                request)));
        using var httpClient = CreateClient(handler);
        var executor = CreateExecutor(httpClient, hostHealthTracker: tracker);
        var directory = CreateTempDirectory();
        var destination = Path.Combine(directory, "client.jar");

        try
        {
            await executor.DownloadFileAsync(
                ManifestUrl,
                DownloadSourcePreference.Official,
                categoryHint: "Mojang",
                destination,
                expectedSha1: null,
                expectedSize: 7,
                CancellationToken.None);

            Assert.Equal(
                ["bmclapi2.bangbang93.com", "piston-meta.mojang.com"],
                handler.RequestUris.Select(uri => uri.Host));
            Assert.Equal("payload", await File.ReadAllTextAsync(destination));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task FileRoundBackoffUsesRetryAfterAndCancellationStopsImmediately()
    {
        var handler = new CallbackRequestHandler((requestNumber, request, _) =>
        {
            var response = CreateResponse(
                requestNumber == 1 ? HttpStatusCode.TooManyRequests : HttpStatusCode.InternalServerError,
                string.Empty,
                request);
            if (requestNumber == 1)
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(10));
            return Task.FromResult(response);
        });
        using var httpClient = CreateClient(handler);
        var executor = CreateExecutor(httpClient);
        var directory = CreateTempDirectory();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => executor.DownloadFileAsync(
                    ManifestUrl,
                    DownloadSourcePreference.Official,
                    categoryHint: "Mojang",
                    Path.Combine(directory, "client.jar"),
                    expectedSha1: null,
                    expectedSize: null,
                    cancellation.Token));

            Assert.Equal([ManifestUrl, BmclManifestUrl], handler.RequestUris.Select(uri => uri.AbsoluteUri));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task BodyIdleTimeoutRetriesWithinSameSourceBudget()
    {
        var handler = new CallbackRequestHandler((_, request, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StreamContent(new BlockingReadStream())
            }));
        using var httpClient = CreateClient(handler);
        var executor = CreateExecutor(httpClient, new DownloadRetryOptions
        {
            MaxAttemptsPerSource = 2,
            RetryDelay = TimeSpan.Zero,
            ResponseHeadersTimeout = TimeSpan.FromSeconds(1),
            BodyIdleTimeout = TimeSpan.FromMilliseconds(20),
            MaxRedirects = 10
        });

        await Assert.ThrowsAsync<MinecraftDownloadRequestExecutor.DownloadSourceRequestException>(
            () => executor.ExecuteAsync(
                ManifestUrl,
                DownloadSourcePreference.Official,
                categoryHint: "Mojang",
                async (context, token) => await context.Response.Content.ReadAsByteArrayAsync(token),
                CancellationToken.None));

        Assert.Equal(4, handler.RequestUris.Count);
        Assert.Equal(handler.RequestUris[0], handler.RequestUris[1]);
        Assert.Equal(handler.RequestUris[2], handler.RequestUris[3]);
        Assert.NotEqual(handler.RequestUris[0], handler.RequestUris[2]);
    }

    [Fact]
    public async Task ResponseHeadersTimeoutRetriesWithinSameSourceBudget()
    {
        var handler = new CallbackRequestHandler(async (_, _, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("Unreachable");
        });
        using var httpClient = CreateClient(handler);
        var executor = CreateExecutor(httpClient, new DownloadRetryOptions
        {
            MaxAttemptsPerSource = 2,
            RetryDelay = TimeSpan.Zero,
            ResponseHeadersTimeout = TimeSpan.FromMilliseconds(20),
            BodyIdleTimeout = TimeSpan.FromSeconds(1),
            MaxRedirects = 10
        });

        await Assert.ThrowsAsync<MinecraftDownloadRequestExecutor.DownloadSourceRequestException>(
            () => executor.ExecuteAsync(
                ManifestUrl,
                DownloadSourcePreference.Official,
                categoryHint: "Mojang",
                static (_, _) => Task.FromResult(true),
                CancellationToken.None));

        Assert.Equal(4, handler.RequestUris.Count);
        Assert.Equal(handler.RequestUris[0], handler.RequestUris[1]);
        Assert.Equal(handler.RequestUris[2], handler.RequestUris[3]);
        Assert.NotEqual(handler.RequestUris[0], handler.RequestUris[2]);
    }

    [Fact]
    public async Task UserCancellationDuringRetryDelayStopsImmediately()
    {
        var handler = new CallbackRequestHandler((_, request, _) =>
            Task.FromResult(CreateResponse(HttpStatusCode.InternalServerError, string.Empty, request)));
        using var httpClient = CreateClient(handler);
        var executor = CreateExecutor(httpClient, new DownloadRetryOptions
        {
            MaxAttemptsPerSource = 4,
            RetryDelay = TimeSpan.FromSeconds(10),
            ResponseHeadersTimeout = TimeSpan.FromSeconds(1),
            BodyIdleTimeout = TimeSpan.FromSeconds(1),
            MaxRedirects = 10
        });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => executor.ExecuteAsync(
                ManifestUrl,
                DownloadSourcePreference.Official,
                categoryHint: "Mojang",
                static (_, _) => Task.FromResult(true),
                cancellation.Token));

        Assert.Single(handler.RequestUris);
    }

    [Fact]
    public async Task BandwidthDelayDoesNotTriggerNetworkIdleTimeout()
    {
        var handler = new CallbackRequestHandler((_, request, _) =>
            Task.FromResult(CreateResponse(HttpStatusCode.OK, "payload", request)));
        using var httpClient = CreateClient(handler);
        var limiter = DownloadBandwidthLimiter.Create(37)!;
        await limiter.ThrottleAsync(37 * 1024 * 1024, CancellationToken.None);
        var executor = new MinecraftDownloadRequestExecutor(
            httpClient,
            bandwidthLimiter: limiter,
            limiter: new ImportConcurrencyLimiter(),
            retryOptions: new DownloadRetryOptions
            {
                MaxAttemptsPerSource = 1,
                RetryDelay = TimeSpan.Zero,
                ResponseHeadersTimeout = TimeSpan.FromSeconds(1),
                BodyIdleTimeout = TimeSpan.FromMilliseconds(20),
                MaxRedirects = 10
            });

        var result = await executor.ExecuteAsync(
            ManifestUrl,
            DownloadSourcePreference.Official,
            categoryHint: "Mojang",
            async (context, token) => await context.Response.Content.ReadAsStringAsync(token),
            CancellationToken.None);

        Assert.Equal("payload", result);
        Assert.Single(handler.RequestUris);
    }

    [Fact]
    public async Task ContentTypeIsNotUsedAsAValidityGate()
    {
        var handler = new CallbackRequestHandler((_, request, _) =>
        {
            var response = CreateResponse(HttpStatusCode.OK, "{\"versions\":[]}", request);
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            return Task.FromResult(response);
        });
        using var httpClient = CreateClient(handler);
        var executor = CreateExecutor(httpClient);

        var count = await executor.ExecuteAsync(
            ManifestUrl,
            DownloadSourcePreference.Official,
            categoryHint: "Mojang",
            async (context, token) =>
            {
                await using var stream = await context.Response.Content.ReadAsStreamAsync(token);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
                return document.RootElement.GetProperty("versions").GetArrayLength();
            },
            CancellationToken.None);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ValidRedirectStaysInsideOneTopLevelAttempt()
    {
        var handler = new CallbackRequestHandler((requestNumber, request, _) =>
        {
            if (requestNumber == 1)
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Redirect)
                {
                    RequestMessage = request
                };
                redirect.Headers.Location = new Uri("/redirected", UriKind.Relative);
                return Task.FromResult(redirect);
            }

            return Task.FromResult(CreateResponse(HttpStatusCode.OK, "{}", request));
        });
        using var httpClient = CreateClient(handler);
        var executor = CreateExecutor(httpClient, new DownloadRetryOptions
        {
            MaxAttemptsPerSource = 1,
            RetryDelay = TimeSpan.Zero,
            ResponseHeadersTimeout = TimeSpan.FromSeconds(1),
            BodyIdleTimeout = TimeSpan.FromSeconds(1),
            MaxRedirects = 10
        });

        var attempt = await executor.ExecuteAsync(
            ManifestUrl,
            DownloadSourcePreference.Official,
            categoryHint: "Mojang",
            static (context, _) => Task.FromResult(context.AttemptNumber),
            CancellationToken.None);

        Assert.Equal(1, attempt);
        Assert.Equal(2, handler.RequestUris.Count);
    }

    [Fact]
    public async Task RedirectLoopSwitchesSourceWithoutRetry()
    {
        var handler = new CallbackRequestHandler((_, request, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                RequestMessage = request
            };
            response.Headers.Location = request.RequestUri;
            return Task.FromResult(response);
        });
        using var httpClient = CreateClient(handler);
        var executor = CreateExecutor(httpClient);

        await Assert.ThrowsAsync<MinecraftDownloadRequestExecutor.DownloadSourceRequestException>(
            () => executor.ExecuteAsync(
                ManifestUrl,
                LauncherDefaults.DefaultDownloadSourcePreference,
                categoryHint: "Mojang",
                static (_, _) => Task.FromResult(true),
                CancellationToken.None));

        Assert.Equal(2, handler.RequestUris.Count);
    }

    [Fact]
    public async Task CmlLibGameInstallerSwitchesSourceAfterBodyFailure()
    {
        var handler = new CallbackRequestHandler((requestNumber, request, _) =>
        {
            HttpContent content = requestNumber == 1
                ? new StreamContent(new FaultingReadStream("part"u8.ToArray()))
                : new ByteArrayContent("complete"u8.ToArray());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = content
            });
        });
        using var runtimeClient = CreateClient(handler);
        using var metadataClient = CreateClient(new CallbackRequestHandler((_, request, _) =>
            Task.FromResult(CreateResponse(HttpStatusCode.OK, "{}", request))));
        var executor = CreateExecutor(runtimeClient);
        var installer = DownloadSpeedTrackingGameInstaller.CreateAsCoreCount(
            metadataClient,
            executor,
            DownloadSourcePreference.Official,
            progress: null);
        var directory = CreateTempDirectory();
        var destination = Path.Combine(directory, "game-file.jar");
        var file = new GameFile("game-file.jar")
        {
            Url = ManifestUrl,
            Path = destination,
            Size = 8,
            Hash = Convert.ToHexString(SHA1.HashData("complete"u8.ToArray()))
        };

        try
        {
            await installer.DownloadGameFileAsync(file, progress: null, CancellationToken.None);

            Assert.Equal("complete", await File.ReadAllTextAsync(destination));
            Assert.Equal(2, handler.RequestUris.Count);
            Assert.NotEqual(handler.RequestUris[0].Host, handler.RequestUris[1].Host);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CmlHashedLibraryPublishesFromTheDestinationDirectoryWithoutCreatingAWorkspace()
    {
        var payload = "library-data"u8.ToArray();
        var handler = new CallbackRequestHandler((_, request, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(payload)
            }));
        using var runtimeClient = CreateClient(handler);
        using var metadataClient = CreateClient(new CallbackRequestHandler((_, request, _) =>
            Task.FromResult(CreateResponse(HttpStatusCode.OK, "{}", request))));
        var executor = CreateExecutor(runtimeClient);
        var directory = CreateTempDirectory();
        var path = new CmlLib.Core.MinecraftPath(directory);
        var destination = Path.Combine(path.Library, "com", "example", "library", "1.0", "library-1.0.jar");
        var operation = new MinecraftDownloadOperationContext(directory);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllTextAsync(
            Path.Combine(Path.GetDirectoryName(destination)!, ".library-1.0.jar.bhl-pending-stale.tmp"),
            "interrupted");

        try
        {
            var installer = DownloadSpeedTrackingGameInstaller.CreateAsCoreCount(
                metadataClient,
                executor,
                DownloadSourcePreference.Official,
                progress: null,
                minecraftPath: path,
                operationContext: operation);
            await installer.DownloadGameFileAsync(
                new GameFile("library-1.0.jar")
                {
                    Url = ManifestUrl,
                    Path = destination,
                    Size = payload.Length,
                    Hash = Convert.ToHexString(SHA1.HashData(payload))
                },
                progress: null,
                CancellationToken.None);

            Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
            Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(destination)!, ".bhl-download-work")));
            Assert.Empty(Directory.EnumerateFiles(
                Path.GetDirectoryName(destination)!,
                ".library-1.0.jar.bhl-pending-*.tmp",
                SearchOption.TopDirectoryOnly));

            operation.Dispose();
        }
        finally
        {
            operation.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task FileDownloadSwitchesSourceAndRestartsAfterInterruptedBody()
    {
        var handler = new CallbackRequestHandler((requestNumber, request, _) =>
        {
            if (requestNumber == 1)
            {
                var first = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = request,
                    Content = new StreamContent(new FaultingReadStream("part"u8.ToArray()))
                };
                first.Headers.ETag = new EntityTagHeaderValue("\"stable\"");
                first.Content.Headers.ContentLength = 8;
                return Task.FromResult(first);
            }

            Assert.Null(request.Headers.Range);
            Assert.False(request.Headers.Contains("If-Range"));
            var fallback = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent("partdata"u8.ToArray())
            };
            fallback.Headers.ETag = new EntityTagHeaderValue("\"fallback\"");
            fallback.Content.Headers.ContentLength = 8;
            return Task.FromResult(fallback);
        });
        using var client = CreateClient(handler);
        var executor = CreateExecutor(client);
        var directory = CreateTempDirectory();
        var destination = Path.Combine(directory, "client.jar");

        try
        {
            await executor.DownloadFileAsync(
                ManifestUrl,
                DownloadSourcePreference.Official,
                "Mojang",
                destination,
                Convert.ToHexString(SHA1.HashData("partdata"u8.ToArray())),
                8,
                CancellationToken.None);

            Assert.Equal("partdata", await File.ReadAllTextAsync(destination));
            Assert.Equal(2, handler.RequestUris.Count);
            Assert.NotEqual(handler.RequestUris[0].Host, handler.RequestUris[1].Host);
            Assert.False(File.Exists(destination + ".part"));
            Assert.False(File.Exists(destination + ".part.meta"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SourceSwitchDiscardsPartialEvenWhenFallbackFailsBeforeBody()
    {
        var handler = new CallbackRequestHandler((requestNumber, request, _) =>
        {
            if (requestNumber == 1)
            {
                var interrupted = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = request,
                    Content = new StreamContent(new FaultingReadStream("part"u8.ToArray()))
                };
                interrupted.Headers.ETag = new EntityTagHeaderValue("\"stable\"");
                interrupted.Content.Headers.ContentLength = 8;
                return Task.FromResult(interrupted);
            }

            Assert.Null(request.Headers.Range);
            Assert.False(request.Headers.Contains("If-Range"));
            if (requestNumber == 2)
                return Task.FromResult(CreateResponse(HttpStatusCode.InternalServerError, string.Empty, request));

            var completed = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent("partdata"u8.ToArray())
            };
            completed.Headers.ETag = new EntityTagHeaderValue("\"stable\"");
            completed.Content.Headers.ContentLength = 8;
            return Task.FromResult(completed);
        });
        using var client = CreateClient(handler);
        var executor = CreateExecutor(client);
        var directory = CreateTempDirectory();
        var destination = Path.Combine(directory, "client.jar");

        try
        {
            await executor.DownloadFileAsync(
                ManifestUrl,
                DownloadSourcePreference.Official,
                "Mojang",
                destination,
                Convert.ToHexString(SHA1.HashData("partdata"u8.ToArray())),
                8,
                CancellationToken.None);

            Assert.Equal(
                ["piston-meta.mojang.com", "bmclapi2.bangbang93.com", "piston-meta.mojang.com"],
                handler.RequestUris.Select(uri => uri.Host));
            Assert.Equal("partdata", await File.ReadAllTextAsync(destination));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SlowBodyDisconnectSwitchesSourceAndRestartsFromZero()
    {
        var clock = new ManualTimeProvider();
        var handler = new CallbackRequestHandler((requestNumber, request, _) =>
        {
            if (requestNumber == 1)
            {
                var first = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = request,
                    Content = new StreamContent(new TimedChunkReadStream(
                        clock,
                        ((byte)'p', TimeSpan.Zero),
                        ((byte)'a', TimeSpan.Zero),
                        ((byte)'r', TimeSpan.Zero),
                        ((byte)'t', TimeSpan.FromSeconds(6))))
                };
                first.Headers.ETag = new EntityTagHeaderValue("\"stable\"");
                first.Content.Headers.ContentLength = 8;
                return Task.FromResult(first);
            }

            Assert.Null(request.Headers.Range);
            Assert.False(request.Headers.Contains("If-Range"));
            var fallback = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent("partdata"u8.ToArray())
            };
            fallback.Headers.ETag = new EntityTagHeaderValue("\"fallback\"");
            fallback.Content.Headers.ContentLength = 8;
            return Task.FromResult(fallback);
        });
        using var client = CreateClient(handler);
        var executor = CreateExecutor(client, CreateSlowBodyOptions(), timeProvider: clock);
        var directory = CreateTempDirectory();
        var destination = Path.Combine(directory, "client.jar");

        try
        {
            await executor.DownloadFileAsync(
                ManifestUrl,
                DownloadSourcePreference.Official,
                "Mojang",
                destination,
                Convert.ToHexString(SHA1.HashData("partdata"u8.ToArray())),
                8,
                CancellationToken.None);

            Assert.Equal(2, handler.RequestUris.Count);
            Assert.NotEqual(handler.RequestUris[0].Host, handler.RequestUris[1].Host);
            Assert.Equal("partdata", await File.ReadAllTextAsync(destination));
            Assert.False(File.Exists(destination + ".part"));
            Assert.False(File.Exists(destination + ".part.meta"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SingleSourceRecoveryRoundResumesWithMatchingValidator()
    {
        var clock = new ManualTimeProvider();
        var handler = new CallbackRequestHandler((requestNumber, request, _) =>
        {
            if (requestNumber == 1)
            {
                var first = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = request,
                    Content = new StreamContent(new TimedChunkReadStream(
                        clock,
                        ((byte)'p', TimeSpan.Zero),
                        ((byte)'a', TimeSpan.Zero),
                        ((byte)'r', TimeSpan.Zero),
                        ((byte)'t', TimeSpan.FromSeconds(6))))
                };
                first.Headers.ETag = new EntityTagHeaderValue("\"stable\"");
                first.Content.Headers.ContentLength = 8;
                return Task.FromResult(first);
            }

            Assert.Equal("bytes=4-", request.Headers.Range?.ToString());
            Assert.Equal("\"stable\"", request.Headers.GetValues("If-Range").Single());
            var resumed = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                RequestMessage = request,
                Content = new ByteArrayContent("data"u8.ToArray())
            };
            resumed.Headers.ETag = new EntityTagHeaderValue("\"stable\"");
            resumed.Content.Headers.ContentLength = 4;
            resumed.Content.Headers.ContentRange = new ContentRangeHeaderValue(4, 7, 8);
            return Task.FromResult(resumed);
        });
        using var client = CreateClient(handler);
        var executor = CreateExecutor(client, CreateSlowBodyOptions(), timeProvider: clock);
        var directory = CreateTempDirectory();
        var destination = Path.Combine(directory, "client.jar");

        try
        {
            await executor.DownloadFileAsync(
                "https://example.test/client.jar",
                DownloadSourcePreference.Official,
                "ThirdParty",
                destination,
                Convert.ToHexString(SHA1.HashData("partdata"u8.ToArray())),
                8,
                CancellationToken.None);

            Assert.Equal(2, handler.RequestUris.Count);
            Assert.Equal(handler.RequestUris[0], handler.RequestUris[1]);
            Assert.Equal("partdata", await File.ReadAllTextAsync(destination));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LightweightAssetReusesPreviouslyVerifiedTargetWithoutNetwork()
    {
        var handler = new CallbackRequestHandler((_, request, _) =>
            Task.FromResult(CreateResponse(HttpStatusCode.OK, "unexpected", request)));
        using var client = CreateClient(handler);
        var executor = CreateExecutor(client);
        var directory = CreateTempDirectory();
        var destination = Path.Combine(directory, "assets", "objects", "aa", "asset");
        var payload = "asset-data"u8.ToArray();
        var sha1 = Convert.ToHexString(SHA1.HashData(payload));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllBytesAsync(destination, payload);

        try
        {
            using var operation = new MinecraftDownloadOperationContext(directory);
            operation.RegisterAsset(destination, sha1, payload.Length);
            await executor.DownloadFileAsync(
                ManifestUrl,
                DownloadSourcePreference.Official,
                "Mojang",
                destination,
                sha1,
                payload.Length,
                CancellationToken.None,
                options: new DownloadFileOptions(DownloadPersistenceMode.LightweightAtomic, operation));

            Assert.Empty(handler.RequestUris);
            Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LightweightAssetRedownloadsSameSizedCorruptTargetAndPublishesAtomically()
    {
        var payload = "asset-data"u8.ToArray();
        var handler = new CallbackRequestHandler((_, request, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(payload)
            }));
        using var client = CreateClient(handler);
        var executor = CreateExecutor(client);
        var directory = CreateTempDirectory();
        var destination = Path.Combine(directory, "assets", "objects", "aa", "asset");
        var sha1 = Convert.ToHexString(SHA1.HashData(payload));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllBytesAsync(destination, "bad-data!!"u8.ToArray());

        try
        {
            using var operation = new MinecraftDownloadOperationContext(directory);
            operation.RegisterAsset(destination, sha1, payload.Length);
            await executor.DownloadFileAsync(
                ManifestUrl,
                DownloadSourcePreference.Official,
                "Mojang",
                destination,
                sha1,
                payload.Length,
                CancellationToken.None,
                options: new DownloadFileOptions(DownloadPersistenceMode.LightweightAtomic, operation));

            Assert.Single(handler.RequestUris);
            Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
            Assert.False(File.Exists(destination + ".part"));
            Assert.False(File.Exists(destination + ".part.meta"));
            Assert.Empty(Directory.EnumerateFiles(
                Path.GetDirectoryName(destination)!,
                ".asset.bhl-pending-*.tmp",
                SearchOption.TopDirectoryOnly));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LightweightAssetSingleFlightDownloadsSameTargetOnce()
    {
        var payload = "asset-data"u8.ToArray();
        var handler = new CallbackRequestHandler(async (_, request, token) =>
        {
            await Task.Delay(50, token);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(payload)
            };
        });
        using var client = CreateClient(handler);
        var executor = CreateExecutor(client);
        var directory = CreateTempDirectory();
        var destination = Path.Combine(directory, "assets", "objects", "aa", "asset");
        var sha1 = Convert.ToHexString(SHA1.HashData(payload));

        try
        {
            using var operation = new MinecraftDownloadOperationContext(directory);
            operation.RegisterAsset(destination, sha1, payload.Length);
            var options = new DownloadFileOptions(DownloadPersistenceMode.LightweightAtomic, operation);
            var first = executor.DownloadFileAsync(ManifestUrl, DownloadSourcePreference.Official, "Mojang", destination, sha1, payload.Length, CancellationToken.None, options: options);
            var second = executor.DownloadFileAsync(ManifestUrl, DownloadSourcePreference.Official, "Mojang", destination, sha1, payload.Length, CancellationToken.None, options: options);

            await Task.WhenAll(first, second);

            Assert.Single(handler.RequestUris);
            Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ConcurrentOperationContextsDoNotCreateDownloadWorkspaces()
    {
        var directory = CreateTempDirectory();
        MinecraftDownloadOperationContext[] contexts = [];
        try
        {
            contexts = await Task.WhenAll(
                Enumerable.Range(0, 16)
                    .Select(_ => Task.Run(() => new MinecraftDownloadOperationContext(directory))));

            Assert.All(contexts, context => Assert.Equal(Path.GetFullPath(directory), context.ManagedRoot));
            Assert.False(Directory.Exists(Path.Combine(directory, ".bhl-download-work")));
        }
        finally
        {
            foreach (var context in contexts)
                context.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CmlLibMetadataHandlerFailsClosedForBinaryRequest()
    {
        using var client = new HttpClient(new DownloadSourceRoutingHttpMessageHandler(
            DownloadSourcePreference.Official,
            DownloadConcurrencyCategory.Metadata,
            new CallbackRequestHandler((_, request, _) => Task.FromResult(CreateResponse(HttpStatusCode.OK, "binary", request)))));

        await Assert.ThrowsAsync<InvalidDataException>(() => client.GetAsync("https://example.test/library.jar"));
    }

    [Fact]
    public async Task CmlLibMetadataHandlerSwitchesSourceAfterInvalidJson()
    {
        var transport = new CallbackRequestHandler((requestNumber, request, _) =>
            Task.FromResult(CreateResponse(
                HttpStatusCode.OK,
                requestNumber == 1 ? "<html>error</html>" : "{\"versions\":[]}",
                request)));
        using var httpClient = new HttpClient(new DownloadSourceRoutingHttpMessageHandler(
            LauncherDefaults.DefaultDownloadSourcePreference,
            DownloadConcurrencyCategory.Metadata,
            transport,
            limiter: new ImportConcurrencyLimiter(),
            retryOptions: new DownloadRetryOptions
            {
                MaxAttemptsPerSource = 4,
                RetryDelay = TimeSpan.Zero,
                ResponseHeadersTimeout = TimeSpan.FromSeconds(1),
                BodyIdleTimeout = TimeSpan.FromSeconds(1),
                MaxRedirects = 10
            }))
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        var body = await httpClient.GetStringAsync(ManifestUrl);

        Assert.Equal("{\"versions\":[]}", body);
        Assert.Equal(2, transport.RequestUris.Count);
    }

    [Fact]
    public async Task LocalDestinationFailureDoesNotStartNetworkRetry()
    {
        var handler = new CallbackRequestHandler((_, request, _) =>
            Task.FromResult(CreateResponse(HttpStatusCode.OK, "payload", request)));
        using var httpClient = CreateClient(handler);
        var executor = CreateExecutor(httpClient);
        var directory = CreateTempDirectory();
        var fileAsDirectory = Path.Combine(directory, "not-a-directory");
        await File.WriteAllTextAsync(fileAsDirectory, "file");

        try
        {
            await Assert.ThrowsAnyAsync<IOException>(
                () => executor.DownloadFileAsync(
                    ManifestUrl,
                    DownloadSourcePreference.Official,
                    categoryHint: "Mojang",
                    Path.Combine(fileAsDirectory, "client.jar"),
                    expectedSha1: null,
                    expectedSize: null,
                    CancellationToken.None));

            Assert.Empty(handler.RequestUris);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SensitiveCurseForgeHeaderIsNotForwardedToRedirectedCdn()
    {
        var handler = new CallbackRequestHandler((requestNumber, request, _) =>
        {
            if (requestNumber == 1)
            {
                var redirect = CreateResponse(HttpStatusCode.Found, string.Empty, request);
                redirect.Headers.Location = new Uri("https://edge.forgecdn.net/files/mod.jar");
                return Task.FromResult(redirect);
            }
            return Task.FromResult(CreateResponse(HttpStatusCode.OK, "payload", request));
        });
        using var client = CreateClient(handler);
        var transport = new MinecraftDownloadTransport(client, new DownloadRetryOptions());

        await using var result = await transport.SendAsync(
            "https://api.curseforge.com/v1/mods/1/files/2/download-url",
            CancellationToken.None,
            sensitiveHeaders: DownloadRequestHeaders.CurseForgeApiKey("secret"));

        Assert.Equal(2, handler.RequestHeaders.Count);
        Assert.Equal("secret", handler.RequestHeaders[0]["x-api-key"]);
        Assert.False(handler.RequestHeaders[1].ContainsKey("x-api-key"));
        result.Response.Dispose();
    }

    [Fact]
    public void DefaultRedirectLimitIsTwenty()
    {
        Assert.Equal(20, DownloadRetryOptions.Default.MaxRedirects);
    }

    [Fact]
    public void DefaultFileSourceRoundsIsThree()
    {
        Assert.Equal(3, DownloadRetryOptions.Default.MaxFileSourceRounds);
    }

    [Fact]
    public void DefaultResponseHeadersTimeoutIsTenSeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), DownloadRetryOptions.Default.ResponseHeadersTimeout);
    }

    [Fact]
    public void DefaultSlowBodyWatchdogMatchesPclThresholds()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), DownloadRetryOptions.Default.SlowBodyReadThreshold);
        Assert.Equal(1024, DownloadRetryOptions.Default.MinimumBodyBytesPerSecond);
    }

    [Fact]
    public void TransportHandlerUsesSystemProxyAndManualRedirects()
    {
        using var handler = MinecraftHttpClientFactory.CreateTransportHandler();
        var httpHandler = Assert.IsType<HttpClientHandler>(handler);

        Assert.True(httpHandler.UseProxy);
        Assert.False(httpHandler.AllowAutoRedirect);
    }

    [Theory]
    [InlineData("http://127.0.0.1/file.jar")]
    [InlineData("https://10.0.0.1/file.jar")]
    [InlineData("https://172.16.0.1/file.jar")]
    [InlineData("https://192.168.1.10/file.jar")]
    [InlineData("https://198.18.0.1/file.jar")]
    [InlineData("http://[::1]/file.jar")]
    [InlineData("https://[fd00::1]/file.jar")]
    public async Task PrivateAndReservedEndpointsReachUnderlyingHandler(string url)
    {
        var handler = new CallbackRequestHandler((_, request, _) =>
            Task.FromResult(CreateResponse(HttpStatusCode.OK, "payload", request)));
        using var client = CreateClient(handler);
        var transport = new MinecraftDownloadTransport(client, new DownloadRetryOptions());

        await using var result = await transport.SendAsync(url, CancellationToken.None);

        Assert.Single(handler.RequestUris);
        Assert.Equal(new Uri(url), handler.RequestUris[0]);
        result.Response.Dispose();
    }

    [Fact]
    public async Task HttpsRedirectToHttpIsFollowed()
    {
        var handler = new CallbackRequestHandler((requestNumber, request, _) =>
        {
            if (requestNumber == 1)
            {
                var redirect = CreateResponse(HttpStatusCode.Found, string.Empty, request);
                redirect.Headers.Location = new Uri("http://127.0.0.1/file.jar");
                return Task.FromResult(redirect);
            }

            return Task.FromResult(CreateResponse(HttpStatusCode.OK, "payload", request));
        });
        using var client = CreateClient(handler);
        var transport = new MinecraftDownloadTransport(client, new DownloadRetryOptions());

        await using var result = await transport.SendAsync(
            "https://downloads.example.com/file.jar",
            CancellationToken.None);

        Assert.Equal(2, handler.RequestUris.Count);
        Assert.Equal(new Uri("http://127.0.0.1/file.jar"), result.FinalUri);
        result.Response.Dispose();
    }

    [Theory]
    [InlineData("file:///C:/temp/file.jar")]
    [InlineData("ftp://downloads.example.com/file.jar")]
    [InlineData("not-a-uri")]
    public async Task NonHttpInitialAddressIsRejectedBeforeRequest(string url)
    {
        var handler = new CallbackRequestHandler((_, request, _) =>
            Task.FromResult(CreateResponse(HttpStatusCode.OK, "payload", request)));
        using var client = CreateClient(handler);
        var transport = new MinecraftDownloadTransport(client, new DownloadRetryOptions());

        var exception = await Assert.ThrowsAsync<DownloadAttemptException>(() =>
            transport.SendAsync(url, CancellationToken.None));

        Assert.Equal(DownloadFailureReason.InvalidRedirect, exception.Reason);
        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task NonHttpRedirectIsRejectedBeforeSecondRequest()
    {
        var handler = new CallbackRequestHandler((_, request, _) =>
        {
            var redirect = CreateResponse(HttpStatusCode.Found, string.Empty, request);
            redirect.Headers.Location = new Uri("ftp://downloads.example.com/file.jar");
            return Task.FromResult(redirect);
        });
        using var client = CreateClient(handler);
        var transport = new MinecraftDownloadTransport(client, new DownloadRetryOptions());

        var exception = await Assert.ThrowsAsync<DownloadAttemptException>(() =>
            transport.SendAsync("https://downloads.example.com/file.jar", CancellationToken.None));

        Assert.Equal(DownloadFailureReason.InvalidRedirect, exception.Reason);
        Assert.Single(handler.RequestUris);
    }

    [Fact]
    public async Task RedirectWithoutLocationIsRejected()
    {
        var handler = new CallbackRequestHandler((_, request, _) =>
            Task.FromResult(CreateResponse(HttpStatusCode.Found, string.Empty, request)));
        using var client = CreateClient(handler);
        var transport = new MinecraftDownloadTransport(client, new DownloadRetryOptions());

        var exception = await Assert.ThrowsAsync<DownloadAttemptException>(() =>
            transport.SendAsync("https://downloads.example.com/file.jar", CancellationToken.None));

        Assert.Equal(DownloadFailureReason.InvalidRedirect, exception.Reason);
        Assert.Single(handler.RequestUris);
    }

    [Fact]
    public async Task TwentyRedirectsCanComplete()
    {
        var handler = new CallbackRequestHandler((requestNumber, request, _) =>
        {
            if (requestNumber <= 20)
            {
                var redirect = CreateResponse(HttpStatusCode.Found, string.Empty, request);
                redirect.Headers.Location = new Uri($"/hop/{requestNumber}", UriKind.Relative);
                return Task.FromResult(redirect);
            }

            return Task.FromResult(CreateResponse(HttpStatusCode.OK, "payload", request));
        });
        using var client = CreateClient(handler);
        var transport = new MinecraftDownloadTransport(client, DownloadRetryOptions.Default);

        await using var result = await transport.SendAsync(
            "https://downloads.example.com/file.jar",
            CancellationToken.None);

        Assert.Equal(21, handler.RequestUris.Count);
        Assert.Equal(20, result.RedirectCount);
        result.Response.Dispose();
    }

    [Fact]
    public async Task TwentyFirstRedirectIsRejected()
    {
        var handler = new CallbackRequestHandler((requestNumber, request, _) =>
        {
            var redirect = CreateResponse(HttpStatusCode.Found, string.Empty, request);
            redirect.Headers.Location = new Uri($"/hop/{requestNumber}", UriKind.Relative);
            return Task.FromResult(redirect);
        });
        using var client = CreateClient(handler);
        var transport = new MinecraftDownloadTransport(client, DownloadRetryOptions.Default);

        var exception = await Assert.ThrowsAsync<DownloadAttemptException>(() => transport.SendAsync(
            "https://downloads.example.com/file.jar",
            CancellationToken.None));

        Assert.Equal(DownloadFailureReason.InvalidRedirect, exception.Reason);
        Assert.Equal(21, handler.RequestUris.Count);
    }

    [Fact]
    public void HostHealthAvoidsOnlyRepeatedTransientFailures()
    {
        var tracker = new DownloadHostHealthTracker();
        tracker.RecordFailure("BmclApiMojang", "node.example", DownloadFailureReason.Network);
        tracker.RecordFailure("BmclApiMojang", "node.example", DownloadFailureReason.BodyTooSlow);
        Assert.False(tracker.ShouldAvoid("BmclApiMojang", "node.example"));

        tracker.RecordFailure("BmclApiMojang", "node.example", DownloadFailureReason.ResponseHeadersTimeout);
        Assert.True(tracker.ShouldAvoid("BmclApiMojang", "node.example"));

        tracker.RecordSuccess("BmclApiMojang", "node.example");
        Assert.False(tracker.ShouldAvoid("BmclApiMojang", "node.example"));
        tracker.RecordFailure("BmclApiMojang", "node.example", DownloadFailureReason.HashMismatch);
        Assert.False(tracker.ShouldAvoid("BmclApiMojang", "node.example"));
    }

    [Fact]
    public async Task SuccessNoResultAndFailureLogsRedactSensitiveUriComponents()
    {
        const string signedUrl = "https://example.test/files/mod.jar?token=super-secret#private-fragment";
        var logger = new CollectingLogger();

        using (var successClient = CreateClient(new CallbackRequestHandler((_, request, _) =>
                   Task.FromResult(CreateResponse(HttpStatusCode.OK, "payload", request)))))
        {
            var executor = CreateExecutor(successClient, logger: logger);
            await executor.ExecuteAsync(
                signedUrl,
                DownloadSourcePreference.Official,
                categoryHint: null,
                static (_, _) => Task.FromResult(true),
                CancellationToken.None);
        }

        using (var noResultClient = CreateClient(new CallbackRequestHandler((_, request, _) =>
                   Task.FromResult(CreateResponse(HttpStatusCode.NotFound, string.Empty, request)))))
        {
            var executor = CreateExecutor(noResultClient, logger: logger);
            var result = await executor.ExecuteLookupAsync(
                signedUrl,
                DownloadSourcePreference.Official,
                categoryHint: null,
                static (_, _) => Task.FromResult(true),
                status => status == HttpStatusCode.NotFound,
                CancellationToken.None);
            Assert.False(result.Found);
        }

        using (var failureClient = CreateClient(new CallbackRequestHandler((_, request, _) =>
                   Task.FromResult(CreateResponse(HttpStatusCode.Forbidden, string.Empty, request)))))
        {
            var executor = CreateExecutor(failureClient, logger: logger);
            await Assert.ThrowsAsync<MinecraftDownloadRequestExecutor.DownloadSourceRequestException>(() =>
                executor.ExecuteAsync(
                    signedUrl,
                    DownloadSourcePreference.Official,
                    categoryHint: null,
                    static (_, _) => Task.FromResult(true),
                    CancellationToken.None));
        }

        Assert.NotEmpty(logger.Messages);
        Assert.Contains(logger.Messages, message => message.Contains("https://example.test/files/mod.jar", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("super-secret", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("private-fragment", StringComparison.Ordinal));
    }

    [Fact]
    public void UriLogSanitizerRemovesUserInfoQueryAndFragment()
    {
        var sanitized = DownloadUriLogSanitizer.Sanitize(
            "https://username:password@example.test:8443/files/mod.jar?signature=secret#fragment");

        Assert.Equal("https://example.test:8443/files/mod.jar", sanitized);
        Assert.Equal("<invalid-uri>", DownloadUriLogSanitizer.Sanitize("not a uri"));
    }

    [Fact]
    public void PublishMutexNameIsCaseInsensitiveOnWindows()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var path = Path.Combine(Path.GetTempPath(), "Launcher", "Libraries", "Example.jar");

        Assert.Equal(
            ResumableDownloadFileSession.GetFinalPublishMutexName(path),
            ResumableDownloadFileSession.GetFinalPublishMutexName(path.ToLowerInvariant()));
    }

    private static MinecraftDownloadRequestExecutor CreateExecutor(
        HttpClient httpClient,
        DownloadRetryOptions? options = null,
        ILogger? logger = null,
        TimeProvider? timeProvider = null,
        DownloadHostHealthTracker? hostHealthTracker = null)
    {
        return new MinecraftDownloadRequestExecutor(
            httpClient,
            limiter: new ImportConcurrencyLimiter(),
            logger: logger,
            hostConcurrencyController: new DownloadHostConcurrencyController(
                maximumJitter: TimeSpan.Zero,
                nextJitter: () => 0,
                delayAsync: static (_, _) => ValueTask.CompletedTask),
            nextRetryJitter: () => 0,
            bmclApiRequestRateLimiter: new BmclApiRequestRateLimiter(TimeSpan.Zero),
            timeProvider: timeProvider,
            hostHealthTracker: hostHealthTracker,
            retryOptions: options ?? new DownloadRetryOptions
            {
                MaxAttemptsPerSource = 4,
                RetryDelay = TimeSpan.Zero,
                ResponseHeadersTimeout = TimeSpan.FromSeconds(1),
                BodyIdleTimeout = TimeSpan.FromSeconds(1),
                MaxRedirects = 10
            });
    }

    private static DownloadRetryOptions CreateSlowBodyOptions() => new()
    {
        MaxAttemptsPerSource = 4,
        RetryDelay = TimeSpan.Zero,
        ResponseHeadersTimeout = TimeSpan.FromSeconds(1),
        FirstByteTimeout = TimeSpan.FromSeconds(20),
        BodyIdleTimeout = TimeSpan.FromSeconds(20),
        SlowBodyReadThreshold = TimeSpan.FromSeconds(5),
        MinimumBodyBytesPerSecond = 1024,
        MaxRedirects = 10
    };

    private static HttpResponseMessage CreateSlowResponse(
        HttpRequestMessage request,
        ManualTimeProvider timeProvider)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StreamContent(new TimedChunkReadStream(
                timeProvider,
                ((byte)'{', TimeSpan.Zero),
                ((byte)'}', TimeSpan.FromSeconds(6))))
        };
    }

    private static HttpClient CreateClient(HttpMessageHandler handler)
    {
        return new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    private static HttpResponseMessage CreateResponse(
        HttpStatusCode statusCode,
        string content,
        HttpRequestMessage request)
    {
        return new HttpResponseMessage(statusCode)
        {
            RequestMessage = request,
            Content = new StringContent(content)
        };
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "launcher-download-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class CallbackRequestHandler(
        Func<int, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback)
        : HttpMessageHandler
    {
        private int requestCount;

        public List<Uri> RequestUris { get; } = [];
        public List<Dictionary<string, string>> RequestHeaders { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            RequestHeaders.Add(request.Headers.ToDictionary(
                header => header.Key,
                header => string.Join(",", header.Value),
                StringComparer.OrdinalIgnoreCase));
            return callback(Interlocked.Increment(ref requestCount), request, cancellationToken);
        }
    }

    private sealed class CollectingLogger : ILogger
    {
        public List<string> Messages { get; } = [];
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            Messages.Add(message);
            Entries.Add(new LogEntry(logLevel, message, exception));
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    private sealed class FaultingReadStream(byte[] firstChunk) : Stream
    {
        private bool returnedFirstChunk;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (returnedFirstChunk)
                return ValueTask.FromException<int>(new IOException("The network stream ended."));

            returnedFirstChunk = true;
            firstChunk.AsSpan().CopyTo(buffer.Span);
            return ValueTask.FromResult(firstChunk.Length);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class BlockingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref timestamp);

        public void Advance(TimeSpan duration) => Interlocked.Add(ref timestamp, duration.Ticks);
    }

    private sealed class TimedChunkReadStream(
        ManualTimeProvider timeProvider,
        params (byte Value, TimeSpan Elapsed)[] chunks) : Stream
    {
        private int readIndex;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (readIndex >= chunks.Length)
                return ValueTask.FromResult(0);

            var chunk = chunks[readIndex++];
            timeProvider.Advance(chunk.Elapsed);
            buffer.Span[0] = chunk.Value;
            return ValueTask.FromResult(1);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
