using System.Net;

namespace Launcher.Infrastructure.Accounts;

internal sealed class MinecraftProfileRequestException : Exception
{
    public MinecraftProfileRequestException(
        MinecraftProfileErrorKind errorKind,
        HttpStatusCode statusCode,
        string? errorCode,
        string message)
        : base(message)
    {
        ErrorKind = errorKind;
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }

    public MinecraftProfileErrorKind ErrorKind { get; }

    public HttpStatusCode StatusCode { get; }

    public string? ErrorCode { get; }
}
