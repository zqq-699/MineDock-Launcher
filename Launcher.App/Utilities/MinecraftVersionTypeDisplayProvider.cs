using Launcher.App.Resources;
using Launcher.App.ViewModels.Shared;

namespace Launcher.App.Utilities;

internal static class MinecraftVersionTypeDisplayProvider
{
    public static string GetLabel(string? versionType, string fallback = "")
    {
        return MinecraftVersionIconResolver.NormalizeVersionType(versionType) switch
        {
            "release" => Strings.Download_ReleaseCategory,
            "snapshot" => Strings.Download_SnapshotCategory,
            "old_beta" => Strings.Download_BetaCategory,
            "old_alpha" => Strings.Download_AlphaCategory,
            _ => fallback
        };
    }
}
