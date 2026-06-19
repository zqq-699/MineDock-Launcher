using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Domain.Models;

namespace Launcher.Application.Accounts;

public sealed partial class LauncherAccount : ObservableObject
{
    public const string DefaultSteveAvatarUrl = "https://minotar.net/avatar/Steve/32.png";

    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public string? Uuid { get; init; }
    public OfflineUuidGenerationMode OfflineUuidGenerationMode { get; init; } = OfflineUuidGenerationMode.Standard;
    public string? AvatarSource { get; init; }
    public string? SkinSource { get; init; }
    public MinecraftSkinModel? SkinModel { get; init; }
    public bool IsOffline { get; init; }
    public bool HasFreshProfile { get; init; }
    public IReadOnlyList<AccountCapeOption> CachedCapeOptions { get; init; } = [];

    public string AvatarUrl
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(AvatarSource))
                return AvatarSource;

            if (IsOffline || string.IsNullOrWhiteSpace(Uuid))
                return DefaultSteveAvatarUrl;

            return $"https://crafatar.com/avatars/{Uuid}?size=32&overlay";
        }
    }

    [ObservableProperty]
    private bool isSelected;
}
