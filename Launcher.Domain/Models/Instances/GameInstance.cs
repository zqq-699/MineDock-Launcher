namespace Launcher.Domain.Models;

public sealed class GameInstance
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string MinecraftVersion { get; set; } = string.Empty;
    public LoaderKind Loader { get; set; } = LoaderKind.Vanilla;
    public string? LoaderVersion { get; set; }
    public string VersionName { get; set; } = string.Empty;
    public string VersionType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? IconSource { get; set; }
    public string InstanceDirectory { get; set; } = string.Empty;
    public string BackupDirectory { get; set; } = string.Empty;
    public MemorySettingsMode MemorySettingsMode { get; set; } = MemorySettingsMode.Manual;
    public int MemoryMb { get; set; } = 4096;
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 720;
    public string PreLaunchCommand { get; set; } = string.Empty;
    public bool WaitForPreLaunchCommand { get; set; } = true;
    public string PostExitCommand { get; set; } = string.Empty;
    public string JvmArguments { get; set; } = string.Empty;
    public string GameArguments { get; set; } = string.Empty;
    public LaunchSettingsMode LaunchSettingsMode { get; set; } = LaunchSettingsMode.UseGlobal;
    public LaunchSettingsMode JavaSettingsMode { get; set; } = LaunchSettingsMode.UseGlobal;
    public JavaSelectionMode JavaSelectionMode { get; set; } = JavaSelectionMode.Auto;
    public string? SelectedJavaExecutablePath { get; set; }
    public bool CheckFilesBeforeLaunch { get; set; } = true;
    public bool AutoRepairMissingFiles { get; set; } = true;
    public bool MinimizeLauncherAfterLaunch { get; set; }
    public bool LaunchFullScreen { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
