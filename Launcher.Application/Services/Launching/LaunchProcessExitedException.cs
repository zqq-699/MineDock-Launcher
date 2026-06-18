namespace Launcher.Application.Services;

public sealed class LaunchProcessExitedException : Exception
{
    public LaunchProcessExitedException(string? diagnosticPath, Exception? innerException = null)
        : base("The launched Minecraft process exited before startup completed.", innerException)
    {
        Report = new LaunchFailureReport(
            LaunchFailureKind.StartupAbnormalExit,
            string.Empty,
            string.Empty,
            null,
            diagnosticPath,
            string.IsNullOrWhiteSpace(diagnosticPath) ? null : Path.GetDirectoryName(diagnosticPath));
    }

    public LaunchProcessExitedException(LaunchFailureReport report, Exception? innerException = null)
        : base("The launched Minecraft process exited before startup completed.", innerException)
    {
        Report = report;
    }

    public LaunchFailureReport Report { get; }

    public string? DiagnosticPath => Report.DiagnosticPath;
}
