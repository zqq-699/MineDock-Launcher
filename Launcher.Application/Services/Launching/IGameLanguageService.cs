using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public interface IGameLanguageService
{
    Task ApplyLauncherLanguageAsync(
        GameInstance instance,
        string launcherLanguage,
        CancellationToken cancellationToken = default);
}
