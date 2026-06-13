namespace Launcher.Core.Models;

public sealed class LauncherSettings
{
    public string OfflineUsername { get; set; } = "Player";
    public bool IsMenuExpanded { get; set; }
    public string Theme { get; set; } = "Dark";
    public string DataDirectory { get; set; } = LauncherDefaults.DefaultDataDirectory;
    public string? DefaultJavaPath { get; set; }
    public int DefaultMemoryMb { get; set; } = 4096;
    public string? DefaultInstanceId { get; set; }
    public bool AccountsInitialized { get; set; }
    public bool MicrosoftAccountsImported { get; set; }
    public List<LauncherAccountRecord> Accounts { get; set; } = [];
}
