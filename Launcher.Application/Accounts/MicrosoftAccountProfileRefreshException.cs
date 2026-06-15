namespace Launcher.Application.Accounts;

public sealed class MicrosoftAccountProfileRefreshException : Exception
{
    public MicrosoftAccountProfileRefreshException(
        string? errorCode = null,
        Exception? innerException = null)
        : base("Microsoft account profile refresh failed", innerException)
    {
        ErrorCode = errorCode;
    }

    public string? ErrorCode { get; }
}
