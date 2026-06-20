namespace Launcher.App.ViewModels.Settings;

public sealed class SettingsThemeOption
{
    public SettingsThemeOption(string id, string title)
    {
        Id = id;
        Title = title;
    }

    public string Id { get; }

    public string Title { get; }
}
