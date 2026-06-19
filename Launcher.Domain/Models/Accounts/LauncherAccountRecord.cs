namespace Launcher.Domain.Models;

public sealed class LauncherAccountRecord
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Uuid { get; set; }
    public OfflineUuidGenerationMode OfflineUuidGenerationMode { get; set; } = OfflineUuidGenerationMode.Standard;
    public string? AvatarSource { get; set; }
    public string? SkinSource { get; set; }
    public MinecraftSkinModel? SkinModel { get; set; }
    public bool IsOffline { get; set; } = true;
    public List<LauncherCapeRecord> Capes { get; set; } = [];
}
