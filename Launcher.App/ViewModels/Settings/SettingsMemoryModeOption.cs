using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Settings;

public sealed class SettingsMemoryModeOption
{
    public SettingsMemoryModeOption(MemorySettingsMode mode, string title)
    {
        Mode = mode;
        Title = title;
    }

    public MemorySettingsMode Mode { get; }

    public string Title { get; }
}
