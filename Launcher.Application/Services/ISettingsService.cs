using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public interface ISettingsService
{
    Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken = default);
}
