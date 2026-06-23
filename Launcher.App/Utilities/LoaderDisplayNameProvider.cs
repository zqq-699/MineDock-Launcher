using Launcher.App.Resources;
using Launcher.Domain.Models;

namespace Launcher.App.Utilities;

internal static class LoaderDisplayNameProvider
{
    public static string GetDisplayName(LoaderKind kind)
    {
        return kind switch
        {
            LoaderKind.Vanilla => Strings.Download_VanillaLoaderTitle,
            LoaderKind.Fabric => Strings.Download_FabricLoaderTitle,
            LoaderKind.Forge => Strings.Download_ForgeLoaderTitle,
            LoaderKind.NeoForge => Strings.Download_NeoForgeLoaderTitle,
            LoaderKind.Quilt => Strings.Download_QuiltLoaderTitle,
            _ => kind.ToString()
        };
    }
}
