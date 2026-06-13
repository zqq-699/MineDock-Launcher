using CommunityToolkit.Mvvm.ComponentModel;

namespace Launcher.App.Models;

public sealed partial class LauncherAccount : ObservableObject
{
    public const string DefaultSteveAvatarUrl = "https://minotar.net/avatar/Steve/32.png";

    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public string? Uuid { get; init; }
    public string? AvatarSource { get; init; }
    public bool IsOffline { get; init; }
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

    public string AccountKindText => IsOffline ? "\u79bb\u7ebf\u8d26\u6237" : "\u6b63\u7248\u8d26\u6237";
    public string UuidText => string.IsNullOrWhiteSpace(Uuid) ? "\u65e0" : Uuid;

    [ObservableProperty]
    private bool isSelected;
}
