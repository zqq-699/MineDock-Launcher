using System.IO;
using System.Net.Http;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Launcher.Application.Services;

namespace Launcher.Infrastructure.Minecraft;

internal sealed class DownloadBandwidthLimiter
{
    private static readonly ConditionalWeakTable<IDownloadSpeedLimitState, SharedBudgetState> SharedStateBudgets = new();
    private static readonly ConcurrentDictionary<int, SharedBudgetState> FixedLimitBudgets = new();

    private readonly SharedBudgetState sharedBudgetState;
    private readonly int defaultDownloadSpeedLimitMbPerSecond;
    private readonly IDownloadSpeedLimitState? downloadSpeedLimitState;

    private DownloadBandwidthLimiter(
        SharedBudgetState sharedBudgetState,
        int defaultDownloadSpeedLimitMbPerSecond,
        IDownloadSpeedLimitState? downloadSpeedLimitState)
    {
        this.sharedBudgetState = sharedBudgetState;
        this.defaultDownloadSpeedLimitMbPerSecond = Math.Max(defaultDownloadSpeedLimitMbPerSecond, 0);
        this.downloadSpeedLimitState = downloadSpeedLimitState;
    }

    public static DownloadBandwidthLimiter? Create(
        int downloadSpeedLimitMbPerSecond,
        IDownloadSpeedLimitState? downloadSpeedLimitState = null)
    {
        var normalizedLimit = Math.Max(downloadSpeedLimitMbPerSecond, 0);
        var effectiveLimit = downloadSpeedLimitState?.DownloadSpeedLimitMbPerSecond ?? normalizedLimit;
        if (effectiveLimit <= 0)
            return null;

        if (downloadSpeedLimitState is not null)
        {
            var sharedBudgetState = SharedStateBudgets.GetValue(downloadSpeedLimitState, static _ => new SharedBudgetState());
            return new DownloadBandwidthLimiter(sharedBudgetState, downloadSpeedLimitMbPerSecond, downloadSpeedLimitState);
        }

        var fixedBudgetState = FixedLimitBudgets.GetOrAdd(normalizedLimit, static _ => new SharedBudgetState());
        return new DownloadBandwidthLimiter(fixedBudgetState, normalizedLimit, downloadSpeedLimitState: null);
    }

    public async ValueTask ThrottleAsync(int bytesRead, CancellationToken cancellationToken)
    {
        if (bytesRead <= 0)
            return;

        var bytesPerSecond = ResolveBytesPerSecond();
        if (bytesPerSecond <= 0)
            return;

        await sharedBudgetState.ThrottleAsync(bytesRead, bytesPerSecond, cancellationToken).ConfigureAwait(false);
    }

    private double ResolveBytesPerSecond()
    {
        var limitMbPerSecond = downloadSpeedLimitState?.DownloadSpeedLimitMbPerSecond
            ?? defaultDownloadSpeedLimitMbPerSecond;
        return limitMbPerSecond <= 0
            ? 0
            : limitMbPerSecond * 1024d * 1024d;
    }

    private sealed class SharedBudgetState
    {
        private readonly object syncRoot = new();
        private DateTime nextAvailableAtUtc = DateTime.UtcNow;

        public async ValueTask ThrottleAsync(int bytesRead, double bytesPerSecond, CancellationToken cancellationToken)
        {
            TimeSpan delay;
            lock (syncRoot)
            {
                var now = DateTime.UtcNow;
                var startAt = now > nextAvailableAtUtc ? now : nextAvailableAtUtc;
                delay = startAt - now;
                nextAvailableAtUtc = startAt.AddSeconds(bytesRead / bytesPerSecond);
            }

            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }
}

internal static class DownloadResponseThrottler
{
    public static async Task ApplyAsync(
        HttpResponseMessage response,
        DownloadBandwidthLimiter? bandwidthLimiter,
        CancellationToken cancellationToken,
        IAsyncDisposable? completionLease = null)
    {
        if (response.Content is null)
        {
            if (completionLease is not null)
                await completionLease.DisposeAsync().ConfigureAwait(false);
            return;
        }

        if (bandwidthLimiter is null && completionLease is null)
            return;

        var originalContent = response.Content;
        var originalStream = await originalContent.ReadAsStreamAsync(cancellationToken);
        var throttledContent = new StreamContent(
            new ThrottledReadStream(originalStream, originalContent, bandwidthLimiter, completionLease));
        foreach (var header in originalContent.Headers)
            throttledContent.Headers.TryAddWithoutValidation(header.Key, header.Value);

        response.Content = throttledContent;
    }

    private sealed class ThrottledReadStream : Stream
    {
        private readonly Stream innerStream;
        private readonly HttpContent ownedContent;
        private readonly DownloadBandwidthLimiter? bandwidthLimiter;
        private readonly IAsyncDisposable? completionLease;

        public ThrottledReadStream(
            Stream innerStream,
            HttpContent ownedContent,
            DownloadBandwidthLimiter? bandwidthLimiter,
            IAsyncDisposable? completionLease)
        {
            this.innerStream = innerStream;
            this.ownedContent = ownedContent;
            this.bandwidthLimiter = bandwidthLimiter;
            this.completionLease = completionLease;
        }

        public override bool CanRead => innerStream.CanRead;
        public override bool CanSeek => innerStream.CanSeek;
        public override bool CanWrite => innerStream.CanWrite;
        public override long Length => innerStream.Length;
        public override long Position
        {
            get => innerStream.Position;
            set => innerStream.Position = value;
        }

        public override void Flush()
        {
            innerStream.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = innerStream.Read(buffer, offset, count);
            if (read > 0 && bandwidthLimiter is not null)
                bandwidthLimiter.ThrottleAsync(read, CancellationToken.None).AsTask().GetAwaiter().GetResult();

            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            var read = innerStream.Read(buffer);
            if (read > 0 && bandwidthLimiter is not null)
                bandwidthLimiter.ThrottleAsync(read, CancellationToken.None).AsTask().GetAwaiter().GetResult();

            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await innerStream.ReadAsync(buffer, cancellationToken);
            if (read > 0 && bandwidthLimiter is not null)
                await bandwidthLimiter.ThrottleAsync(read, cancellationToken);

            return read;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var read = await innerStream.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
            if (read > 0 && bandwidthLimiter is not null)
                await bandwidthLimiter.ThrottleAsync(read, cancellationToken);

            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return innerStream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            innerStream.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            innerStream.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            innerStream.Write(buffer);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return innerStream.WriteAsync(buffer, cancellationToken);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return innerStream.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                innerStream.Dispose();
                ownedContent.Dispose();
                if (completionLease is not null)
                    completionLease.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await innerStream.DisposeAsync();
            ownedContent.Dispose();
            if (completionLease is not null)
                await completionLease.DisposeAsync().ConfigureAwait(false);
            await base.DisposeAsync();
        }
    }
}
