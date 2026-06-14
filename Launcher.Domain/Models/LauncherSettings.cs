namespace Launcher.Domain.Models;

public sealed class LauncherSettings
{
    public string OfflineUsername { get; set; } = LauncherDefaults.DefaultOfflineUsername;
    public bool IsMenuExpanded { get; set; }
    public string Theme { get; set; } = LauncherDefaults.DefaultTheme;
    public string DataDirectory { get; set; } = string.Empty;
    public string MinecraftDirectory { get; set; } = string.Empty;
    public string? DefaultJavaPath { get; set; }
    public int DefaultMemoryMb { get; set; } = LauncherDefaults.DefaultMemoryMb;
    public string? DefaultInstanceId { get; set; }
    public string? SelectedAccountId { get; set; }
    public bool AccountsInitialized { get; set; }
    public bool MicrosoftAccountsImported { get; set; }
    public List<LauncherAccountRecord> Accounts { get; set; } = [];
}
