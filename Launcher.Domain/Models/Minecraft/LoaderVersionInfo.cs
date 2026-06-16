namespace Launcher.Domain.Models;

public sealed record LoaderVersionInfo(string Version, bool IsStable = true)
{
    public override string ToString() => Version;
}
