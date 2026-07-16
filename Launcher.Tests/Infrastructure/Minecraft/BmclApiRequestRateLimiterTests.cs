using System.Net;
using System.Net.Http;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class BmclApiRequestRateLimiterTests
{
    private static readonly Uri BmclApiUri = new("https://bmclapi2.bangbang93.com/assets/aa/hash");

    [Fact]
    public async Task BmclApiRequestsAreReleasedAtConfiguredIntervals()
    {
        var clock = new TestTimeProvider();
        var delays = new List<TimeSpan>();
        var limiter = new BmclApiRequestRateLimiter(
            TimeSpan.FromMilliseconds(50),
            clock,
            (delay, _) =>
            {
                delays.Add(delay);
                clock.Advance(delay);
                return ValueTask.CompletedTask;
            });

        await limiter.WaitAsync(BmclApiUri, CancellationToken.None);
        await limiter.WaitAsync(BmclApiUri, CancellationToken.None);
        await limiter.WaitAsync(BmclApiUri, CancellationToken.None);

        Assert.Equal(
            [TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50)],
            delays);
    }

    [Theory]
    [InlineData("https://resources.download.minecraft.net/aa/hash")]
    [InlineData("https://node.example/assets/aa/hash")]
    public async Task NonBmclApiHostsBypassRateLimit(string url)
    {
        var delayCount = 0;
        var limiter = new BmclApiRequestRateLimiter(
            TimeSpan.FromMilliseconds(50),
            delayAsync: (_, _) =>
            {
                Interlocked.Increment(ref delayCount);
                return ValueTask.CompletedTask;
            });
        var uri = new Uri(url);

        await limiter.WaitAsync(uri, CancellationToken.None);
        await limiter.WaitAsync(uri, CancellationToken.None);

        Assert.Equal(0, delayCount);
    }

    [Fact]
    public async Task CancellationWhileWaitingDoesNotBlockTheNextRequest()
    {
        var delayStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDelay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var limiter = new BmclApiRequestRateLimiter(
            TimeSpan.FromMilliseconds(50),
            delayAsync: async (_, token) =>
            {
                delayStarted.TrySetResult();
                await releaseDelay.Task.WaitAsync(token);
            });
        await limiter.WaitAsync(BmclApiUri, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();

        var canceledRequest = limiter.WaitAsync(BmclApiUri, cancellation.Token).AsTask();
        await delayStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await canceledRequest);
        releaseDelay.TrySetResult();
        await limiter.WaitAsync(BmclApiUri, CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task BmclApiRetryWaitsBeforeTakingAnotherGlobalSlot()
    {
        var clock = new TestTimeProvider();
        var completedRateLimitDelays = 0;
        var rateLimiter = new BmclApiRequestRateLimiter(
            TimeSpan.FromMilliseconds(50),
            clock,
            (delay, _) =>
            {
                clock.Advance(delay);
                Interlocked.Increment(ref completedRateLimitDelays);
                return ValueTask.CompletedTask;
            });
        var globalLimiter = new RecordingImportConcurrencyLimiter(
            () => Volatile.Read(ref completedRateLimitDelays));
        using var httpClient = new HttpClient(new RetryOnceHandler());
        var executor = new MinecraftDownloadRequestExecutor(
            httpClient,
            limiter: globalLimiter,
            retryOptions: new DownloadRetryOptions
            {
                MaxAttemptsPerSource = 2,
                RetryDelay = TimeSpan.Zero,
                ResponseHeadersTimeout = TimeSpan.FromSeconds(1)
            },
            hostConcurrencyController: new DownloadHostConcurrencyController(
                maximumJitter: TimeSpan.Zero,
                nextJitter: () => 0,
                delayAsync: static (_, _) => ValueTask.CompletedTask),
            nextRetryJitter: () => 0,
            bmclApiRequestRateLimiter: rateLimiter);

        var result = await executor.ExecuteAsync(
            BmclApiUri.AbsoluteUri,
            DownloadSourcePreference.BmclApi,
            "Mojang",
            static (_, _) => Task.FromResult(true),
            CancellationToken.None);

        Assert.True(result);
        Assert.Equal([0, 1], globalLimiter.DelayCountsAtAcquisition);
    }

    [Fact]
    public void OnlyCanonicalBmclApiEntryIsRateLimited()
    {
        Assert.True(BmclApiRequestRateLimiter.IsBmclApiEntry(BmclApiUri));
        Assert.False(BmclApiRequestRateLimiter.IsBmclApiEntry(
            new Uri("https://minio.749333.xyz/assets/aa/hash")));
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset utcNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => utcNow;
        public void Advance(TimeSpan duration) => utcNow += duration;
    }

    private sealed class RecordingImportConcurrencyLimiter(Func<int> observeDelayCount)
        : IImportConcurrencyLimiter
    {
        public List<int> DelayCountsAtAcquisition { get; } = [];

        public ValueTask<IImportConcurrencyLease> AcquireMetadataSlotAsync(
            CancellationToken cancellationToken = default) => AcquireAsync();

        public ValueTask<IImportConcurrencyLease> AcquireModpackDownloadSlotAsync(
            CancellationToken cancellationToken = default) => AcquireAsync();

        public ValueTask<IImportConcurrencyLease> AcquireRuntimeDownloadSlotAsync(
            CancellationToken cancellationToken = default) => AcquireAsync();

        public ValueTask<IImportConcurrencyLease> AcquireHashSlotAsync(
            CancellationToken cancellationToken = default) => AcquireAsync();

        private ValueTask<IImportConcurrencyLease> AcquireAsync()
        {
            DelayCountsAtAcquisition.Add(observeDelayCount());
            return ValueTask.FromResult<IImportConcurrencyLease>(new NoopLease());
        }
    }

    private sealed class NoopLease : IImportConcurrencyLease
    {
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RetryOnceHandler : HttpMessageHandler
    {
        private int requestCount;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var statusCode = Interlocked.Increment(ref requestCount) == 1
                ? HttpStatusCode.InternalServerError
                : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                RequestMessage = request,
                Content = new ByteArrayContent("ok"u8.ToArray())
            });
        }
    }
}
