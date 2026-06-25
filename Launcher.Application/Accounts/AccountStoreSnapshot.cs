namespace Launcher.Application.Accounts;

public sealed record AccountStoreSnapshot(
    IReadOnlyList<LauncherAccount> Accounts,
    string? SelectedAccountId);
