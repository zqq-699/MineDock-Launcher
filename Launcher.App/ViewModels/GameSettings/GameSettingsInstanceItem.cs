using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.App.Resources;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.GameSettings;

public sealed partial class GameSettingsInstanceItem : ObservableObject
{
    public GameSettingsInstanceItem(GameInstance instance, string versionType)
    {
        Instance = instance;
        VersionType = NormalizeVersionType(versionType);
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

    public LoaderKind Loader => Instance.Loader;

    public bool HasModLoader => Loader is not LoaderKind.Vanilla;

    public bool IsRelease => VersionType.Equals("release", StringComparison.OrdinalIgnoreCase);

    public bool IsSnapshot => VersionType.Equals("snapshot", StringComparison.OrdinalIgnoreCase);

    public bool IsBeta => VersionType.Equals("old_beta", StringComparison.OrdinalIgnoreCase);

    public bool IsAlpha => VersionType.Equals("old_alpha", StringComparison.OrdinalIgnoreCase);

    public string TypeLabel => VersionType switch
    {
        "release" => Strings.Download_ReleaseCategory,
        "snapshot" => Strings.Download_SnapshotCategory,
        "old_beta" => Strings.Download_BetaCategory,
        "old_alpha" => Strings.Download_AlphaCategory,
        _ => string.Empty
    };

    public string LoaderLabel => Loader switch
    {
        LoaderKind.Vanilla => Strings.Download_VanillaLoaderTitle,
        LoaderKind.Fabric => Strings.Download_FabricLoaderTitle,
        LoaderKind.Forge => Strings.Download_ForgeLoaderTitle,
        _ => Loader.ToString()
    };

    public string Subtitle => string.Format(Strings.GameSettings_InstanceSubtitleFormat, MinecraftVersion, LoaderLabel);

    public string UpdatedDateText => Instance.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd");

    public string IconSource => MinecraftVersionIconResolver.Resolve(Instance, VersionType, MinecraftVersion);

    [ObservableProperty]
    private bool isSelected;

    public bool MatchesSearch(string query)
    {
        return Contains(Name, query)
            || Contains(MinecraftVersion, query)
            || Contains(VersionName, query)
            || Contains(LoaderLabel, query)
            || Contains(TypeLabel, query);
    }

    public static string NormalizeVersionType(string? type)
    {
        return MinecraftVersionIconResolver.NormalizeVersionType(type);
    }

    private static bool Contains(string value, string query)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}

