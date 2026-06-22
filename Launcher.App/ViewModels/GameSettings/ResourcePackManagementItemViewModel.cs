using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.GameSettings;

public sealed partial class ResourcePackManagementItemViewModel : ObservableObject
{
    public ResourcePackManagementItemViewModel(LocalResourcePack resourcePack)
    {
        Title = resourcePack.Name;
        Subtitle = string.Equals(resourcePack.Name, resourcePack.FileName, StringComparison.OrdinalIgnoreCase)
            ? null
            : resourcePack.FileName;
        FullPath = resourcePack.FullPath;
        IconSource = resourcePack.IconSource;
        CreatedAt = resourcePack.CreatedAt;
    }

    public string Title { get; }

    public string? Subtitle { get; }

    public string FullPath { get; }

    public string? IconSource { get; }

    public DateTimeOffset CreatedAt { get; }

    public string TrailingText => CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public string IconKey => string.IsNullOrWhiteSpace(IconSource)
        ? "main_menu_library"
        : string.Empty;

    [ObservableProperty]
    private bool isSelected;
}
