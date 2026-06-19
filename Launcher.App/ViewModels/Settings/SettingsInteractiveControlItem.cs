namespace Launcher.App.ViewModels.Settings;

public sealed class SettingsInteractiveControlItem
{
    public SettingsInteractiveControlItem(string title, string category)
    {
        Title = title;
        Category = category;
    }

    public string Title { get; }

    public string Category { get; }
}
