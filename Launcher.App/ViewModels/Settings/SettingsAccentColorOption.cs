namespace Launcher.App.ViewModels.Settings;

public sealed class SettingsAccentColorOption
{
    public SettingsAccentColorOption(string id, string title)
    {
        Id = id;
        Title = title;
    }

    public string Id { get; }

    public string Title { get; }
}
