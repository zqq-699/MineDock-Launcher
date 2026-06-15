using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests.Helpers;

internal sealed class TestSettingsService : ISettingsService
{
    private LauncherSettings settings;

    public TestSettingsService(LauncherSettings settings)
    {
        this.settings = settings;
    }

    public int SaveCount { get; private set; }

    public Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(settings);
    }

    public Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken = default)
    {
        SaveCount++;
        this.settings = settings;
        return Task.CompletedTask;
    }
}

