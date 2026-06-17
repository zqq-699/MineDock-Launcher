using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.GameSettings;

public sealed class GameSettingsLaunchSettingsModeOption
{
    public GameSettingsLaunchSettingsModeOption(LaunchSettingsMode mode, string title)
    {
        Mode = mode;
        Title = title;
    }

    public LaunchSettingsMode Mode { get; }

    public string Title { get; }

    public override string ToString()
    {
        return Title;
    }
}
