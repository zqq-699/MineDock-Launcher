using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.App.Utilities;
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

    public string Name => GameInstanceDisplayFormatter.GetName(Instance);

    public string MinecraftVersion => GameInstanceDisplayFormatter.GetMinecraftVersion(Instance);

    public string VersionName => GameInstanceDisplayFormatter.GetVersionName(Instance);

    public LoaderKind Loader => Instance.Loader;

    public bool HasModLoader => Loader is not LoaderKind.Vanilla;

    public bool IsRelease => VersionType.Equals("release", StringComparison.OrdinalIgnoreCase);

    public bool IsSnapshot => VersionType.Equals("snapshot", StringComparison.OrdinalIgnoreCase);

    public bool IsBeta => VersionType.Equals("old_beta", StringComparison.OrdinalIgnoreCase);

    public bool IsAlpha => VersionType.Equals("old_alpha", StringComparison.OrdinalIgnoreCase);

    public string TypeLabel => MinecraftVersionTypeDisplayProvider.GetLabel(VersionType);

    public string LoaderLabel => GameInstanceDisplayFormatter.GetLoaderLabel(Loader);

    public string LoaderVersionDisplay => LoaderVersionDisplayFormatter.Format(Loader, Instance.LoaderVersion);

    public string Subtitle => GameInstanceDisplayFormatter.GetSubtitle(Instance);

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
        OnPropertyChanged(nameof(LoaderVersionDisplay));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(UpdatedDateText));
        OnPropertyChanged(nameof(IconSource));
    }
}

