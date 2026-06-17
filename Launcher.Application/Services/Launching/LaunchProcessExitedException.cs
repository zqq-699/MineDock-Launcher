namespace Launcher.Application.Services;

public sealed class LaunchProcessExitedException : Exception
{
    public LaunchProcessExitedException(string? diagnosticPath, Exception? innerException = null)
        : base("The launched Minecraft process exited before startup completed.", innerException)
    {
        DiagnosticPath = diagnosticPath;
    }

    public string? DiagnosticPath { get; }
}
