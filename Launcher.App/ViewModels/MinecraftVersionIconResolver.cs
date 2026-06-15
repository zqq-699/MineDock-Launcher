using Launcher.Domain.Models;

namespace Launcher.App.ViewModels;

internal static class MinecraftVersionIconResolver
{
    public const string DefaultReleaseIconSource = "/Assets/Icons/block/grass_block.png";
    public const string DefaultSnapshotIconSource = "/Assets/Icons/block/dirt_block.png";

    public static string Resolve(GameInstance instance, string? versionType = null, string? minecraftVersion = null)
    {
        if (!string.IsNullOrWhiteSpace(instance.IconSource))
            return instance.IconSource;

        var normalizedType = NormalizeVersionType(versionType);
        if (normalizedType.Equals("snapshot", StringComparison.OrdinalIgnoreCase)
            || IsSnapshotVersionName(minecraftVersion)
            || IsSnapshotVersionName(instance.MinecraftVersion)
            || IsSnapshotVersionName(instance.VersionName))
            return DefaultSnapshotIconSource;

        return DefaultReleaseIconSource;
    }

    public static string NormalizeVersionType(string? type)
    {
        return type?.Trim().ToLowerInvariant().Replace("-", "_") switch
        {
            "release" => "release",
            "snapshot" => "snapshot",
            "old_beta" or "oldbeta" or "beta" => "old_beta",
            "old_alpha" or "oldalpha" or "alpha" => "old_alpha",
            _ => string.Empty
        };
    }

    private static bool IsSnapshotVersionName(string? version)
    {
        return !string.IsNullOrWhiteSpace(version)
            && version.Length >= 5
            && char.IsDigit(version[0])
            && char.IsDigit(version[1])
            && version[2] == 'w'
            && char.IsDigit(version[3])
            && char.IsDigit(version[4]);
    }
}
