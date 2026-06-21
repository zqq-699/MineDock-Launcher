using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.App.Utilities;
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

    public string Name => GameInstanceDisplayFormatter.GetName(Instance);

    public string MinecraftVersion => GameInstanceDisplayFormatter.GetMinecraftVersion(Instance);

    public string VersionName => GameInstanceDisplayFormatter.GetVersionName(Instance);

    public string LoaderLabel => GameInstanceDisplayFormatter.GetLoaderLabel(Instance.Loader);

    public string LoaderVersionDisplay => LoaderVersionDisplayFormatter.Format(Instance.Loader, Instance.LoaderVersion);

    public string Subtitle => GameInstanceDisplayFormatter.GetSubtitle(Instance);

    public string IconSource => MinecraftVersionIconResolver.Resolve(Instance, VersionType, MinecraftVersion);

    [ObservableProperty]
    private bool isSelected;
}

