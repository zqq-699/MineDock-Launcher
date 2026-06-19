namespace Launcher.Domain.Models;

public sealed class LauncherSkinRecord
{
    public string Id { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public MinecraftSkinModel SkinModel { get; set; } = MinecraftSkinModel.Classic;
    public string ContentHash { get; set; } = string.Empty;
    public DateTimeOffset AddedAtUtc { get; set; }
}
