using Launcher.App.Resources;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Settings;

public sealed class SettingsJavaRuntimeItem
{
    public SettingsJavaRuntimeItem(JavaRuntimeInfo runtime)
    {
        DisplayName = runtime.DisplayName;
        VersionText = string.IsNullOrWhiteSpace(runtime.Version)
            ? Strings.Settings_JavaVersionUnknown
            : runtime.Version;
        MajorVersion = runtime.MajorVersion;
        Architecture = runtime.Architecture;
        ExecutablePath = runtime.ExecutablePath;
        InstallationDirectory = runtime.InstallationDirectory;
        Source = runtime.Source;
    }

    public string DisplayName { get; }

    public string VersionText { get; }

    public int? MajorVersion { get; }

    public string Architecture { get; }

    public string ExecutablePath { get; }

    public string InstallationDirectory { get; }

    public string Source { get; }
}
