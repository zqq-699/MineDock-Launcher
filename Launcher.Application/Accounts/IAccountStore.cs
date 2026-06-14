using Launcher.Application.Accounts;
using Launcher.Domain.Models;

namespace Launcher.Application.Accounts;

public interface IAccountStore
{
    Task<IReadOnlyList<LauncherAccount>> LoadAsync(LauncherSettings settings);

    Task SaveOrderAsync(LauncherSettings settings, IEnumerable<LauncherAccount> accounts);
}
