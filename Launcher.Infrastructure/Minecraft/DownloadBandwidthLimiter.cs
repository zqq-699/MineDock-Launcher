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

    public void Throttle(int bytesRead)
    {
        if (bytesRead <= 0)
            return;

        var bytesPerSecond = ResolveBytesPerSecond();
        if (bytesPerSecond > 0)
            sharedBudgetState.Throttle(bytesRead, bytesPerSecond);
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
            var delay = Reserve(bytesRead, bytesPerSecond);
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        public void Throttle(int bytesRead, double bytesPerSecond)
        {
            var delay = Reserve(bytesRead, bytesPerSecond);
            if (delay > TimeSpan.Zero)
                Thread.Sleep(delay);
        }

        private TimeSpan Reserve(int bytesRead, double bytesPerSecond)
        {
            lock (syncRoot)
            {
                var now = DateTime.UtcNow;
                var startAt = now > nextAvailableAtUtc ? now : nextAvailableAtUtc;
                var delay = startAt - now;
                nextAvailableAtUtc = startAt.AddSeconds(bytesRead / bytesPerSecond);
                return delay;
            }
        }
    }
}

internal static class DownloadResponseThrottler
{
    public static async Task ApplyAsync(
        HttpResponseMessage response,
        DownloadBandwidthLimiter? bandwidthLimiter,
        CancellationToken cancellationToken,
        IImportConcurrencyLease? completionLease = null,
        TimeSpan? bodyIdleTimeout = null)
    {
        if (response.Content is null)
        {
            if (completionLease is not null)
                await completionLease.DisposeAsync().ConfigureAwait(false);
            return;
        }

        if (bandwidthLimiter is null && completionLease is null && bodyIdleTimeout is null)
            return;

        var originalContent = response.Content;
        var originalStream = await originalContent.ReadAsStreamAsync(cancellationToken);
        Stream networkStream = bodyIdleTimeout is { } idleTimeout
            ? new IdleTimeoutReadStream(originalStream, idleTimeout, cancellationToken)
            : originalStream;
        var throttledContent = new StreamContent(
            new ThrottledReadStream(networkStream, originalContent, bandwidthLimiter, completionLease));
        foreach (var header in originalContent.Headers)
            throttledContent.Headers.TryAddWithoutValidation(header.Key, header.Value);

        response.Content = throttledContent;
    }

    private sealed class IdleTimeoutReadStream : Stream
    {
        private readonly Stream innerStream;
        private readonly TimeSpan idleTimeout;
        private readonly CancellationToken operationCancellationToken;

        public IdleTimeoutReadStream(
            Stream innerStream,
            TimeSpan idleTimeout,
            CancellationToken operationCancellationToken)
        {
            this.innerStream = innerStream;
            this.idleTimeout = idleTimeout;
            this.operationCancellationToken = operationCancellationToken;
        }

        public override bool CanRead => innerStream.CanRead;
        public override bool CanSeek => innerStream.CanSeek;
        public override bool CanWrite => false;
        public override long Length => innerStream.Length;
        public override long Position
        {
            get => innerStream.Position;
            set => innerStream.Position = value;
        }

        public override void Flush() => innerStream.Flush();
        public override int Read(byte[] buffer, int offset, int count) => innerStream.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => innerStream.Read(buffer);

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                operationCancellationToken,
                cancellationToken);
            timeout.CancelAfter(idleTimeout);

            try
            {
                return await innerStream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
                when (!operationCancellationToken.IsCancellationRequested
                    && !cancellationToken.IsCancellationRequested)
            {
                throw new DownloadBodyInterruptedException(
                    $"The response body produced no data for {idleTimeout}.",
                    exception);
            }
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override long Seek(long offset, SeekOrigin origin) => innerStream.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(ReadOnlySpan<byte> buffer) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                innerStream.Dispose();
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await innerStream.DisposeAsync().ConfigureAwait(false);
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class ThrottledReadStream : Stream
    {
        private readonly Stream innerStream;
        private readonly HttpContent ownedContent;
        private readonly DownloadBandwidthLimiter? bandwidthLimiter;
        private readonly IImportConcurrencyLease? completionLease;

        public ThrottledReadStream(
            Stream innerStream,
            HttpContent ownedContent,
            DownloadBandwidthLimiter? bandwidthLimiter,
            IImportConcurrencyLease? completionLease)
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
                bandwidthLimiter.Throttle(read);

            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            var read = innerStream.Read(buffer);
            if (read > 0 && bandwidthLimiter is not null)
                bandwidthLimiter.Throttle(read);

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
                completionLease?.Dispose();
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
