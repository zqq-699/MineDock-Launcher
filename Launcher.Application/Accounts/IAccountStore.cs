using Launcher.Application.Accounts;
namespace Launcher.Application.Accounts;

public interface IAccountStore
{
    Task<AccountStoreSnapshot> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveOrderAsync(
        string? selectedAccountId,
        IEnumerable<LauncherAccount> accounts,
        CancellationToken cancellationToken = default);
}
