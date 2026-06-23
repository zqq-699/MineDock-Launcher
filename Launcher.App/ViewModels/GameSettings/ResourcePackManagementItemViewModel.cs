using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.GameSettings;

public sealed partial class ResourcePackManagementItemViewModel : ObservableObject
{
    public ResourcePackManagementItemViewModel(LocalResourcePack resourcePack)
    {
        SyncFrom(resourcePack);
    }

    public string TrailingText => CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public string IconKey => string.IsNullOrWhiteSpace(IconSource)
        ? "main_menu_library"
        : string.Empty;

    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private string? subtitle;

    [ObservableProperty]
    private string fullPath = string.Empty;

    [ObservableProperty]
    private string? iconSource;

    [ObservableProperty]
    private DateTimeOffset createdAt;

    [ObservableProperty]
    private bool isSelected;

    public void SyncFrom(LocalResourcePack resourcePack)
    {
        Title = resourcePack.Name;
        Subtitle = string.Equals(resourcePack.Name, resourcePack.FileName, StringComparison.OrdinalIgnoreCase)
            ? null
            : resourcePack.FileName;
        FullPath = resourcePack.FullPath;
        IconSource = resourcePack.IconSource;
        CreatedAt = resourcePack.CreatedAt;
    }

    partial void OnIconSourceChanged(string? value)
    {
        OnPropertyChanged(nameof(IconKey));
    }

    partial void OnCreatedAtChanged(DateTimeOffset value)
    {
        OnPropertyChanged(nameof(TrailingText));
    }
}
