using CommunityToolkit.Mvvm.ComponentModel;

namespace Launcher.App.ViewModels.GameSettings;

public sealed partial class GameSettingsDetailSectionItem : ObservableObject
{
    public GameSettingsDetailSectionItem(string id, string title, string iconKey)
    {
        Id = id;
        Title = title;
        IconKey = iconKey;
    }

    public string Id { get; }

    public string Title { get; }

    public string Icon { get; } = string.Empty;

    public string IconKey { get; }

    public string IconMode => "Svg";

    [ObservableProperty]
    private bool isSelected;
}
