namespace Launcher.Core.Models;

public sealed record MinecraftVersionInfo(string Name, string Type, bool IsInstalled, DateTimeOffset? ReleaseTime = null);
