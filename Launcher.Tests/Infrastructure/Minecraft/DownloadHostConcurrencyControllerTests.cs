using System.Net;
using System.Net.Http;
using Launcher.Application.Services;
using Launcher.Infrastructure.Minecraft;
using Launcher.Infrastructure.Modpacks;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class DownloadHostConcurrencyControllerTests
{
    private static readonly Uri CurseForgeUri = new("https://edge.forgecdn.net/file.jar");
    private static readonly Uri MojangUri = new("https://piston-data.mojang.com/file.jar");

    [Fact]
    public async Task RateLimitReducesOnlyTheAffectedHost()
    {
        var clock = new TestTimeProvider();
        var controller = CreateController(clock);
        await CreateHostAsync(controller, CurseForgeUri);
        await CreateHostAsync(controller, MojangUri);

        var adjustment = controller.RecordResult(
            CurseForgeUri,
            DownloadFailureReason.HttpStatus,
            HttpStatusCode.TooManyRequests);

        Assert.NotNull(adjustment);
        Assert.Equal(32, controller.GetSnapshot(Origin(CurseForgeUri)).CurrentTarget);
        Assert.Equal(64, controller.GetSnapshot(Origin(MojangUri)).CurrentTarget);
    }

    [Fact]
    public async Task PermanentStatusesAndOneTransientFailureDoNotReduceHost()
    {
        var clock = new TestTimeProvider();
        var controller = CreateController(clock);
        await CreateHostAsync(controller, CurseForgeUri);

        controller.RecordResult(CurseForgeUri, DownloadFailureReason.HttpStatus, HttpStatusCode.Forbidden);
        controller.RecordResult(CurseForgeUri, DownloadFailureReason.HttpStatus, HttpStatusCode.NotFound);
        controller.RecordResult(CurseForgeUri, DownloadFailureReason.Network);
        clock.Advance(TimeSpan.FromSeconds(20));
        controller.RecordResult(CurseForgeUri, failureReason: null);

        Assert.Equal(64, controller.GetSnapshot(Origin(CurseForgeUri)).CurrentTarget);
    }

    [Fact]
    public async Task CongestionThresholdRequiresThreeFailuresAndTwentyFivePercent()
    {
        var clock = new TestTimeProvider();
        var controller = CreateController(clock);
        await CreateHostAsync(controller, CurseForgeUri);

        for (var index = 0; index < 5; index++)
            controller.RecordResult(CurseForgeUri, failureReason: null);
        controller.RecordResult(CurseForgeUri, DownloadFailureReason.Network);
        controller.RecordResult(CurseForgeUri, DownloadFailureReason.BodyInterrupted);
        clock.Advance(TimeSpan.FromSeconds(20));
        var adjustment = controller.RecordResult(CurseForgeUri, DownloadFailureReason.ResponseHeadersTimeout);

        Assert.NotNull(adjustment);
        Assert.Equal(DownloadHostAdjustmentReason.CongestionThreshold, adjustment.Reason);
        Assert.Equal(32, adjustment.CurrentTarget);
    }

    [Fact]
    public async Task HealthyBacklogDoublesHostTargetAfterTwentySeconds()
    {
        var clock = new TestTimeProvider();
        var controller = CreateController(clock);
        await CreateHostAsync(controller, CurseForgeUri);
        controller.RecordResult(
            CurseForgeUri,
            DownloadFailureReason.HttpStatus,
            HttpStatusCode.TooManyRequests);

        var leases = new List<DownloadHostConcurrencyController.DownloadAdmissionLease>();
        for (var index = 0; index < 32; index++)
        {
            leases.Add(await controller.AcquireAsync(
                CurseForgeUri,
                AcquireNoopGlobalAsync,
                applyColdStartJitter: false,
                CancellationToken.None));
        }

        try
        {
            var queued = controller.AcquireAsync(
                CurseForgeUri,
                AcquireNoopGlobalAsync,
                applyColdStartJitter: false,
                CancellationToken.None).AsTask();
            Assert.True(SpinWait.SpinUntil(
                () => controller.GetSnapshot(Origin(CurseForgeUri)).WaitingCount == 1,
                TimeSpan.FromSeconds(1)));

            for (var index = 0; index < 31; index++)
                controller.RecordResult(CurseForgeUri, failureReason: null);
            clock.Advance(TimeSpan.FromSeconds(20));
            var adjustment = controller.RecordResult(CurseForgeUri, failureReason: null);

            Assert.NotNull(adjustment);
            Assert.Equal(DownloadHostAdjustmentReason.HealthyRecovery, adjustment.Reason);
            Assert.Equal(64, adjustment.CurrentTarget);
            await using var released = await queued.WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally
        {
            foreach (var lease in leases)
                await lease.DisposeAsync();
        }
    }

    [Fact]
    public async Task ConcurrentColdStartAddsConfiguredJitterAfterFirstRequest()
    {
        var delays = new List<TimeSpan>();
        var controller = new DownloadHostConcurrencyController(
            maximumJitter: TimeSpan.FromSeconds(2),
            nextJitter: () => 0.5,
            delayAsync: (delay, _) =>
            {
                delays.Add(delay);
                return ValueTask.CompletedTask;
            });

        await using var first = await controller.AcquireAsync(
            CurseForgeUri,
            AcquireNoopGlobalAsync,
            applyColdStartJitter: true,
            CancellationToken.None);
        await using var second = await controller.AcquireAsync(
            CurseForgeUri,
            AcquireNoopGlobalAsync,
            applyColdStartJitter: true,
            CancellationToken.None);

        Assert.Equal([TimeSpan.FromSeconds(1)], delays);
    }

    [Fact]
    public async Task CancellationDuringJitterReleasesHostWithoutTakingGlobalSlot()
    {
        var globalAcquireCount = 0;
        var controller = new DownloadHostConcurrencyController(
            maximumJitter: TimeSpan.FromSeconds(2),
            nextJitter: () => 0.5,
            delayAsync: static (_, token) => new ValueTask(Task.Delay(Timeout.InfiniteTimeSpan, token)));
        await using var first = await controller.AcquireAsync(
            CurseForgeUri,
            AcquireGlobalAsync,
            applyColdStartJitter: true,
            CancellationToken.None);
        using var cancellation = new CancellationTokenSource();

        var canceled = controller.AcquireAsync(
            CurseForgeUri,
            AcquireGlobalAsync,
            applyColdStartJitter: true,
            cancellation.Token).AsTask();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await canceled);
        Assert.Equal(1, controller.GetSnapshot(Origin(CurseForgeUri)).ActiveCount);
        Assert.Equal(1, globalAcquireCount);

        ValueTask<IImportConcurrencyLease> AcquireGlobalAsync(CancellationToken _)
        {
            Interlocked.Increment(ref globalAcquireCount);
            return AcquireNoopGlobalAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task IdleHostStateResetsToSixtyFourAfterFiveMinutes()
    {
        var clock = new TestTimeProvider();
        var controller = CreateController(clock);
        await CreateHostAsync(controller, CurseForgeUri);
        controller.RecordResult(
            CurseForgeUri,
            DownloadFailureReason.HttpStatus,
            HttpStatusCode.TooManyRequests);
        Assert.Equal(32, controller.GetSnapshot(Origin(CurseForgeUri)).CurrentTarget);

        clock.Advance(TimeSpan.FromMinutes(5));
        await CreateHostAsync(controller, CurseForgeUri);

        Assert.Equal(64, controller.GetSnapshot(Origin(CurseForgeUri)).CurrentTarget);
    }

    [Fact]
    public async Task RedirectReleasesIntermediateHostAndKeepsFinalLeaseUntilResultDisposal()
    {
        var controller = CreateController(new TestTimeProvider());
        var limiter = new ImportConcurrencyLimiter();
        using var httpClient = new HttpClient(new RedirectHandler());
        var transport = new MinecraftDownloadTransport(
            httpClient,
            new DownloadRetryOptions(),
            (uri, jitter, token) => controller.AcquireAsync(
                uri,
                limiter.AcquireRuntimeDownloadSlotAsync,
                jitter,
                token));

        var result = await transport.SendAsync(
            "https://first.example/file.jar",
            CancellationToken.None,
            applyColdStartJitter: true);
        try
        {
            Assert.Equal(0, controller.GetSnapshot("https://first.example:443").ActiveCount);
            Assert.Equal(1, controller.GetSnapshot("https://second.example:443").ActiveCount);
            Assert.Equal(1, limiter.DownloadSnapshot.ActiveCount);
        }
        finally
        {
            result.Response.Dispose();
            await result.DisposeAsync();
        }

        Assert.Equal(0, controller.GetSnapshot("https://second.example:443").ActiveCount);
        Assert.Equal(0, limiter.DownloadSnapshot.ActiveCount);
    }

    [Fact]
    public void RetryDelayAddsUpToTwoSecondsButPreservesRetryAfter()
    {
        var options = new DownloadRetryOptions
        {
            RetryDelay = TimeSpan.FromSeconds(1),
            MaximumRetryDelay = TimeSpan.FromSeconds(30)
        };
        using var httpClient = new HttpClient(new SuccessHandler());
        var executor = new MinecraftDownloadRequestExecutor(
            httpClient,
            retryOptions: options,
            nextRetryJitter: () => 0.5);
        var transient = new DownloadAttemptException(
            DownloadFailureDisposition.RetryCurrentSource,
            DownloadFailureReason.Network,
            "transient");
        var rateLimited = new DownloadAttemptException(
            DownloadFailureDisposition.RetryCurrentSource,
            DownloadFailureReason.HttpStatus,
            "rate limited",
            statusCode: HttpStatusCode.TooManyRequests,
            retryAfter: TimeSpan.FromSeconds(7));

        Assert.Equal(TimeSpan.FromSeconds(2), executor.GetRetryDelay(transient, attempt: 1));
        Assert.Equal(TimeSpan.FromSeconds(5), executor.GetRetryDelay(transient, attempt: 3));
        Assert.Equal(TimeSpan.FromSeconds(7), executor.GetRetryDelay(rateLimited, attempt: 1));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public void PermanentHttpStatusesAreNeutral(HttpStatusCode statusCode)
    {
        Assert.Equal(
            DownloadHostResultKind.Neutral,
            DownloadHostConcurrencyController.ClassifyResult(DownloadFailureReason.HttpStatus, statusCode));
    }

    private static DownloadHostConcurrencyController CreateController(TestTimeProvider clock) => new(
        timeProvider: clock,
        maximumJitter: TimeSpan.Zero,
        nextJitter: () => 0,
        delayAsync: static (_, _) => ValueTask.CompletedTask);

    private static async Task CreateHostAsync(DownloadHostConcurrencyController controller, Uri uri)
    {
        await using var lease = await controller.AcquireAsync(
            uri,
            AcquireNoopGlobalAsync,
            applyColdStartJitter: false,
            CancellationToken.None);
    }

    private static string Origin(Uri uri) => DownloadHostConcurrencyController.NormalizeOrigin(uri);

    private static ValueTask<IImportConcurrencyLease> AcquireNoopGlobalAsync(CancellationToken _) =>
        ValueTask.FromResult<IImportConcurrencyLease>(new NoopLease());

    private sealed class NoopLease : IImportConcurrencyLease
    {
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset utcNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => utcNow;
        public void Advance(TimeSpan duration) => utcNow += duration;
    }

    private sealed class RedirectHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host == "first.example")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
                {
                    RequestMessage = request,
                    Headers = { Location = new Uri("https://second.example/file.jar") }
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent("ok"u8.ToArray())
            });
        }
    }

    private sealed class SuccessHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent("ok"u8.ToArray())
            });
    }
}
