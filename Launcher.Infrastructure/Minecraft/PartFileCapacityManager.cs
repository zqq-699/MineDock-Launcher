/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO;

namespace Launcher.Infrastructure.Minecraft;

/// <summary>
/// Process-local capacity accounting for task-scoped work files. Its lock only
/// protects counters; it never enumerates user download directories.
/// </summary>
internal static class PartFileCapacityManager
{
    internal const long DefaultCapacityBytes = 2L * 1024 * 1024 * 1024;
    private const long InitialUnknownReservation = 64L * 1024 * 1024;
    private const long UnknownGrowthBytes = 16L * 1024 * 1024;
    private static readonly object SyncRoot = new();
    private static long retainedBytes;
    private static long reservedBytes;

    public static CapacityLease Reserve(long? expectedSize)
    {
        var reservation = expectedSize is > 0 ? expectedSize.Value : InitialUnknownReservation;
        lock (SyncRoot)
        {
            EnsureFits(reservation);
            reservedBytes += reservation;
        }
        return new CapacityLease(reservation);
    }

    private static void EnsureFits(long additional)
    {
        if (additional < 0 || retainedBytes + reservedBytes + additional > DefaultCapacityBytes)
            throw LocalFailure("The task-scoped download capacity budget is exhausted.");
    }

    private static DownloadLocalFileException LocalFailure(string message) =>
        new(message, new IOException(message));

    internal sealed class CapacityLease : IDisposable
    {
        private long remainingReservation;
        private long retained;
        private bool disposed;

        internal CapacityLease(long reservation) => remainingReservation = reservation;

        public void SetExpectedSize(long? expectedSize)
        {
            if (!expectedSize.HasValue || expectedSize.Value <= 0)
                return;
            lock (SyncRoot)
            {
                ThrowIfDisposed();
                var desiredRemaining = Math.Max(0, expectedSize.Value - retained);
                var delta = desiredRemaining - remainingReservation;
                if (delta > 0)
                    EnsureFits(delta);
                reservedBytes += delta;
                remainingReservation = desiredRemaining;
            }
        }

        public void BeforeWrite(int bytes)
        {
            if (bytes <= 0)
                return;
            lock (SyncRoot)
            {
                ThrowIfDisposed();
                while (remainingReservation < bytes)
                {
                    var growth = Math.Max(UnknownGrowthBytes, bytes - remainingReservation);
                    EnsureFits(growth);
                    reservedBytes += growth;
                    remainingReservation += growth;
                }
                remainingReservation -= bytes;
                reservedBytes -= bytes;
                retained += bytes;
                retainedBytes += bytes;
            }
        }

        public void DiscardRetainedBytes()
        {
            lock (SyncRoot)
            {
                ThrowIfDisposed();
                if (retained == 0)
                    return;

                // A 200 response after a Range request (or an invalid partial
                // response) replaces this task's temporary file. Convert the
                // discarded physical bytes back into this same lease's future
                // reservation without changing the global total.
                retainedBytes -= retained;
                reservedBytes += retained;
                remainingReservation += retained;
                retained = 0;
            }
        }

        public void Dispose()
        {
            lock (SyncRoot)
            {
                if (disposed)
                    return;
                disposed = true;
                reservedBytes -= remainingReservation;
                retainedBytes -= retained;
                remainingReservation = 0;
                retained = 0;
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(CapacityLease));
        }
    }
}
