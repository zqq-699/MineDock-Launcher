using Launcher.Domain.Models;

namespace Launcher.Application.Accounts;

public interface IAccountStateService
{
    Task<LauncherAccountState> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(LauncherAccountState state, CancellationToken cancellationToken = default);
}
