namespace Launcher.Domain.Models;

public sealed class GameInstance
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string MinecraftVersion { get; set; } = string.Empty;
    public LoaderKind Loader { get; set; } = LoaderKind.Vanilla;
    public string? LoaderVersion { get; set; }
    public string VersionName { get; set; } = string.Empty;
    public string InstanceDirectory { get; set; } = string.Empty;
    public string? JavaPath { get; set; }
    public int MemoryMb { get; set; } = 4096;
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 720;
    public string JvmArguments { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
