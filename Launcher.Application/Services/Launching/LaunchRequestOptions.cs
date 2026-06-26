namespace Launcher.Application.Services;

public sealed record LaunchRequestOptions(bool IgnoreJavaVersionRequirement = false)
{
    public static LaunchRequestOptions Default { get; } = new();
}
