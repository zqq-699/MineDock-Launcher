using System.Net;
using System.Net.Http;

namespace Launcher.Infrastructure.Minecraft;

internal sealed record DownloadRetryOptions
{
    public static DownloadRetryOptions Default { get; } = new();

    public int MaxAttemptsPerSource { get; init; } = 4;
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan ResponseHeadersTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan BodyIdleTimeout { get; init; } = TimeSpan.FromSeconds(30);
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
    ResponseHeadersTimeout,
    BodyInterrupted,
    HttpStatus,
    InvalidRedirect,
    InvalidContent,
    HashMismatch,
    LocalFileSystem
}

internal class DownloadAttemptException : Exception
{
    public DownloadAttemptException(
        DownloadFailureDisposition disposition,
        DownloadFailureReason reason,
        string message,
        Exception? innerException = null,
        HttpStatusCode? statusCode = null)
        : base(message, innerException)
    {
        Disposition = disposition;
        Reason = reason;
        StatusCode = statusCode;
    }

    public DownloadFailureDisposition Disposition { get; }
    public DownloadFailureReason Reason { get; }
    public HttpStatusCode? StatusCode { get; }
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
    int AttemptNumber);
