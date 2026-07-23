/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure;
using Launcher.Infrastructure.FileSystem;
using Launcher.Infrastructure.Resources;
using Launcher.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Tests.Infrastructure.FileSystem;

public sealed class RemoteThumbnailDownloadClientTests : TestTempDirectory
{
    [Fact]
    public async Task DownloadsAtMostThirtyTwoThumbnailsAndUsesGlobalMetadataLeases()
    {
        var handler = new BlockingThumbnailHandler(RemoteThumbnailDownloadClient.MaximumConcurrency);
        using var httpClient = new HttpClient(handler);
        var limiter = new RecordingLimiter();
        var client = new RemoteThumbnailDownloadClient(
            httpClient,
            limiter,
            downloadSpeedLimitState: null,
            NullLogger.Instance);

        var downloads = Enumerable.Range(0, 40)
            .Select(index => client.DownloadAsync(
                $"https://cdn.example.com/icons/{index}.png",
                maximumBytes: 1024,
                CancellationToken.None))
            .ToArray();

        await handler.MaximumStarted.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(32, handler.MaximumActiveRequests);
        Assert.Equal(32, limiter.MaximumActiveMetadataLeases);
        Assert.Equal(32, limiter.MetadataLeaseCount);

        handler.ReleaseAll();
        var results = await Task.WhenAll(downloads).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.All(results, bytes => Assert.Equal(BlockingThumbnailHandler.Payload, bytes));
        Assert.Equal(40, handler.RequestCount);
        Assert.Equal(40, limiter.MetadataLeaseCount);
        Assert.Equal(0, limiter.ModpackLeaseCount);
        Assert.Equal(0, limiter.RuntimeLeaseCount);
    }

    [Fact]
    public async Task LocalModEnrichmentStartsThumbnailDownloadsConcurrently()
    {
        Directory.CreateDirectory(TempRoot);
        var mods = Enumerable.Range(0, 2)
            .Select(index =>
            {
                var path = Path.Combine(TempRoot, $"mod-{index}.jar");
                File.WriteAllBytes(path, Encoding.UTF8.GetBytes($"mod-content-{index}"));
                return new LocalMod
                {
                    Name = $"Mod {index}",
                    FileName = Path.GetFileName(path),
                    FullPath = path,
                    IsEnabled = true
                };
            })
            .ToArray();
        var hashes = mods
            .Select(mod => Convert.ToHexString(SHA1.HashData(File.ReadAllBytes(mod.FullPath))).ToLowerInvariant())
            .ToArray();
        var handler = new LocalModIconHandler(hashes);
        using var httpClient = new HttpClient(handler);
        var service = new LocalModIconEnrichmentService(
            new LauncherPathProvider(TempRoot),
            httpClient,
            logger: NullLogger<LocalModIconEnrichmentService>.Instance);

        var enrichment = service.ResolveMissingIconSourcesAsync(mods);
        await handler.AllIconsStarted.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, handler.MaximumActiveIconRequests);

