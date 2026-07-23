/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;
using Launcher.Infrastructure.Modpacks;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class SegmentedDownloadTests : TestTempDirectory
{
    private const string DownloadUrl = "https://downloads.example.test/client.jar";
    private static readonly byte[] LargePayload = CreatePayload(
        checked((int)MinecraftDownloadRequestExecutor.MinimumSegmentedDownloadSize));

    [Fact]
    public async Task TrustedLargeFileUsesDynamicRangesAndPublishesVerifiedPayload()
    {
        var handler = new RecordingRequestHandler((_, request, _) =>
        {
            Assert.Equal("identity", request.Headers.AcceptEncoding.Single().Value);
            return Task.FromResult(CreatePartialResponse(request, LargePayload));
        });
        using var client = CreateClient(handler);
        var executor = CreateExecutor(client);
        var destination = Path.Combine(TempRoot, "client.jar");
        var progress = new List<(int Attempt, long Bytes, long? Total)>();

        await executor.DownloadFileAsync(
            DownloadUrl,
            DownloadSourcePreference.Official,
            "ThirdParty",
            destination,
            new DownloadIntegrityExpectation(
                LargePayload.Length,
                [(HashAlgorithmName.SHA512, Sha512(LargePayload))]),
            CancellationToken.None,
            reportAttemptProgress: (attempt, bytes, total) => progress.Add((attempt, bytes, total)));

        Assert.True(handler.RequestCount > 4);
        Assert.Equal(LargePayload, await File.ReadAllBytesAsync(destination));
        AssertRangesCoverPayload(handler.RangeHeaders, LargePayload.Length);
        Assert.NotEmpty(progress);
        Assert.All(progress, item =>
        {
            Assert.Equal(1, item.Attempt);
            Assert.Equal(LargePayload.Length, item.Total);
        });
        Assert.Equal(progress.Select(item => item.Bytes).Order().ToArray(), progress.Select(item => item.Bytes).ToArray());
        Assert.Equal(LargePayload.Length, progress[^1].Bytes);
        AssertNoPendingFiles(destination);
    }

    [Fact]
    public async Task InvalidContentRangeFallsBackOnceToSingleStream()
    {
        var handler = new RecordingRequestHandler((requestNumber, request, _) =>
        {
            if (requestNumber > 1)
                return Task.FromResult(CreateFullResponse(request, LargePayload));

            var response = CreatePartialResponse(request, LargePayload);
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(1, 2, LargePayload.Length);
            return Task.FromResult(response);
        });
        using var client = CreateClient(handler);
        var executor = CreateExecutor(client);
        var destination = Path.Combine(TempRoot, "invalid-range.jar");

        await executor.DownloadFileAsync(
            DownloadUrl,
            DownloadSourcePreference.Official,
            "ThirdParty",
            destination,
            Sha1(LargePayload),
            LargePayload.Length,
            CancellationToken.None);

        Assert.Equal(2, handler.RequestCount);
        Assert.NotNull(handler.RangeHeaders.ElementAt(0));
        Assert.Null(handler.RangeHeaders.ElementAt(1));
        Assert.Equal(LargePayload, await File.ReadAllBytesAsync(destination));
        AssertNoPendingFiles(destination);
    }

    [Fact]
    public async Task CorruptSegmentFallsBackToVerifiedSingleStream()
    {
        var corrupted = 0;
        var handler = new RecordingRequestHandler((_, request, _) =>
        {
            if (request.Headers.Range is null)
                return Task.FromResult(CreateFullResponse(request, LargePayload));

            var shouldCorrupt = Interlocked.CompareExchange(ref corrupted, 1, 0) == 0;
            return Task.FromResult(CreatePartialResponse(request, LargePayload, shouldCorrupt));
        });
        using var client = CreateClient(handler);
        var executor = CreateExecutor(client);
        var destination = Path.Combine(TempRoot, "client.jar");

        await executor.DownloadFileAsync(
            DownloadUrl,
            DownloadSourcePreference.Official,
            "ThirdParty",
            destination,
            Sha1(LargePayload),
            LargePayload.Length,
            CancellationToken.None);

        Assert.True(handler.RequestCount > 5);
        Assert.Null(handler.RangeHeaders.Last());
        Assert.Equal(LargePayload, await File.ReadAllBytesAsync(destination));
        AssertNoPendingFiles(destination);
    }

    [Fact]
    public async Task UnknownSizeIsLearnedFromFirstPartialResponse()
    {
        var handler = new RecordingRequestHandler((_, request, _) =>
            Task.FromResult(CreatePartialResponse(request, LargePayload)));
        using var client = CreateClient(handler);
        var destination = Path.Combine(TempRoot, "unknown-size.jar");

        await CreateExecutor(client).DownloadFileAsync(
            DownloadUrl,
            DownloadSourcePreference.Official,
            "ThirdParty",
            destination,
            new DownloadIntegrityExpectation(
                expectedSize: null,
                [(HashAlgorithmName.SHA512, Sha512(LargePayload))]),
            CancellationToken.None);

        Assert.True(handler.RequestCount > 4);
        Assert.Equal(LargePayload, await File.ReadAllBytesAsync(destination));
        AssertNoPendingFiles(destination);
    }

    [Fact]
    public async Task TransientProbeFailureRetriesRangeInsteadOfFallingBack()
    {
        var handler = new RecordingRequestHandler((requestNumber, request, _) =>
            requestNumber == 1
                ? Task.FromException<HttpResponseMessage>(new HttpRequestException("transient probe failure"))
                : Task.FromResult(CreatePartialResponse(request, LargePayload)));
        using var client = CreateClient(handler);
        var destination = Path.Combine(TempRoot, "probe-retry.jar");

        await CreateExecutor(client, maxAttemptsPerSource: 2).DownloadFileAsync(
            DownloadUrl,
            DownloadSourcePreference.Official,
            "ThirdParty",
            destination,
            Sha1(LargePayload),
            LargePayload.Length,
            CancellationToken.None);

        Assert.True(handler.RequestCount > 5);
        Assert.All(handler.RangeHeaders, range => Assert.NotNull(range));
        Assert.Equal(LargePayload, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task InterruptedRangeRetriesOnlyThatRangeAndKeepsProgressMonotonic()
    {
        var interrupted = 0;
        var progress = new List<long>();
        var handler = new RecordingRequestHandler((_, request, _) =>
        {
            if (Interlocked.CompareExchange(ref interrupted, 1, 0) != 0)
                return Task.FromResult(CreatePartialResponse(request, LargePayload));

            var range = Assert.Single(request.Headers.Range!.Ranges);
            var start = range.From!.Value;
            var end = range.To ?? LargePayload.Length - 1;
            var bytes = LargePayload.AsSpan(
                checked((int)start),
                checked((int)(end - start + 1))).ToArray();
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                RequestMessage = request,
                Content = new StreamContent(new InterruptingReadStream(bytes, 160 * 1024))
            };
            response.Content.Headers.ContentLength = bytes.Length;
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, end, LargePayload.Length);
            return Task.FromResult(response);
        });
        using var client = CreateClient(handler);
        var destination = Path.Combine(TempRoot, "range-retry.jar");

        await CreateExecutor(client, maxAttemptsPerSource: 2).DownloadFileAsync(
            DownloadUrl,
            DownloadSourcePreference.Official,
            "ThirdParty",
            destination,
            Sha1(LargePayload),
            LargePayload.Length,
            CancellationToken.None,
            reportAttemptProgress: (_, bytes, _) => progress.Add(bytes));

        Assert.True(handler.RequestCount > 5);
        Assert.All(handler.RangeHeaders, range => Assert.NotNull(range));
        Assert.Equal(progress.Order().ToArray(), progress.ToArray());
        Assert.Equal(LargePayload.Length, progress[^1]);
        Assert.Equal(LargePayload, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task IgnoredProbeRangeConsumesSameOkResponseWithoutSecondRequest()
    {
        var handler = FullResponseHandler(LargePayload);
        using var client = CreateClient(handler);
        var destination = Path.Combine(TempRoot, "range-ignored.jar");

        await CreateExecutor(client).DownloadFileAsync(
            DownloadUrl,
            DownloadSourcePreference.Official,
            "ThirdParty",
            destination,
            Sha1(LargePayload),
            LargePayload.Length,
            CancellationToken.None);

        Assert.Equal(1, handler.RequestCount);
        Assert.NotNull(handler.RangeHeaders.Single());
        Assert.Equal(LargePayload, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task PartialResponsesWithoutContentLengthAreAccepted()
    {
        var handler = new RecordingRequestHandler((_, request, _) =>
        {
            var response = CreatePartialResponse(request, LargePayload);
            response.Content.Headers.ContentLength = null;
            return Task.FromResult(response);
        });
        using var client = CreateClient(handler);
        var destination = Path.Combine(TempRoot, "no-content-length.jar");

        await CreateExecutor(client).DownloadFileAsync(
            DownloadUrl,
            DownloadSourcePreference.Official,
            "ThirdParty",
            destination,
            Sha1(LargePayload),
            LargePayload.Length,
            CancellationToken.None);

        Assert.NotEmpty(handler.RangeHeaders);
        Assert.All(handler.RangeHeaders, range => Assert.NotNull(range));
        Assert.Equal(LargePayload, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task LegalShortPartialResponsesRequeueOnlyTheirSuffix()
    {
        var handler = new RecordingRequestHandler((_, request, _) =>
            Task.FromResult(CreatePartialResponse(
                request,
                LargePayload,
                maximumBytes: 128 * 1024)));
        using var client = CreateClient(handler);
        var destination = Path.Combine(TempRoot, "short-partial.jar");

        await CreateExecutor(client).DownloadFileAsync(
            DownloadUrl,
            DownloadSourcePreference.Official,
            "ThirdParty",
            destination,
            Sha1(LargePayload),
            LargePayload.Length,
            CancellationToken.None);

        Assert.True(handler.RequestCount > LargePayload.Length / MinecraftDownloadRequestExecutor.MinimumSegmentedChunkSize);
        Assert.All(handler.RangeHeaders, range => Assert.NotNull(range));
        Assert.Equal(LargePayload, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task StrongETagIsSentAsIfRangeOnFollowingChunks()
    {
        var handler = new RecordingRequestHandler((_, request, _) =>
        {
            var response = CreatePartialResponse(request, LargePayload);
            response.Headers.ETag = new EntityTagHeaderValue("\"version-1\"");
            return Task.FromResult(response);
        });
        using var client = CreateClient(handler);
        var destination = Path.Combine(TempRoot, "if-range.jar");

        await CreateExecutor(client).DownloadFileAsync(
            DownloadUrl,
            DownloadSourcePreference.Official,
            "ThirdParty",
            destination,
            Sha1(LargePayload),
            LargePayload.Length,
            CancellationToken.None);

        var requests = handler.Requests.ToArray();
        Assert.Null(requests[0].IfRange);
        Assert.All(requests.Skip(1), request => Assert.Equal("\"version-1\"", request.IfRange));
        Assert.Equal(LargePayload, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task ChangedStrongETagStopsSegmentedSourceAndFallsBackCleanly()
    {
        var partialCount = 0;
        var handler = new RecordingRequestHandler((_, request, _) =>
        {
            if (request.Headers.Range is null)
                return Task.FromResult(CreateFullResponse(request, LargePayload));

            var response = CreatePartialResponse(request, LargePayload);
            response.Headers.ETag = new EntityTagHeaderValue(
                Interlocked.Increment(ref partialCount) == 1 ? "\"version-1\"" : "\"version-2\"");
            return Task.FromResult(response);
        });
        using var client = CreateClient(handler);
        var destination = Path.Combine(TempRoot, "etag-changed.jar");

        await CreateExecutor(client).DownloadFileAsync(
            DownloadUrl,
            DownloadSourcePreference.Official,
            "ThirdParty",
            destination,
            Sha1(LargePayload),
            LargePayload.Length,
            CancellationToken.None);

        Assert.Null(handler.RangeHeaders.Last());
        Assert.Equal(LargePayload, await File.ReadAllBytesAsync(destination));
        AssertNoPendingFiles(destination);
    }

    [Fact]
    public async Task RequestedRangeNotSatisfiableStopsSegmentedSourceBeforeSingleStreamFallback()
    {
        var handler = new RecordingRequestHandler((_, request, _) =>
            Task.FromResult(request.Headers.Range is null
                ? CreateFullResponse(request, LargePayload)
                : new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    RequestMessage = request
                }));
        using var client = CreateClient(handler);
        var destination = Path.Combine(TempRoot, "range-416.jar");

        await CreateExecutor(client).DownloadFileAsync(
            DownloadUrl,
            DownloadSourcePreference.Official,
            "ThirdParty",
            destination,
            Sha1(LargePayload),
            LargePayload.Length,
            CancellationToken.None);

        Assert.Equal(2, handler.RequestCount);
        Assert.NotNull(handler.RangeHeaders.First());
        Assert.Null(handler.RangeHeaders.Last());
        Assert.Equal(LargePayload, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task HashMismatchOnFirstCandidateStartsSecondCandidateFromCleanFile()
    {
        const string fallbackUrl = "https://fallback.example.test/client.jar";
        var handler = new RecordingRequestHandler((_, request, _) =>
            Task.FromResult(CreatePartialResponse(
                request,
                LargePayload,
                corrupt: request.RequestUri!.Host == "downloads.example.test")));
        using var client = CreateClient(handler);
        var destination = Path.Combine(TempRoot, "candidate-switch.jar");

        await CreateExecutor(client).DownloadFileAsync(
            [DownloadUrl, fallbackUrl],
            DownloadSourcePreference.Official,
            "ThirdParty",
            destination,
            new DownloadIntegrityExpectation(
                LargePayload.Length,
                [(HashAlgorithmName.SHA512, Sha512(LargePayload))]),
            CancellationToken.None);

        Assert.Contains(handler.Requests, request => request.Host == "downloads.example.test");
        Assert.Contains(handler.Requests, request => request.Host == "fallback.example.test");
        Assert.All(handler.RangeHeaders, range => Assert.NotNull(range));
        Assert.Equal(LargePayload, await File.ReadAllBytesAsync(destination));
        AssertNoPendingFiles(destination);
    }

    [Fact]
    public async Task RedirectedHttpsCdnIsPinnedForFollowingChunks()
    {
        var handler = new RecordingRequestHandler((_, request, _) =>
        {
            if (request.RequestUri!.Host == "downloads.example.test")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Found)
                {
                    RequestMessage = request,
                    Headers = { Location = new Uri("https://cdn.example.test/client.jar") }
                });
            }
            return Task.FromResult(CreatePartialResponse(request, LargePayload));
        });
        using var client = CreateClient(handler);
        var destination = Path.Combine(TempRoot, "redirected.jar");

        await CreateExecutor(client).DownloadFileAsync(
            DownloadUrl,
            DownloadSourcePreference.Official,
            "ThirdParty",
            destination,
            Sha1(LargePayload),
            LargePayload.Length,
            CancellationToken.None);

        Assert.Equal(1, handler.Requests.Count(request => request.Host == "downloads.example.test"));
        Assert.True(handler.Requests.Count(request => request.Host == "cdn.example.test") > 4);
        Assert.Equal(LargePayload, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task FiftyOneMiBFileUsesMoreThanFourButNoMoreThanSixtyFourConcurrentRanges()
    {
        var payload = CreatePayload(51 * 1024 * 1024);
        var active = 0;
        var maximumActive = 0;
        var handler = new RecordingRequestHandler(async (_, request, cancellationToken) =>
        {
            var current = Interlocked.Increment(ref active);
            UpdateMaximum(ref maximumActive, current);
            try
            {
                if (request.Headers.Range!.Ranges.Single().From != 0)
                    await Task.Delay(40, cancellationToken);
                return CreatePartialResponse(request, payload);
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        });
        using var client = CreateClient(handler);
        var destination = Path.Combine(TempRoot, "51-mib.jar");

        await CreateExecutor(client).DownloadFileAsync(
            DownloadUrl,
            DownloadSourcePreference.Official,
            "ThirdParty",
            destination,
            Sha1(payload),
            payload.Length,
            CancellationToken.None);

        Assert.True(maximumActive > 4, $"Observed only {maximumActive} concurrent requests.");
        Assert.InRange(maximumActive, 1, 64);
        Assert.True(handler.RequestCount > 64);
        Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task LargeFileAbsorbsReleasedSlotsBySplittingInFlightTail()
    {
        var payload = CreatePayload(16 * 1024 * 1024);
        var releaseBodies = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oneActive = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fourActive = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var eightActive = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var maximumActive = 0;
        var progress = new ConcurrentQueue<long>();
        var handler = new RecordingRequestHandler((_, request, _) =>
        {
            var range = Assert.Single(request.Headers.Range!.Ranges);
            if (range.From == 0)
                return Task.FromResult(CreatePartialResponse(request, payload));

            var start = range.From!.Value;
            var end = range.To ?? payload.Length - 1;
            var bytes = payload.AsSpan(
                checked((int)start),
                checked((int)(end - start + 1))).ToArray();
            var content = new StreamContent(new GatedReadStream(
                bytes,
                releaseBodies.Task,
                () =>
                {
                    var current = Interlocked.Increment(ref active);
                    UpdateMaximum(ref maximumActive, current);
                    if (current >= 1)
                        oneActive.TrySetResult(true);
                    if (current >= 4)
                        fourActive.TrySetResult(true);
                    if (current >= 8)
                        eightActive.TrySetResult(true);
                },
                () => Interlocked.Decrement(ref active)));
            content.Headers.ContentLength = bytes.Length;
            content.Headers.ContentRange = new ContentRangeHeaderValue(start, end, payload.Length);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                RequestMessage = request,
                Content = content
            });
        });
        using var client = CreateClient(handler);
        var limiter = new ImportConcurrencyLimiter();
        limiter.SetMaximumDownloadConcurrency(8);
        var blockers = new IDisposable?[7];
        var destination = Path.Combine(TempRoot, "dynamic-expansion.jar");

        try
        {
            for (var index = 0; index < blockers.Length; index++)
                blockers[index] = await limiter.AcquireRuntimeDownloadSlotAsync();

            var download = CreateExecutor(client, limiter).DownloadFileAsync(
                DownloadUrl,
                DownloadSourcePreference.Official,
                "ThirdParty",
                destination,
                Sha1(payload),
                payload.Length,
                CancellationToken.None,
                (attempt, bytes, _) => progress.Enqueue(bytes));

            await oneActive.Task.WaitAsync(TimeSpan.FromSeconds(5));
            for (var index = 0; index < 3; index++)
            {
                blockers[index]!.Dispose();
                blockers[index] = null;
            }
            await fourActive.Task.WaitAsync(TimeSpan.FromSeconds(5));

            for (var index = 3; index < blockers.Length; index++)
            {
                blockers[index]!.Dispose();
                blockers[index] = null;
            }
            await eightActive.Task.WaitAsync(TimeSpan.FromSeconds(5));

            releaseBodies.TrySetResult(true);
            await download.WaitAsync(TimeSpan.FromSeconds(15));

            Assert.Equal(8, maximumActive);
            Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
            var samples = progress.ToArray();
            Assert.NotEmpty(samples);
            Assert.Equal(payload.Length, samples[^1]);
            Assert.All(samples, value => Assert.InRange(value, 0, payload.Length));
            Assert.True(samples.SequenceEqual(samples.Order()), "Progress must remain monotonic.");

            var closedRanges = handler.RangeHeaders
                .Where(value => value is not null)
                .Select(value => RangeHeaderValue.Parse(value!).Ranges.Single())
                .Where(range => range.To is not null && range.From != 0)
                .ToArray();
            Assert.True(closedRanges.Length >= 8);
            var original = closedRanges[0];
            Assert.Contains(
                closedRanges.Skip(1),
                range => range.From > original.From && range.From <= original.To);
            Assert.Equal(0, limiter.DownloadSnapshot.ActiveCount);
            Assert.Equal(0, limiter.DownloadSnapshot.WaitingCount);
        }
        finally
        {
            releaseBodies.TrySetResult(true);
            foreach (var blocker in blockers)
                blocker?.Dispose();
        }
    }

    [Fact]
    public async Task QueuedOrdinaryDownloadGetsReleasedSlotBeforeLargeFileExpands()
    {
        var payload = CreatePayload(12 * 1024 * 1024);
        var releaseBodies = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oneActive = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var twoActive = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var handler = new RecordingRequestHandler((_, request, _) =>
        {
            var range = Assert.Single(request.Headers.Range!.Ranges);
            if (range.From == 0)
                return Task.FromResult(CreatePartialResponse(request, payload));

            var start = range.From!.Value;
            var end = range.To ?? payload.Length - 1;
            var bytes = payload.AsSpan(
                checked((int)start),
                checked((int)(end - start + 1))).ToArray();
            var content = new StreamContent(new GatedReadStream(
                bytes,
                releaseBodies.Task,
                () =>
                {
                    var current = Interlocked.Increment(ref active);
                    if (current >= 1)
                        oneActive.TrySetResult(true);
                    if (current >= 2)
                        twoActive.TrySetResult(true);
                },
                () => Interlocked.Decrement(ref active)));
            content.Headers.ContentLength = bytes.Length;
            content.Headers.ContentRange = new ContentRangeHeaderValue(start, end, payload.Length);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                RequestMessage = request,
                Content = content
            });
        });
        using var client = CreateClient(handler);
        var limiter = new ImportConcurrencyLimiter();
        limiter.SetMaximumDownloadConcurrency(8);
        var blockers = new IDisposable?[7];
        IDisposable? ordinaryLease = null;
        var destination = Path.Combine(TempRoot, "ordinary-priority.jar");

        try
        {
            for (var index = 0; index < blockers.Length; index++)
                blockers[index] = await limiter.AcquireRuntimeDownloadSlotAsync();

            var download = CreateExecutor(client, limiter).DownloadFileAsync(
                DownloadUrl,
                DownloadSourcePreference.Official,
                "ThirdParty",
                destination,
                Sha1(payload),
                payload.Length,
                CancellationToken.None);

            await oneActive.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var ordinaryWaiter = limiter.AcquireRuntimeDownloadSlotAsync().AsTask();
            Assert.False(ordinaryWaiter.IsCompleted);
            Assert.Equal(1, limiter.DownloadSnapshot.WaitingCount);

            blockers[0]!.Dispose();
            blockers[0] = null;
            ordinaryLease = await ordinaryWaiter.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(
                MinecraftDownloadRequestExecutor.SegmentedExpansionScanInterval * 3);

            Assert.Equal(1, Volatile.Read(ref active));
            Assert.False(twoActive.Task.IsCompleted);

            ordinaryLease.Dispose();
            ordinaryLease = null;
            await twoActive.Task.WaitAsync(TimeSpan.FromSeconds(5));

            releaseBodies.TrySetResult(true);
            for (var index = 1; index < blockers.Length; index++)
            {
                blockers[index]!.Dispose();
                blockers[index] = null;
            }
            await download.WaitAsync(TimeSpan.FromSeconds(15));

            Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
            Assert.Equal(0, limiter.DownloadSnapshot.ActiveCount);
            Assert.Equal(0, limiter.DownloadSnapshot.WaitingCount);
        }
        finally
        {
            releaseBodies.TrySetResult(true);
            ordinaryLease?.Dispose();
            foreach (var blocker in blockers)
                blocker?.Dispose();
        }
    }

    [Fact]
    public async Task LightweightAtomicLargeFileKeepsSingleStreamPath()
    {
        var handler = FullResponseHandler(LargePayload);
        using var client = CreateClient(handler);
        var executor = CreateExecutor(client);
        var destination = Path.Combine(TempRoot, "lightweight.jar");

        await executor.DownloadFileAsync(
            DownloadUrl,
            DownloadSourcePreference.Official,
            "ThirdParty",
            destination,
            Sha1(LargePayload),
            LargePayload.Length,
            CancellationToken.None,
            options: new DownloadFileOptions(
                DownloadPersistenceMode.LightweightAtomic,
                ManagedRoot: TempRoot));

        Assert.Equal([null], handler.RangeHeaders);
    }

    [Fact]
    public async Task BmclApiCandidateKeepsExistingSingleStreamPolicy()
    {
        const string bmclUrl = "https://bmclapi2.bangbang93.com/custom/client.jar";
        var handler = FullResponseHandler(LargePayload);
        using var client = CreateClient(handler);
        var destination = Path.Combine(TempRoot, "bmcl.jar");

        await CreateExecutor(client).DownloadFileAsync(
            bmclUrl,
            DownloadSourcePreference.BmclApi,
            "ThirdParty",
            destination,
            Sha1(LargePayload),
            LargePayload.Length,
            CancellationToken.None);

        Assert.Equal([null], handler.RangeHeaders);
        Assert.Equal(LargePayload, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task CancellationCleansTemporaryFileAndReleasesAllDownloadSlots()
    {
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new RecordingRequestHandler((_, request, _) =>
        {
            started.TrySetResult(true);
            return Task.FromResult(CreateBlockingPartialResponse(request, LargePayload.Length));
        });
        using var client = CreateClient(handler);
        var limiter = new ImportConcurrencyLimiter();
        limiter.SetMaximumDownloadConcurrency(4);
        var executor = CreateExecutor(client, limiter);
        var destination = Path.Combine(TempRoot, "cancelled.jar");
        using var cancellation = new CancellationTokenSource();

        var download = executor.DownloadFileAsync(
            DownloadUrl,
            DownloadSourcePreference.Official,
            "ThirdParty",
            destination,
            Sha1(LargePayload),
            LargePayload.Length,
            cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download);
        Assert.Equal(0, limiter.DownloadSnapshot.ActiveCount);
        Assert.Equal(0, limiter.DownloadSnapshot.WaitingCount);
        Assert.False(File.Exists(destination));
        AssertNoPendingFiles(destination);
    }

    [Fact]
    public async Task MultipleLargeFilesCanUseSegmentedDownloadsAtTheSameTime()
    {
        const string firstUrl = "https://first.example.test/client.jar";
        const string secondUrl = "https://second.example.test/client.jar";
        var bothProbesArrived = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBodies = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstClosedRange = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondClosedRange = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var probeCount = 0;
        var handler = new RecordingRequestHandler(async (_, request, cancellationToken) =>
        {
            var range = Assert.Single(request.Headers.Range!.Ranges);
            if (range.To is null)
            {
                if (Interlocked.Increment(ref probeCount) == 2)
                    bothProbesArrived.TrySetResult(true);
                await bothProbesArrived.Task.WaitAsync(cancellationToken);
                return CreatePartialResponse(
                    request,
                    LargePayload,
                    maximumBytes: checked((int)MinecraftDownloadRequestExecutor.MinimumSegmentedChunkSize));
            }

            var start = range.From!.Value;
            var end = range.To.Value;
            var bytes = LargePayload.AsSpan(
                checked((int)start),
                checked((int)(end - start + 1))).ToArray();
            var started = request.RequestUri!.Host == "first.example.test"
                ? firstClosedRange
                : secondClosedRange;
            var content = new StreamContent(new GatedReadStream(
                bytes,
                releaseBodies.Task,
                () => started.TrySetResult(true),
                () => { }));
            content.Headers.ContentLength = bytes.Length;
            content.Headers.ContentRange = new ContentRangeHeaderValue(
                start,
                end,
                LargePayload.Length);
            return new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                RequestMessage = request,
                Content = content
            };
        });
        using var client = CreateClient(handler);
        var executor = CreateExecutor(client);
        var firstDestination = Path.Combine(TempRoot, "first.jar");
        var secondDestination = Path.Combine(TempRoot, "second.jar");

        var firstDownload = executor.DownloadFileAsync(
            firstUrl,
            DownloadSourcePreference.Official,
            "ThirdParty",
            firstDestination,
            Sha1(LargePayload),
            LargePayload.Length,
            CancellationToken.None);
        var secondDownload = executor.DownloadFileAsync(
            secondUrl,
            DownloadSourcePreference.Official,
            "ThirdParty",
            secondDestination,
            Sha1(LargePayload),
            LargePayload.Length,
            CancellationToken.None);

        await Task.WhenAll(
            firstClosedRange.Task.WaitAsync(TimeSpan.FromSeconds(5)),
            secondClosedRange.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        releaseBodies.TrySetResult(true);
        await Task.WhenAll(firstDownload, secondDownload).WaitAsync(TimeSpan.FromSeconds(15));

        var groupedRequests = handler.Requests
            .GroupBy(request => request.Host)
            .ToDictionary(group => group.Key, group => group.ToArray());
        Assert.All(
            new[] { "first.example.test", "second.example.test" },
            host =>
            {
                Assert.True(groupedRequests[host].Length > 1);
                Assert.Contains(groupedRequests[host], request => request.Range == "bytes=0-");
                Assert.Contains(groupedRequests[host], request => request.Range?.Contains('-') == true
                    && request.Range != "bytes=0-");
            });
        Assert.Equal(LargePayload, await File.ReadAllBytesAsync(firstDestination));
        Assert.Equal(LargePayload, await File.ReadAllBytesAsync(secondDestination));
        AssertNoPendingFiles(firstDestination);
        AssertNoPendingFiles(secondDestination);
    }

    [Fact]
    public void FairCoordinatorDistributesEightWorkersAcrossThreeFiles()
    {
        var coordinator = new SegmentedDownloadCoordinator();
        var global = new SegmentedGlobalConcurrencySnapshot(0, 0, 8);
        var host = new DownloadHostConcurrencySnapshot(
            "https://downloads.example.test:443",
            ActiveCount: 0,
            WaitingCount: 0,
            CurrentTarget: 8,
            ConfiguredMaximum: 8);
        using var first = coordinator.Register(host.Origin, 16, () => global, _ => host);
        using var second = coordinator.Register(host.Origin, 16, () => global, _ => host);
        using var third = coordinator.Register(host.Origin, 16, () => global, _ => host);

        var registrations = new[] { first, second, third };
        foreach (var registration in registrations)
        {
            while (registration.TryReserveAdditionalWorker(out _))
            {
                registration.ConfirmAdditionalWorkerActivated();
            }
        }

        var workers = registrations.Select(registration => registration.Snapshot.LiveWorkerCount).ToArray();
        Assert.Equal(8, workers.Sum());
        Assert.InRange(workers.Max() - workers.Min(), 0, 1);

        var thirdWorkerCount = third.Snapshot.LiveWorkerCount;
        for (var index = 1; index < thirdWorkerCount; index++)
            third.ReleaseAdditionalWorker(retirementReserved: false);
        third.ReleaseBaselineWorker();
        third.Dispose();

        while (first.TryReserveAdditionalWorker(out _))
        {
            first.ConfirmAdditionalWorkerActivated();
        }
        while (second.TryReserveAdditionalWorker(out _))
        {
            second.ConfirmAdditionalWorkerActivated();
        }
        Assert.Equal(4, first.Snapshot.LiveWorkerCount);
        Assert.Equal(4, second.Snapshot.LiveWorkerCount);

        foreach (var registration in new[] { first, second })
        {
            var workerCount = registration.Snapshot.LiveWorkerCount;
            for (var index = 1; index < workerCount; index++)
                registration.ReleaseAdditionalWorker(retirementReserved: false);
            registration.ReleaseBaselineWorker();
        }
    }

    [Fact]
    public void FairCoordinatorSkipsAFullHostAndUsesCapacityOnAnotherHost()
    {
        var coordinator = new SegmentedDownloadCoordinator();
        var global = new SegmentedGlobalConcurrencySnapshot(0, 0, 8);
        var hostA = new DownloadHostConcurrencySnapshot(
            "https://a.example.test:443",
            ActiveCount: 0,
            WaitingCount: 0,
            CurrentTarget: 2,
            ConfiguredMaximum: 8);
        var hostB = new DownloadHostConcurrencySnapshot(
            "https://b.example.test:443",
            ActiveCount: 0,
            WaitingCount: 0,
            CurrentTarget: 8,
            ConfiguredMaximum: 8);
        DownloadHostConcurrencySnapshot GetHost(string origin) =>
            origin == hostA.Origin ? hostA : hostB;

        using var firstA = coordinator.Register(hostA.Origin, 16, () => global, GetHost);
        using var secondA = coordinator.Register(hostA.Origin, 16, () => global, GetHost);
        using var fileB = coordinator.Register(hostB.Origin, 16, () => global, GetHost);
        var registrations = new[] { firstA, secondA, fileB };
        foreach (var registration in registrations)
        {
            while (registration.TryReserveAdditionalWorker(out _))
            {
                registration.ConfirmAdditionalWorkerActivated();
            }
        }

        Assert.Equal(1, firstA.Snapshot.LiveWorkerCount);
        Assert.Equal(1, secondA.Snapshot.LiveWorkerCount);
        Assert.Equal(6, fileB.Snapshot.LiveWorkerCount);
        Assert.Equal(8, registrations.Sum(registration => registration.Snapshot.LiveWorkerCount));

        foreach (var registration in registrations)
        {
            var workerCount = registration.Snapshot.LiveWorkerCount;
            for (var index = 1; index < workerCount; index++)
                registration.ReleaseAdditionalWorker(retirementReserved: false);
            registration.ReleaseBaselineWorker();
        }
    }

    [Fact]
    public void CanceledWorkerReservationDoesNotCountAsAnActivatedWorker()
    {
        var coordinator = new SegmentedDownloadCoordinator();
        var global = new SegmentedGlobalConcurrencySnapshot(0, 0, 8);
        var host = new DownloadHostConcurrencySnapshot(
            "https://downloads.example.test:443",
            ActiveCount: 0,
            WaitingCount: 0,
            CurrentTarget: 8,
            ConfiguredMaximum: 8);
        using var registration = coordinator.Register(
            host.Origin,
            16,
            () => global,
            _ => host);

        Assert.True(registration.TryReserveAdditionalWorker(out _));
        registration.CancelAdditionalWorkerReservation();

        var snapshot = registration.Snapshot;
        Assert.Equal(1, snapshot.LiveWorkerCount);
        Assert.Equal(0, snapshot.AdditionalWorkersGranted);
        Assert.Equal(0, snapshot.AdditionalWorkersReturned);
        registration.ReleaseBaselineWorker();
    }

    [Fact]
    public void AdditionalWorkerTakesQueuedRangeBeforeCreatingAnotherSplit()
    {
        var session = new AdaptiveSegmentDownloadSession(
            start: 0,
            totalLength: 8 * 1024 * 1024,
            MinecraftDownloadRequestExecutor.MinimumSegmentedChunkSize);
        Assert.True(session.TrySplitLargest(out _));
        var chunkCountBeforeTake = session.TotalChunkCount;

        Assert.True(session.TryTakeQueuedOrSplit(out var segment, out var split));

        Assert.NotNull(segment);
        Assert.Null(split);
        Assert.Equal(chunkCountBeforeTake, session.TotalChunkCount);
        session.Return(segment!);
    }

    private static MinecraftDownloadRequestExecutor CreateExecutor(
        HttpClient client,
        ImportConcurrencyLimiter? limiter = null,
        int maxAttemptsPerSource = 1)
    {
        var effectiveLimiter = limiter ?? new ImportConcurrencyLimiter();
        if (limiter is null)
            effectiveLimiter.SetMaximumDownloadConcurrency(64);
        return new MinecraftDownloadRequestExecutor(
            client,
            limiter: effectiveLimiter,
            category: DownloadConcurrencyCategory.Runtime,
            retryOptions: new DownloadRetryOptions
            {
                MaxAttemptsPerSource = maxAttemptsPerSource,
                MaxFileSourceRounds = 1,
                RetryDelay = TimeSpan.Zero,
                ResponseHeadersTimeout = TimeSpan.FromSeconds(5),
                FirstByteTimeout = TimeSpan.FromSeconds(5),
                BodyIdleTimeout = TimeSpan.FromSeconds(5),
                MaxRedirects = 10
            },
            hostConcurrencyController: new DownloadHostConcurrencyController(
                maximumJitter: TimeSpan.Zero,
                nextJitter: () => 0,
                delayAsync: static (_, _) => ValueTask.CompletedTask),
            bmclApiRequestRateLimiter: new BmclApiRequestRateLimiter(TimeSpan.Zero),
            nextRetryJitter: () => 0,
            segmentedDownloadCoordinator: new SegmentedDownloadCoordinator());
    }

    private static RecordingRequestHandler FullResponseHandler(byte[] payload) =>
        new((_, request, _) => Task.FromResult(CreateFullResponse(request, payload)));

    private static HttpClient CreateClient(HttpMessageHandler handler) =>
        new(handler) { Timeout = Timeout.InfiniteTimeSpan };

    private static HttpResponseMessage CreateFullResponse(HttpRequestMessage request, byte[] payload) =>
        new(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new ByteArrayContent(payload)
        };

    private static HttpResponseMessage CreatePartialResponse(
        HttpRequestMessage request,
        byte[] payload,
        bool corrupt = false,
        int? maximumBytes = null)
    {
        var range = Assert.Single(request.Headers.Range!.Ranges);
        var start = range.From!.Value;
        var requestedEnd = range.To ?? payload.Length - 1;
        var end = maximumBytes.HasValue
            ? Math.Min(requestedEnd, start + maximumBytes.Value - 1)
            : requestedEnd;
        var bytes = payload.AsSpan(
            checked((int)start),
            checked((int)(end - start + 1))).ToArray();
        if (corrupt)
            bytes[0] ^= 0xFF;
        var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
        {
            RequestMessage = request,
            Content = new ByteArrayContent(bytes)
        };
        response.Content.Headers.ContentLength = bytes.Length;
        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, end, payload.Length);
        return response;
    }

    private static HttpResponseMessage CreateBlockingPartialResponse(HttpRequestMessage request, long totalLength)
    {
        var range = Assert.Single(request.Headers.Range!.Ranges);
        var start = range.From!.Value;
        var end = range.To ?? totalLength - 1;
        var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
        {
            RequestMessage = request,
            Content = new StreamContent(new BlockingReadStream())
        };
        response.Content.Headers.ContentLength = end - start + 1;
        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, end, totalLength);
        return response;
    }

    private static void AssertRangesCoverPayload(IEnumerable<string?> values, long totalLength)
    {
        var ranges = values
            .Select(value => RangeHeaderValue.Parse(value!).Ranges.Single())
            .ToArray();
        Assert.True(ranges.Length > 4);
        var probe = Assert.Single(ranges.Where(range => range.To is null));
        Assert.Equal(0, probe.From);
        long next = MinecraftDownloadRequestExecutor.MinimumSegmentedChunkSize;
        foreach (var range in ranges.Where(range => range.To is not null).OrderBy(range => range.From))
        {
            Assert.True(
                range.From <= next,
                $"Range {range.From}-{range.To} leaves a gap after byte {next - 1}.");
            next = Math.Max(next, range.To!.Value + 1);
        }
        Assert.Equal(totalLength, next);
    }

    private static void AssertNoPendingFiles(string destination)
    {
        var directory = Path.GetDirectoryName(destination)!;
        Assert.Empty(Directory.Exists(directory)
            ? Directory.EnumerateFiles(
                directory,
                $".{Path.GetFileName(destination)}.bhl-pending-*.tmp",
                SearchOption.TopDirectoryOnly)
            : []);
    }

    private static string Sha1(byte[] payload) =>
        Convert.ToHexString(SHA1.HashData(payload));

    private static string Sha512(byte[] payload) =>
        Convert.ToHexString(SHA512.HashData(payload));

    private static byte[] CreatePayload(int length)
    {
        var payload = new byte[length];
        for (var index = 0; index < payload.Length; index++)
            payload[index] = (byte)(index % 251);
        return payload;
    }

    private sealed class RecordingRequestHandler(
        Func<int, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback)
        : HttpMessageHandler
    {
        private int requestCount;
        public int RequestCount => Volatile.Read(ref requestCount);
        public ConcurrentQueue<string?> RangeHeaders { get; } = new();
        public ConcurrentQueue<RequestSnapshot> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RangeHeaders.Enqueue(request.Headers.Range?.ToString());
            Requests.Enqueue(new RequestSnapshot(
                request.RequestUri!.Host,
                request.Headers.Range?.ToString(),
                request.Headers.Contains("x-api-key"),
                request.Headers.TryGetValues("If-Range", out var values) ? values.Single() : null));
            return callback(Interlocked.Increment(ref requestCount), request, cancellationToken);
        }
    }

    private static void UpdateMaximum(ref int maximum, int value)
    {
        while (true)
        {
            var current = Volatile.Read(ref maximum);
            if (current >= value || Interlocked.CompareExchange(ref maximum, value, current) == current)
                return;
        }
    }

    private sealed record RequestSnapshot(
        string Host,
        string? Range,
        bool HasSensitiveHeader,
        string? IfRange);

    private sealed class BlockingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class InterruptingReadStream(byte[] bytes, int interruptAfter) : Stream
    {
        private int position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => bytes.Length;
        public override long Position { get => position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (position >= interruptAfter)
                throw new IOException("simulated body interruption");
            var count = Math.Min(buffer.Length, Math.Min(interruptAfter - position, bytes.Length - position));
            bytes.AsMemory(position, count).CopyTo(buffer);
            position += count;
            return ValueTask.FromResult(count);
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class GatedReadStream(
        byte[] bytes,
        Task release,
        Action started,
        Action completed) : Stream
    {
        private int position;
        private int startedFlag;
        private int completedFlag;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => bytes.Length;
        public override long Position { get => position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref startedFlag, 1) == 0)
                started();
            await release.WaitAsync(cancellationToken);
            if (position >= bytes.Length)
                return 0;
            var count = Math.Min(buffer.Length, bytes.Length - position);
            bytes.AsMemory(position, count).CopyTo(buffer);
            position += count;
            return count;
        }
        protected override void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref completedFlag, 1) == 0 && Volatile.Read(ref startedFlag) != 0)
                completed();
            base.Dispose(disposing);
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
