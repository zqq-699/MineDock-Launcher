namespace Launcher.Application.Services;

public sealed record LaunchExitResult(LaunchFailureReport? FailureReport)
{
    public bool IsFailure => FailureReport is not null;

    public static LaunchExitResult Success { get; } = new((LaunchFailureReport?)null);
}
