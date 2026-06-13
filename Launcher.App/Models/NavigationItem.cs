using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Core.Models;

namespace Launcher.App.Models;

public sealed partial class NavigationItem : ObservableObject
{
    public required string Page { get; init; }
    public required string Title { get; init; }
    public required string Icon { get; init; }
    public string? IconKey { get; init; }
    public LoaderKind? Loader { get; init; }

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private string? avatarUrl;

    public bool HasAvatar => !string.IsNullOrWhiteSpace(AvatarUrl);

    partial void OnAvatarUrlChanged(string? value)
    {
        OnPropertyChanged(nameof(HasAvatar));
    }
}
