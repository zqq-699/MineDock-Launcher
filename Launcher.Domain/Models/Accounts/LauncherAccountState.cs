namespace Launcher.Domain.Models;

public sealed class LauncherAccountState
{
    public string OfflineUsername { get; set; } = LauncherDefaults.DefaultOfflineUsername;
    public string? SelectedAccountId { get; set; }
    public bool AccountsInitialized { get; set; }
    public bool MicrosoftAccountsImported { get; set; }
    public List<LauncherAccountRecord> Accounts { get; set; } = [];
}
