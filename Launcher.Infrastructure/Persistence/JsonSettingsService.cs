using System.IO;
using System.Text.Json;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure;

namespace Launcher.Infrastructure.Persistence;

public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string settingsPath;
    private readonly LauncherPathProvider pathProvider;
    private readonly SemaphoreSlim ioLock = new(1, 1);

    public JsonSettingsService(string? dataDirectory = null)
    {
        pathProvider = new LauncherPathProvider();
        var root = dataDirectory ?? pathProvider.DefaultDataDirectory;
        settingsPath = Path.Combine(root, "settings.json");
    }

    public async Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await ioLock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(settingsPath))
            {
                var settings = Normalize(new LauncherSettings
                {
                    DataDirectory = Path.GetDirectoryName(settingsPath) ?? pathProvider.DefaultDataDirectory
                });
                await SaveCoreAsync(settings, cancellationToken);
                return settings;
            }

            await using var stream = File.OpenRead(settingsPath);
            var loaded = await JsonSerializer.DeserializeAsync<LauncherSettings>(stream, JsonOptions, cancellationToken);
            return Normalize(loaded ?? new LauncherSettings());
        }
        finally
        {
            ioLock.Release();
        }
    }

    public async Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(settings);
        await ioLock.WaitAsync(cancellationToken);
        try
        {
            await SaveCoreAsync(normalized, cancellationToken);
        }
        finally
        {
            ioLock.Release();
        }
    }

    private async Task SaveCoreAsync(LauncherSettings settings, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        await using var stream = File.Create(settingsPath);
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
    }

    private LauncherSettings Normalize(LauncherSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.OfflineUsername))
            settings.OfflineUsername = LauncherDefaults.DefaultOfflineUsername;

        if (string.IsNullOrWhiteSpace(settings.DataDirectory))
            settings.DataDirectory = pathProvider.DefaultDataDirectory;

        settings.MinecraftDirectory = Path.GetFullPath(pathProvider.DefaultMinecraftDirectory);

        settings.Accounts ??= [];
        settings.Accounts.RemoveAll(account => string.IsNullOrWhiteSpace(account.Id)
            || string.IsNullOrWhiteSpace(account.DisplayName));
        foreach (var account in settings.Accounts)
        {
            account.Capes ??= [];
            account.Capes.RemoveAll(cape => !cape.IsNone && string.IsNullOrWhiteSpace(cape.DisplayName));
        }

        if (!settings.AccountsInitialized)
            settings.AccountsInitialized = true;

        if (!string.IsNullOrWhiteSpace(settings.SelectedAccountId)
            && settings.Accounts.All(account => !string.Equals(account.Id, settings.SelectedAccountId, StringComparison.Ordinal)))
        {
            settings.SelectedAccountId = null;
        }

        settings.DefaultMemoryMb = Math.Clamp(settings.DefaultMemoryMb, 1024, 32768);
        if (settings.JavaSelectionMode is not JavaSelectionMode.Auto
            && settings.JavaSelectionMode is not JavaSelectionMode.Manual)
        {
            settings.JavaSelectionMode = JavaSelectionMode.Auto;
        }

        if (string.IsNullOrWhiteSpace(settings.SelectedJavaExecutablePath))
            settings.SelectedJavaExecutablePath = null;

        return settings;
    }
}
