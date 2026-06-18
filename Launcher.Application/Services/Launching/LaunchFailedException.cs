namespace Launcher.Application.Services;

public sealed class LaunchFailedException : Exception
{
    public LaunchFailedException(LaunchFailureReport report, Exception innerException)
        : base("Minecraft launch failed before the game process became stable.", innerException)
    {
        Report = report;
    }

    public LaunchFailureReport Report { get; }
}
