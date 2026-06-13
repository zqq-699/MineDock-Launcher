using Launcher.App.Models;
using Launcher.Core.Models;

namespace Launcher.App.Services;

public interface IAccountStore
{
    Task<IReadOnlyList<LauncherAccount>> LoadAsync(LauncherSettings settings);

    Task SaveOrderAsync(LauncherSettings settings, IEnumerable<LauncherAccount> accounts);
}
