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

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task TransientStatusesConsumeFourAttemptBudget(HttpStatusCode statusCode)
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

        Assert.Equal(4, handler.RequestUris.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.MethodNotAllowed)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task PermanentStatusesDoNotRetry(HttpStatusCode statusCode)
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

        Assert.Single(handler.RequestUris);
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
            DownloadSourcePreference.Auto,
            categoryHint: "Mojang",
            async (context, token) => await context.Response.Content.ReadAsStringAsync(token),
            CancellationToken.None);

        Assert.Equal("{}", result);
        Assert.Equal(5, handler.RequestUris.Count);
        Assert.All(handler.RequestUris.Take(4), uri => Assert.Equal(handler.RequestUris[0], uri));
        Assert.NotEqual(handler.RequestUris[0], handler.RequestUris[4]);
    }

    [Fact]
    public async Task AutoSourceSwitchesImmediatelyAfterSustainedLowBodySpeed()
    {
        var handler = new CallbackRequestHandler((requestNumber, request, _) =>
        {
            HttpContent content = requestNumber == 1
                ? new StreamContent(new SustainedLowSpeedStream())
                : new StringContent("{}");
            if (requestNumber == 1)
                content.Headers.ContentLength = 4 * 1024 * 1024;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = content
            });
        });
        using var httpClient = CreateClient(handler);
        var executor = CreateExecutor(httpClient, new DownloadRetryOptions
        {
            MaxAttemptsPerSource = 4,
            RetryDelay = TimeSpan.Zero,
            ResponseHeadersTimeout = TimeSpan.FromSeconds(1),
            BodyIdleTimeout = TimeSpan.FromSeconds(1),
            SustainedLowSpeedWindow = TimeSpan.FromMilliseconds(10),
            SustainedLowSpeedBytesPerSecond = 1024,
            LowSpeedMinimumFileBytes = 1
        });

        var result = await executor.ExecuteAsync(
            ManifestUrl,
            DownloadSourcePreference.Auto,
            categoryHint: "Mojang",
            async (context, token) => await context.Response.Content.ReadAsStringAsync(token),
            CancellationToken.None);

        Assert.Equal("{}", result);
        Assert.Equal(2, handler.RequestUris.Count);
        Assert.NotEqual(handler.RequestUris[0], handler.RequestUris[1]);
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
            DownloadSourcePreference.Auto,
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
                DownloadSourcePreference.Auto,
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
            DownloadSourcePreference.Auto,
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
                DownloadSourcePreference.Auto,
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

    [Fact]
    public async Task InvalidJsonSwitchesSourceWithoutRetry()
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
            DownloadSourcePreference.Auto,
            categoryHint: "Mojang",
            async (context, token) =>
            {
                await using var stream = await context.Response.Content.ReadAsStreamAsync(token);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
                return document.RootElement.GetProperty("versions").GetArrayLength();
            },
            CancellationToken.None);

        Assert.Equal(2, handler.RequestUris.Count);
    }

    [Fact]
    public async Task InterruptedBodyRetriesSameSourceAndReplacesOnlyCompleteFile()
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
            Assert.Equal(handler.RequestUris[0], handler.RequestUris[1]);
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
                DownloadSourcePreference.Auto,
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

        Assert.Equal(2, handler.RequestUris.Count);
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

        Assert.Equal(2, handler.RequestUris.Count);
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
                DownloadSourcePreference.Auto,
                categoryHint: "Mojang",
                static (_, _) => Task.FromResult(true),
                CancellationToken.None));

        Assert.Equal(2, handler.RequestUris.Count);
    }

    [Fact]
    public async Task CmlLibGameInstallerUsesSingleExecutorBudgetForBodyFailure()
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
    public async Task FileDownloadResumesOnlyAfterMatchingPartialResponse()
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
            Assert.False(File.Exists(destination + ".part"));
            Assert.False(File.Exists(destination + ".part.meta"));
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
            DownloadSourcePreference.Auto,
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
        var policy = new DownloadAddressPolicy((_, _) => Task.FromResult(new[] { IPAddress.Parse("8.8.8.8") }));
        var transport = new MinecraftDownloadTransport(client, new DownloadRetryOptions(), policy);

        var result = await transport.SendAsync(
            "https://api.curseforge.com/v1/mods/1/files/2/download-url",
            CancellationToken.None,
            sensitiveHeaders: DownloadRequestHeaders.CurseForgeApiKey("secret"),
            isThirdParty: true);

        Assert.Equal(2, handler.RequestHeaders.Count);
        Assert.Equal("secret", handler.RequestHeaders[0]["x-api-key"]);
        Assert.False(handler.RequestHeaders[1].ContainsKey("x-api-key"));
        result.Response.Dispose();
    }

    [Fact]
    public async Task ThirdPartyPrivateAddressIsRejectedBeforeAnyRequest()
    {
        var handler = new CallbackRequestHandler((_, request, _) =>
            Task.FromResult(CreateResponse(HttpStatusCode.OK, "payload", request)));
        using var client = CreateClient(handler);
        var policy = new DownloadAddressPolicy((_, _) => Task.FromResult(new[] { IPAddress.Parse("10.0.0.1") }));
        var transport = new MinecraftDownloadTransport(client, new DownloadRetryOptions(), policy);

        var exception = await Assert.ThrowsAsync<DownloadAttemptException>(() => transport.SendAsync(
            "https://example.invalid/file.jar", CancellationToken.None, isThirdParty: true));

        Assert.Equal(DownloadFailureReason.UnsafeAddress, exception.Reason);
        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task RedirectTargetIsResolvedAndValidatedAgain()
    {
        var handler = new CallbackRequestHandler((_, request, _) =>
        {
            var redirect = CreateResponse(HttpStatusCode.Found, string.Empty, request);
            redirect.Headers.Location = new Uri("https://private.example.invalid/file.jar");
            return Task.FromResult(redirect);
        });
        using var client = CreateClient(handler);
        var resolutions = 0;
        var policy = new DownloadAddressPolicy((host, _) =>
        {
            resolutions++;
            return Task.FromResult(new[]
            {
                IPAddress.Parse(host.StartsWith("private", StringComparison.Ordinal) ? "192.168.1.10" : "8.8.8.8")
            });
        });
        var transport = new MinecraftDownloadTransport(client, new DownloadRetryOptions(), policy);

        await Assert.ThrowsAsync<DownloadAttemptException>(() => transport.SendAsync(
            "https://public.example.invalid/file.jar", CancellationToken.None, isThirdParty: true));

        Assert.Equal(2, resolutions);
        Assert.Single(handler.RequestUris);
    }

    [Fact]
    public void HostHealthAvoidsOnlyRepeatedTransientFailures()
    {
        var tracker = new DownloadHostHealthTracker();
        tracker.RecordFailure("BmclApiMojang", "node.example", DownloadFailureReason.Network);
        tracker.RecordFailure("BmclApiMojang", "node.example", DownloadFailureReason.BodyIdleTimeout);
        Assert.False(tracker.ShouldAvoid("BmclApiMojang", "node.example"));

        tracker.RecordFailure("BmclApiMojang", "node.example", DownloadFailureReason.ResponseHeadersTimeout);
        Assert.True(tracker.ShouldAvoid("BmclApiMojang", "node.example"));

        tracker.RecordSuccess("BmclApiMojang", "node.example");
        Assert.False(tracker.ShouldAvoid("BmclApiMojang", "node.example"));
        tracker.RecordFailure("BmclApiMojang", "node.example", DownloadFailureReason.HashMismatch);
        Assert.False(tracker.ShouldAvoid("BmclApiMojang", "node.example"));
    }

    [Fact]
    public void HostHealthAvoidsSustainedLowSpeedImmediately()
    {
        var tracker = new DownloadHostHealthTracker();

        tracker.RecordFailure("BmclApiMojang", "slow.example", DownloadFailureReason.SustainedLowSpeed);

        Assert.True(tracker.ShouldAvoid("BmclApiMojang", "slow.example"));
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
        ILogger? logger = null)
    {
        return new MinecraftDownloadRequestExecutor(
            httpClient,
            limiter: new ImportConcurrencyLimiter(),
            logger: logger,
            retryOptions: options ?? new DownloadRetryOptions
            {
                MaxAttemptsPerSource = 4,
                RetryDelay = TimeSpan.Zero,
                ResponseHeadersTimeout = TimeSpan.FromSeconds(1),
                BodyIdleTimeout = TimeSpan.FromSeconds(1),
                MaxRedirects = 10
            });
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

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }

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

    private sealed class SustainedLowSpeedStream : Stream
    {
        private int reads;

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
            if (reads++ == 0)
            {
                buffer.Span[0] = (byte)'x';
                return 1;
            }

            if (reads == 2)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
                buffer.Span[0] = (byte)'x';
                return 1;
            }

            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
