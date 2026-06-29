namespace Launcher.Application.Services;

public sealed class InstanceBackupException : Exception
{
    public InstanceBackupException(InstanceBackupFailureReason reason, string message)
        : base(message)
    {
        Reason = reason;
    }

    public InstanceBackupException(InstanceBackupFailureReason reason, string message, Exception innerException)
        : base(message, innerException)
    {
        Reason = reason;
    }

    public InstanceBackupFailureReason Reason { get; }
}
