namespace Launcher.App.ViewModels.Settings;

public sealed class SettingsJavaSelectionOption
{
    public SettingsJavaSelectionOption(string id, string title)
    {
        Id = id;
        Title = title;
    }

    public string Id { get; }

    public string Title { get; }
}
