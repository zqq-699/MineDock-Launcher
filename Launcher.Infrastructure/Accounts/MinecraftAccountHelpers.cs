using CmlLib.Core.Auth.Microsoft.Sessions;
using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Accounts;

internal static class MinecraftAccountHelpers
{
    public static bool IsActiveState(string? state)
    {
        return string.Equals(state, "ACTIVE", StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeUuid(string? uuid)
    {
        return uuid?.Replace("-", string.Empty, StringComparison.Ordinal) ?? string.Empty;
    }

    public static string? GetActiveSkinUrl(JEProfile profile)
    {
        var activeSkin = profile.Skins?
            .FirstOrDefault(skin => IsActiveState(skin.State)
                && !string.IsNullOrWhiteSpace(skin.Url));

        return activeSkin?.Url
            ?? profile.Skins?.FirstOrDefault(skin => !string.IsNullOrWhiteSpace(skin.Url))?.Url;
    }

    public static string? GetActiveSkinUrl(MinecraftProfileResponse profile)
    {
        var activeSkin = profile.Skins?
            .FirstOrDefault(skin => IsActiveState(skin.State) && !string.IsNullOrWhiteSpace(skin.Url));

        return activeSkin?.Url
            ?? profile.Skins?.FirstOrDefault(skin => !string.IsNullOrWhiteSpace(skin.Url))?.Url;
    }

    public static MinecraftSkinModel? GetActiveSkinModel(MinecraftProfileResponse profile)
    {
        var activeSkin = profile.Skins?
            .FirstOrDefault(skin => IsActiveState(skin.State) && !string.IsNullOrWhiteSpace(skin.Url))
            ?? profile.Skins?.FirstOrDefault(skin => !string.IsNullOrWhiteSpace(skin.Url));

        return activeSkin?.Variant?.ToLowerInvariant() switch
        {
            "slim" => MinecraftSkinModel.Slim,
            "classic" => MinecraftSkinModel.Classic,
            _ => null
        };
    }
}
