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

    public GameInstance Instance { get; private set; }

    public string VersionType { get; private set; }

    public string Name => string.IsNullOrWhiteSpace(Instance.Name)
        ? VersionName
        : Instance.Name;

    public string MinecraftVersion => string.IsNullOrWhiteSpace(Instance.MinecraftVersion)
        ? Strings.GameSettings_UnknownMinecraftVersion
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

    public string Subtitle => Loader switch
    {
        LoaderKind.Vanilla => string.Format(Strings.GameSettings_InstanceSubtitleVanillaFormat, MinecraftVersion),
        _ when !string.IsNullOrWhiteSpace(Instance.LoaderVersion)
            => string.Format(
                Strings.GameSettings_InstanceSubtitleLoaderFormat,
                MinecraftVersion,
                LoaderLabel,
                Instance.LoaderVersion),
        _ => string.Format(
            Strings.GameSettings_InstanceSubtitleLoaderWithoutVersionFormat,
            MinecraftVersion,
            LoaderLabel)
    };

    public string UpdatedDateText => Instance.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd");

    public string IconSource => MinecraftVersionIconResolver.Resolve(Instance, VersionType, MinecraftVersion);

    [ObservableProperty]
    private bool isSelected;

    public void Update(GameInstance instance, string versionType)
    {
        var normalizedVersionType = NormalizeVersionType(versionType);
        if (ReferenceEquals(Instance, instance)
            && string.Equals(VersionType, normalizedVersionType, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Instance = instance;
        VersionType = normalizedVersionType;
        NotifyDisplayPropertiesChanged();
    }

    public bool MatchesSearch(string query)
    {
        return Contains(Name, query)
            || Contains(MinecraftVersion, query)
            || Contains(VersionName, query)
            || Contains(LoaderLabel, query)
            || Contains(Instance.LoaderVersion ?? string.Empty, query)
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

    private void NotifyDisplayPropertiesChanged()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(MinecraftVersion));
        OnPropertyChanged(nameof(VersionName));
        OnPropertyChanged(nameof(Loader));
        OnPropertyChanged(nameof(HasModLoader));
        OnPropertyChanged(nameof(IsRelease));
        OnPropertyChanged(nameof(IsSnapshot));
        OnPropertyChanged(nameof(IsBeta));
        OnPropertyChanged(nameof(IsAlpha));
        OnPropertyChanged(nameof(TypeLabel));
        OnPropertyChanged(nameof(LoaderLabel));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(UpdatedDateText));
        OnPropertyChanged(nameof(IconSource));
    }
}

