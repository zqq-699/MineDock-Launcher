using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.App.Resources;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels;

public sealed partial class DownloadMinecraftVersionItem : ObservableObject
{
    public DownloadMinecraftVersionItem(MinecraftVersionInfo version)
    {
        Version = version;
    }

    public MinecraftVersionInfo Version { get; }

    public string Name => Version.Name;

    public string Type => Version.Type;

    public string TypeLabel => Version.Type.ToLowerInvariant() switch
    {
        "release" => Strings.Download_ReleaseCategory,
        "snapshot" => Strings.Download_SnapshotCategory,
        _ => Version.Type
    };

    public string ReleaseDateText => Version.ReleaseTime is { } releaseTime
        ? releaseTime.ToLocalTime().ToString("yyyy-MM-dd")
        : string.Empty;

    public bool IsRelease => Version.Type.Equals("Release", StringComparison.OrdinalIgnoreCase);

    public bool IsSnapshot => Version.Type.Equals("Snapshot", StringComparison.OrdinalIgnoreCase);

    public string IconSource => IsSnapshot
        ? "/Assets/Icons/block/dirt_block.png"
        : "/Assets/Icons/block/grass_block.png";

    [ObservableProperty]
    private bool isSelected;
}
