namespace Launcher.Application.Accounts;

public sealed class MicrosoftAccountSkinUpdateException : Exception
{
    public MicrosoftAccountSkinUpdateException(
        string? errorCode = null,
        Exception? innerException = null)
        : base("Microsoft account skin update failed", innerException)
    {
        ErrorCode = errorCode;
    }

    public string? ErrorCode { get; }
}
