using Launcher.Application.Accounts;

namespace Launcher.Application.Accounts;

public interface IMicrosoftAccountService
{
    Task<IReadOnlyList<LauncherAccount>> GetSavedAccountsAsync(CancellationToken cancellationToken = default);

    Task<LauncherAccount> LoginInteractivelyAsync(CancellationToken cancellationToken = default);

    Task DeleteAccountAsync(LauncherAccount account, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AccountCapeOption>> GetCapesAsync(LauncherAccount account, CancellationToken cancellationToken = default);

    Task<LauncherAccount> UploadSkinAsync(LauncherAccount account, string skinFilePath, CancellationToken cancellationToken = default);

    Task SetActiveCapeAsync(LauncherAccount account, string? capeId, CancellationToken cancellationToken = default);

    Task<LauncherAccount> ChangeNameAsync(LauncherAccount account, string newName, CancellationToken cancellationToken = default);
}
