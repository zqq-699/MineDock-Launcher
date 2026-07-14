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
using System.Net.Http;

namespace Launcher.Infrastructure.Minecraft;

internal sealed record DownloadRetryOptions
{
    public static DownloadRetryOptions Default { get; } = new();

    public int MaxAttemptsPerSource { get; init; } = 4;
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan ResponseHeadersTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan FirstByteTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan BodyIdleTimeout { get; init; } = TimeSpan.FromSeconds(12);
    public TimeSpan SustainedLowSpeedWindow { get; init; } = TimeSpan.FromSeconds(10);
    public long SustainedLowSpeedBytesPerSecond { get; init; } = 16 * 1024;
    public long LowSpeedMinimumFileBytes { get; init; } = 1024 * 1024;
    public TimeSpan MaximumRetryAfter { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan MaximumRetryDelay { get; init; } = TimeSpan.FromSeconds(30);
    public int MaxRedirects { get; init; } = 10;
}

internal enum DownloadFailureDisposition
{
    RetryCurrentSource,
    SwitchSource,
    Abort
}

internal enum DownloadFailureReason
{
    Network,
    Dns,
    ResponseHeadersTimeout,
    FirstByteTimeout,
    BodyIdleTimeout,
    SustainedLowSpeed,
    BodyInterrupted,
    HttpStatus,
    InvalidRedirect,
    InvalidContent,
    HashMismatch,
    LocalFileSystem,
    ResourceConflict,
    UnsafeAddress
}

internal class DownloadAttemptException : Exception
{
    public DownloadAttemptException(
        DownloadFailureDisposition disposition,
        DownloadFailureReason reason,
        string message,
        Exception? innerException = null,
        HttpStatusCode? statusCode = null,
        TimeSpan? retryAfter = null)
        : base(message, innerException)
    {
        Disposition = disposition;
        Reason = reason;
        StatusCode = statusCode;
        RetryAfter = retryAfter;
    }

    public DownloadFailureDisposition Disposition { get; }
    public DownloadFailureReason Reason { get; }
    public HttpStatusCode? StatusCode { get; }
    public TimeSpan? RetryAfter { get; }
    public string? FinalHost { get; private set; }

    public DownloadAttemptException WithFinalHost(string? finalHost)
    {
        FinalHost = finalHost;
        return this;
    }
}

internal sealed class DownloadBodyInterruptedException : DownloadAttemptException
{
    public DownloadBodyInterruptedException(string message, Exception? innerException = null)
        : base(
            DownloadFailureDisposition.RetryCurrentSource,
            DownloadFailureReason.BodyInterrupted,
            message,
            innerException)
    {
    }
}

internal sealed class DownloadTimeoutException : DownloadAttemptException
{
    public DownloadTimeoutException(DownloadFailureReason reason, string message, Exception? innerException = null)
        : base(DownloadFailureDisposition.RetryCurrentSource, reason, message, innerException)
    {
    }
}

internal sealed class DownloadContentValidationException : DownloadAttemptException
{
    public DownloadContentValidationException(string message, Exception? innerException = null)
        : base(
            DownloadFailureDisposition.SwitchSource,
            DownloadFailureReason.InvalidContent,
            message,
            innerException)
    {
    }
}

internal sealed class DownloadHashMismatchException : DownloadAttemptException
{
    public DownloadHashMismatchException(string message)
        : base(
            DownloadFailureDisposition.SwitchSource,
            DownloadFailureReason.HashMismatch,
            message)
    {
    }
}

internal sealed class DownloadLocalFileException : DownloadAttemptException
{
    public DownloadLocalFileException(string message, Exception innerException)
        : base(
            DownloadFailureDisposition.Abort,
            DownloadFailureReason.LocalFileSystem,
            message,
            innerException)
    {
    }
}

internal sealed class DownloadNoResultException : Exception
{
    public DownloadNoResultException(string message)
        : base(message)
    {
    }
}

internal sealed record DownloadLookupResult<T>(bool Found, T? Value)
{
    public static DownloadLookupResult<T> Success(T value) => new(true, value);
    public static DownloadLookupResult<T> NotFound() => new(false, default);
}

internal sealed record DownloadAttemptContext(
    ResolvedDownloadRequest Resolution,
    HttpResponseMessage Response,
    DownloadTransportResult Transport,
    int AttemptNumber);
