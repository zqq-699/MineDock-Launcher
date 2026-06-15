using CommunityToolkit.Mvvm.ComponentModel;

namespace Launcher.App.ViewModels.GameSettings;

public sealed partial class GameSettingsInstanceCategory : ObservableObject
{
    public GameSettingsInstanceCategory(string id, string title, string icon, string? iconKey = null)
    {
        Id = id;
        Title = title;
        Icon = icon;
        IconKey = iconKey;
    }

    public string Id { get; }

    public string Title { get; }

    public string Icon { get; }

    public string? IconKey { get; }

    public string IconMode => string.IsNullOrWhiteSpace(IconKey) ? "Glyph" : "Svg";

    [ObservableProperty]
    private bool isSelected;
}

