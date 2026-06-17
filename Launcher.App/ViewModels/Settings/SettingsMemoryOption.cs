using Launcher.App.Resources;

namespace Launcher.App.ViewModels.Settings;

public sealed class SettingsMemoryOption
{
    public SettingsMemoryOption(int memoryMb)
    {
        MemoryMb = memoryMb;
    }

    public int MemoryMb { get; }

    public string Title => string.Format(Strings.Settings_MemoryOptionFormat, MemoryMb);
}
