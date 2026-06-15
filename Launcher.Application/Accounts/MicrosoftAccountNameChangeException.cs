namespace Launcher.Application.Accounts;

public sealed class MicrosoftAccountNameChangeException : Exception
{
    public MicrosoftAccountNameChangeException(
        MicrosoftAccountNameChangeFailureReason reason,
        string? errorCode = null,
        Exception? innerException = null)
        : base($"Microsoft account name change failed: {reason}", innerException)
    {
        Reason = reason;
        ErrorCode = errorCode;
    }

    public MicrosoftAccountNameChangeFailureReason Reason { get; }

    public string? ErrorCode { get; }
}
