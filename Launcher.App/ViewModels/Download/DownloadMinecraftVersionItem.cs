using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.App.Resources;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Download;

public sealed partial class DownloadMinecraftVersionItem : ObservableObject
{
    public DownloadMinecraftVersionItem(MinecraftVersionInfo version)
    {
        Version = version;
    }

    public MinecraftVersionInfo Version { get; }

    public string Name => Version.Name;

    public string Type => Version.Type;

    public string VersionType => MinecraftVersionIconResolver.NormalizeVersionType(Type);

    public string TypeLabel => VersionType switch
    {
        "release" => Strings.Download_ReleaseCategory,
        "snapshot" => Strings.Download_SnapshotCategory,
        "old_beta" => Strings.Download_BetaCategory,
        "old_alpha" => Strings.Download_AlphaCategory,
        _ => Version.Type
    };

    public string ReleaseDateText => Version.ReleaseTime is { } releaseTime
        ? releaseTime.ToLocalTime().ToString("yyyy-MM-dd")
        : string.Empty;

    public bool IsRelease => VersionType.Equals("release", StringComparison.OrdinalIgnoreCase);

    public bool IsSnapshot => VersionType.Equals("snapshot", StringComparison.OrdinalIgnoreCase);

    public bool IsBeta => VersionType.Equals("old_beta", StringComparison.OrdinalIgnoreCase);

    public bool IsAlpha => VersionType.Equals("old_alpha", StringComparison.OrdinalIgnoreCase);

    public string IconSource => MinecraftVersionIconResolver.Resolve(VersionType, Name);

    [ObservableProperty]
    private bool isSelected;
}

