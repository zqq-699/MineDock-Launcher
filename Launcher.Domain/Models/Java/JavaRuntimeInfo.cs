namespace Launcher.Domain.Models;

public sealed record JavaRuntimeInfo(
    string DisplayName,
    string? Version,
    int? MajorVersion,
    string Architecture,
    string ExecutablePath,
    string InstallationDirectory,
    string Source);
