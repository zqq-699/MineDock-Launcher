using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests;

internal sealed class TestSettingsService : ISettingsService
{
    private LauncherSettings settings;

    public TestSettingsService(LauncherSettings settings)
    {
        this.settings = settings;
    }

    public Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(settings);
    }

    public Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken = default)
    {
        this.settings = settings;
        return Task.CompletedTask;
    }
}
