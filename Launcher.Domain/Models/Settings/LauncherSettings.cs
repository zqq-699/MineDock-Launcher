namespace Launcher.Domain.Models;

public sealed class LauncherSettings
{
    public string OfflineUsername { get; set; } = LauncherDefaults.DefaultOfflineUsername;
    public bool IsMenuExpanded { get; set; }
    public string Theme { get; set; } = LauncherDefaults.DefaultTheme;
    public bool ThemeFollowSystem { get; set; } = true;
    public bool DisableBackgroundBlur { get; set; }
    public int LauncherBackgroundOpacityPercent { get; set; } = LauncherDefaults.DefaultLauncherBackgroundOpacityPercent;
    public string DataDirectory { get; set; } = string.Empty;
    public string MinecraftDirectory { get; set; } = string.Empty;
    public DownloadSourcePreference DownloadSourcePreference { get; set; } = DownloadSourcePreference.Auto;
    public int DownloadSpeedLimitMbPerSecond { get; set; }
    public MemorySettingsMode DefaultMemorySettingsMode { get; set; } = MemorySettingsMode.Auto;
    public int DefaultMemoryMb { get; set; } = LauncherDefaults.DefaultMemoryMb;
    public JavaSelectionMode JavaSelectionMode { get; set; } = JavaSelectionMode.Auto;
    public string? SelectedJavaExecutablePath { get; set; }
    public bool DefaultCheckFilesBeforeLaunch { get; set; } = true;
    public bool DefaultAutoRepairMissingFiles { get; set; } = true;
    public bool DefaultMinimizeLauncherAfterLaunch { get; set; }
    public bool DefaultLaunchFullScreen { get; set; }
    public string DefaultPreLaunchCommand { get; set; } = string.Empty;
    public bool DefaultWaitForPreLaunchCommand { get; set; } = true;
    public string DefaultPostExitCommand { get; set; } = string.Empty;
    public string DefaultJvmArguments { get; set; } = string.Empty;
    public string DefaultGameArguments { get; set; } = string.Empty;
    public string? DefaultInstanceId { get; set; }
    public string? SelectedAccountId { get; set; }
    public bool AccountsInitialized { get; set; }
    public bool MicrosoftAccountsImported { get; set; }
    public List<LauncherAccountRecord> Accounts { get; set; } = [];
}
