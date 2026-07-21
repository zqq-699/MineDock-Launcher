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
    public void DefaultRequestIntervalIsTenMilliseconds()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(10), BmclApiRequestRateLimiter.DefaultRequestInterval);
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

    private sealed class NoopLease : IImportConcurrencyLease
    {
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

}
