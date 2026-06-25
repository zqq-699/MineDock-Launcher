using System.IO;
using System.Text.Json;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Persistence;

public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string settingsPath;
    private readonly LauncherPathProvider pathProvider;
    private readonly ILogger<JsonSettingsService> logger;
    private readonly SemaphoreSlim ioLock = new(1, 1);

    public JsonSettingsService(string? dataDirectory = null, ILogger<JsonSettingsService>? logger = null)
    {
        pathProvider = new LauncherPathProvider();
        var root = dataDirectory ?? pathProvider.DefaultDataDirectory;
        settingsPath = Path.Combine(root, "settings.json");
        this.logger = logger ?? NullLogger<JsonSettingsService>.Instance;
    }

    public async Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await ioLock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(settingsPath))
            {
                var defaultSettings = Normalize(new LauncherSettings
                {
                    DataDirectory = Path.GetDirectoryName(settingsPath) ?? pathProvider.DefaultDataDirectory
                });
                await SaveCoreAsync(defaultSettings, cancellationToken);
                logger.LogInformation("Default launcher settings created. SettingsPath={SettingsPath}", settingsPath);
                return defaultSettings;
            }

            await using var stream = File.OpenRead(settingsPath);
            var loaded = await JsonSerializer.DeserializeAsync<LauncherSettings>(stream, JsonOptions, cancellationToken);
            var loadedSettings = Normalize(loaded ?? new LauncherSettings());
            logger.LogDebug("Launcher settings loaded. SettingsPath={SettingsPath}", settingsPath);
            return loadedSettings;
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
            logger.LogInformation("Launcher settings saved. SettingsPath={SettingsPath}", settingsPath);
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
        settings.Theme = NormalizeTheme(settings.Theme);
        var normalizedAccentColor = LauncherAccentColors.Normalize(settings.AccentColor);
        if (!string.IsNullOrWhiteSpace(settings.AccentColor)
            && !string.Equals(settings.AccentColor, normalizedAccentColor, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "Invalid launcher accent color preference encountered in settings. AccentColor={AccentColor} FallingBackTo={FallbackAccentColor}",
                settings.AccentColor,
                normalizedAccentColor);
        }

        settings.AccentColor = normalizedAccentColor;
        settings.LauncherBackgroundOpacityPercent = Math.Clamp(
            settings.LauncherBackgroundOpacityPercent,
            0,
            100);

        if (string.IsNullOrWhiteSpace(settings.DataDirectory))
            settings.DataDirectory = pathProvider.DefaultDataDirectory;

        settings.MinecraftDirectory = string.IsNullOrWhiteSpace(settings.MinecraftDirectory)
            ? Path.GetFullPath(pathProvider.DefaultMinecraftDirectory)
            : Path.GetFullPath(settings.MinecraftDirectory);

        settings.DefaultMemoryMb = Math.Clamp(settings.DefaultMemoryMb, 1024, 32768);
        if (settings.DefaultMemorySettingsMode is not MemorySettingsMode.Auto
            && settings.DefaultMemorySettingsMode is not MemorySettingsMode.Manual)
        {
            settings.DefaultMemorySettingsMode = MemorySettingsMode.Auto;
        }

        if (settings.DownloadSourcePreference is not DownloadSourcePreference.Auto
            && settings.DownloadSourcePreference is not DownloadSourcePreference.Official
            && settings.DownloadSourcePreference is not DownloadSourcePreference.BmclApi)
        {
            settings.DownloadSourcePreference = DownloadSourcePreference.Auto;
        }

        if (settings.DownloadSpeedLimitMbPerSecond < 0)
            settings.DownloadSpeedLimitMbPerSecond = 0;

        if (settings.JavaSelectionMode is not JavaSelectionMode.Auto
            && settings.JavaSelectionMode is not JavaSelectionMode.Manual)
        {
            settings.JavaSelectionMode = JavaSelectionMode.Auto;
        }

        if (string.IsNullOrWhiteSpace(settings.SelectedJavaExecutablePath))
            settings.SelectedJavaExecutablePath = null;

        return settings;
    }

    private static string NormalizeTheme(string? theme)
    {
        if (string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase))
            return "Light";

        if (string.Equals(theme, LauncherDefaults.DefaultTheme, StringComparison.OrdinalIgnoreCase))
            return LauncherDefaults.DefaultTheme;

        return LauncherDefaults.DefaultTheme;
    }
}
