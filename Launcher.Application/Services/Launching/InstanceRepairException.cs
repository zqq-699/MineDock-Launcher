namespace Launcher.Application.Services;

public sealed class InstanceRepairException : Exception
{
    public LaunchDownloadDiagnostic? DownloadDiagnostic { get; }

    public InstanceRepairException()
    {
    }

    public InstanceRepairException(string message)
        : base(message)
    {
    }

    public InstanceRepairException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public InstanceRepairException(string message, LaunchDownloadDiagnostic downloadDiagnostic)
        : base(message)
    {
        DownloadDiagnostic = downloadDiagnostic;
    }

    public InstanceRepairException(
        string message,
        Exception innerException,
        LaunchDownloadDiagnostic downloadDiagnostic)
        : base(message, innerException)
    {
        DownloadDiagnostic = downloadDiagnostic;
    }
}