        handler.ReleaseIcons();
        var resolved = await enrichment.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(2, resolved.Count);
        Assert.All(mods, mod => Assert.True(resolved.ContainsKey(mod.FullPath)));
    }

    private sealed class BlockingThumbnailHandler(int expectedMaximum) : HttpMessageHandler
    {
        public static readonly byte[] Payload = [1, 2, 3, 4];

        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource maximumStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int activeRequests;
        private int maximumActiveRequests;
        private int requestCount;

        public Task MaximumStarted => maximumStarted.Task;

        public int MaximumActiveRequests => Volatile.Read(ref maximumActiveRequests);
        public int RequestCount => Volatile.Read(ref requestCount);

        public void ReleaseAll() => release.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref requestCount);
            var active = Interlocked.Increment(ref activeRequests);
            UpdateMaximum(ref maximumActiveRequests, active);
            if (active == expectedMaximum)
                maximumStarted.TrySetResult();

            try
            {
                await release.Task.WaitAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Payload)
                };
            }
            finally
            {
                Interlocked.Decrement(ref activeRequests);
            }
        }
    }

    private sealed class LocalModIconHandler(IReadOnlyList<string> hashes) : HttpMessageHandler
    {
        private static readonly byte[] IconPayload = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        private readonly TaskCompletionSource allIconsStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseIcons = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int activeIconRequests;
        private int maximumActiveIconRequests;

        public Task AllIconsStarted => allIconsStarted.Task;
        public int MaximumActiveIconRequests => Volatile.Read(ref maximumActiveIconRequests);

        public void ReleaseIcons() => releaseIcons.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (uri.Host.Equals("api.modrinth.com", StringComparison.OrdinalIgnoreCase)
                && uri.AbsolutePath.EndsWith("/version_files", StringComparison.OrdinalIgnoreCase))
            {
                var versions = hashes
                    .Select((hash, index) => new KeyValuePair<string, object>(
                        hash,
                        new Dictionary<string, string> { ["project_id"] = $"project-{index}" }))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
                return JsonResponse(versions);
            }

            if (uri.Host.Equals("api.modrinth.com", StringComparison.OrdinalIgnoreCase)
                && uri.AbsolutePath.EndsWith("/projects", StringComparison.OrdinalIgnoreCase))
            {
                var projects = hashes.Select((_, index) => new Dictionary<string, string>
                {
                    ["id"] = $"project-{index}",
                    ["icon_url"] = $"https://cdn.example.com/icons/{index}.png"
                });
                return JsonResponse(projects);
            }

            if (uri.Host.Equals("cdn.example.com", StringComparison.OrdinalIgnoreCase))
            {
                var active = Interlocked.Increment(ref activeIconRequests);
                UpdateMaximum(ref maximumActiveIconRequests, active);
                if (active == hashes.Count)
                    allIconsStarted.TrySetResult();
                try
                {
                    await releaseIcons.Task.WaitAsync(cancellationToken);
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(IconPayload)
                    };
                }
                finally
                {
                    Interlocked.Decrement(ref activeIconRequests);
                }
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage JsonResponse<T>(T value) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(value),
                Encoding.UTF8,
                "application/json")
        };
    }

    private sealed class RecordingLimiter : IImportConcurrencyLimiter
    {
        private int activeMetadataLeases;
        private int maximumActiveMetadataLeases;
        private int metadataLeaseCount;
        private int modpackLeaseCount;
        private int runtimeLeaseCount;
        private int activeDownloadLeases;

        public int MaximumActiveMetadataLeases => Volatile.Read(ref maximumActiveMetadataLeases);
        public int MetadataLeaseCount => Volatile.Read(ref metadataLeaseCount);
        public int ModpackLeaseCount => Volatile.Read(ref modpackLeaseCount);
        public int RuntimeLeaseCount => Volatile.Read(ref runtimeLeaseCount);
        public DownloadConcurrencySnapshot DownloadSnapshot =>
            new(Volatile.Read(ref activeDownloadLeases), WaitingCount: 0, CurrentTarget: 64);

        public bool TryAcquireAvailableDownloadSlot(out IImportConcurrencyLease? lease)
        {
            Interlocked.Increment(ref activeDownloadLeases);
            lease = new RecordingLease(() => Interlocked.Decrement(ref activeDownloadLeases));
            return true;
        }

        public ValueTask<IImportConcurrencyLease> AcquireMetadataSlotAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref metadataLeaseCount);
            var active = Interlocked.Increment(ref activeMetadataLeases);
            UpdateMaximum(ref maximumActiveMetadataLeases, active);
            return ValueTask.FromResult<IImportConcurrencyLease>(
                new RecordingLease(() => Interlocked.Decrement(ref activeMetadataLeases)));
        }

        public ValueTask<IImportConcurrencyLease> AcquireModpackDownloadSlotAsync(
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref modpackLeaseCount);
            Interlocked.Increment(ref activeDownloadLeases);
            return ValueTask.FromResult<IImportConcurrencyLease>(
                new RecordingLease(() => Interlocked.Decrement(ref activeDownloadLeases)));
        }

        public ValueTask<IImportConcurrencyLease> AcquireRuntimeDownloadSlotAsync(
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref runtimeLeaseCount);
            Interlocked.Increment(ref activeDownloadLeases);
            return ValueTask.FromResult<IImportConcurrencyLease>(
                new RecordingLease(() => Interlocked.Decrement(ref activeDownloadLeases)));
        }

        public ValueTask<IImportConcurrencyLease> AcquireHashSlotAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IImportConcurrencyLease>(new RecordingLease(static () => { }));
    }

    private sealed class RecordingLease(Action release) : IImportConcurrencyLease
    {
        private Action? release = release;

        public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke();

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private static void UpdateMaximum(ref int target, int value)
    {
        var current = Volatile.Read(ref target);
        while (value > current)
        {
            var observed = Interlocked.CompareExchange(ref target, value, current);
            if (observed == current)
                return;
            current = observed;
        }
    }
}
