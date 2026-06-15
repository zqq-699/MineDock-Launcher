using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Download;

public sealed partial class DownloadLoaderOption : ObservableObject
{
    public DownloadLoaderOption(LoaderKind kind, string title, string subtitle, string icon, string? iconSource = null)
    {
        Kind = kind;
        Title = title;
        Subtitle = subtitle;
        Icon = icon;
        IconSource = iconSource;
    }

    public LoaderKind Kind { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public string Icon { get; }

    public string? IconSource { get; }

    [ObservableProperty]
    private bool isSelected;
}

