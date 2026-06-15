using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.App.Resources;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Home;

public sealed partial class HomeLaunchInstanceItem : ObservableObject
{
    public HomeLaunchInstanceItem(GameInstance instance, string versionType = "")
    {
        Instance = instance;
        VersionType = MinecraftVersionIconResolver.NormalizeVersionType(versionType);
    }

    public GameInstance Instance { get; }

    public string VersionType { get; }

    public string Name => string.IsNullOrWhiteSpace(Instance.Name)
        ? VersionName
        : Instance.Name;

    public string MinecraftVersion => string.IsNullOrWhiteSpace(Instance.MinecraftVersion)
        ? VersionName
        : Instance.MinecraftVersion;

    public string VersionName => string.IsNullOrWhiteSpace(Instance.VersionName)
        ? Instance.MinecraftVersion
        : Instance.VersionName;

    public string LoaderLabel => Instance.Loader switch
    {
        LoaderKind.Vanilla => Strings.Download_VanillaLoaderTitle,
        LoaderKind.Fabric => Strings.Download_FabricLoaderTitle,
        LoaderKind.Forge => Strings.Download_ForgeLoaderTitle,
        _ => Instance.Loader.ToString()
    };

    public string Subtitle => string.Format(Strings.Home_LaunchInstanceSubtitleFormat, MinecraftVersion, LoaderLabel);

    public string IconSource => MinecraftVersionIconResolver.Resolve(Instance, VersionType, MinecraftVersion);

    [ObservableProperty]
    private bool isSelected;
}

