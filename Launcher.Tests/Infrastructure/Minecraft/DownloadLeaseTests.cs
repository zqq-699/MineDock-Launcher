using System.Net;
using System.Net.Http;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;
using Launcher.Infrastructure.Modpacks;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class DownloadRequestExecutorLeaseTests
{
    [Fact]
    public async Task DownloadRequestExecutorCancellationReleasesMetadataSlots()
    {
        var limiter = new ImportConcurrencyLimiter();
        var handler = new BlockingRequestHandler(expectedRequestCount: 2);
        using var httpClient = new HttpClient(handler);
        var executor = new MinecraftDownloadRequestExecutor(
            httpClient,
            limiter: limiter,
            category: DownloadConcurrencyCategory.Metadata,
            retryOptions: new DownloadRetryOptions
            {
                ResponseHeadersTimeout = TimeSpan.FromMinutes(1)
            });
        using var cancellation = new CancellationTokenSource();

        var requests = Enumerable.Range(0, 2)
            .Select(index => executor.ExecuteAsync(
                $"https://example.test/metadata/{index}.json",
                DownloadSourcePreference.Official,
                categoryHint: "Mojang",
                static (_, _) => Task.FromResult(true),
                cancellation.Token))
            .ToArray();

        await handler.WaitForExpectedRequestsAsync();
        cancellation.Cancel();

        foreach (var request in requests)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await request);

        await using var leases = await SlotLeaseSet.AcquireAsync(
            limiter.AcquireMetadataSlotAsync,
            slotCount: 2);
    }
}

public sealed class DownloadSourceRoutingLeaseTests
{
    [Fact]
    public async Task DownloadSourceRoutingCancellationReleasesRuntimeSlots()
    {
        var limiter = new ImportConcurrencyLimiter();
        var handler = new BlockingRequestHandler(expectedRequestCount: 8);
        using var httpClient = new HttpClient(new DownloadSourceRoutingHttpMessageHandler(
            DownloadSourcePreference.Official,
            DownloadConcurrencyCategory.Runtime,
            handler,
            limiter: limiter,
            retryOptions: new DownloadRetryOptions
            {
                ResponseHeadersTimeout = TimeSpan.FromMinutes(1)
            }));
        using var cancellation = new CancellationTokenSource();

        var requests = Enumerable.Range(0, 8)
            .Select(index => httpClient.GetAsync($"https://example.test/runtime/{index}.jar", cancellation.Token))
            .ToArray();

        await handler.WaitForExpectedRequestsAsync();
        cancellation.Cancel();

        foreach (var request in requests)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await request);

        await using var leases = await SlotLeaseSet.AcquireAsync(
            limiter.AcquireRuntimeDownloadSlotAsync,
            slotCount: 8);
    }

    [Fact]
    public async Task DownloadSourceRoutingFinalFailureReleasesRuntimeSlots()
    {
        var limiter = new ImportConcurrencyLimiter();
        using var httpClient = new HttpClient(new DownloadSourceRoutingHttpMessageHandler(
            DownloadSourcePreference.Official,
            DownloadConcurrencyCategory.Runtime,
            new ThrowingRequestHandler(),
            limiter: limiter,
            retryOptions: new DownloadRetryOptions
            {
                MaxAttemptsPerSource = 1,
                RetryDelay = TimeSpan.Zero
            }));

        foreach (var index in Enumerable.Range(0, 8))
        {
            await Assert.ThrowsAsync<DownloadAttemptException>(
                () => httpClient.GetAsync($"https://example.test/runtime/{index}.jar"));
        }

        await using var leases = await SlotLeaseSet.AcquireAsync(
            limiter.AcquireRuntimeDownloadSlotAsync,
            slotCount: 8);
    }
}

internal sealed class BlockingRequestHandler(int expectedRequestCount) : HttpMessageHandler
{
    private readonly TaskCompletionSource expectedRequestsArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int requestCount;

    public Task WaitForExpectedRequestsAsync()
    {
        return expectedRequestsArrived.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref requestCount) == expectedRequestCount)
            expectedRequestsArrived.TrySetResult();

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new ByteArrayContent("ok"u8.ToArray())
        };
    }
}

internal sealed class ThrowingRequestHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("download failed");
    }
}

internal sealed class SlotLeaseSet(IReadOnlyList<IAsyncDisposable> leases) : IAsyncDisposable
{
    public static async Task<SlotLeaseSet> AcquireAsync(
        Func<CancellationToken, ValueTask<IAsyncDisposable>> acquireAsync,
        int slotCount)
    {
        var leases = await Task.WhenAll(
                Enumerable.Range(0, slotCount)
                    .Select(_ => acquireAsync(CancellationToken.None).AsTask()))
            .WaitAsync(TimeSpan.FromSeconds(2));

        return new SlotLeaseSet(leases);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var lease in leases)
            await lease.DisposeAsync();
    }
}
