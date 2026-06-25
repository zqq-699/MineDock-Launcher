namespace Launcher.Application.Services;

public sealed class JavaRuntimeSelectionException : Exception
{
    public JavaRuntimeSelectionException(
        string message,
        JavaRuntimeSelectionFailureReason reason = JavaRuntimeSelectionFailureReason.Unknown,
        int? requiredMajorVersion = null)
        : base(message)
    {
        Reason = reason;
        RequiredMajorVersion = requiredMajorVersion;
    }

    public JavaRuntimeSelectionException(
        string message,
        Exception innerException,
        JavaRuntimeSelectionFailureReason reason = JavaRuntimeSelectionFailureReason.Unknown,
        int? requiredMajorVersion = null)
        : base(message, innerException)
    {
        Reason = reason;
        RequiredMajorVersion = requiredMajorVersion;
    }

    public JavaRuntimeSelectionFailureReason Reason { get; }

    public int? RequiredMajorVersion { get; }
}

public enum JavaRuntimeSelectionFailureReason
{
    Unknown,
    AutomaticRuntimeMissing,
    AutomaticRuntimeNotFound,
    ManualRuntimeMissing,
    ManualRuntimeUnavailable
}
