using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Minecraft;

internal sealed record LaunchDiagnosticContext(
    string MinecraftDirectory,
    string InstanceDirectory,
    string InstanceId,
    string InstanceName,
    string VersionName,
    string MinecraftVersion,
    LoaderKind Loader,
    string? LoaderVersion,
    string? JavaPath,
    int MemoryMb,
    IReadOnlyList<string> SensitiveValues);
