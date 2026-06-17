namespace Launcher.App.ViewModels.GameSettings;

public sealed class GameSettingsIconOption
{
    public GameSettingsIconOption(string title, string iconSource)
    {
        Title = title;
        IconSource = iconSource;
    }

    public string Title { get; }

    public string IconSource { get; }

    public override string ToString() => Title;
}
