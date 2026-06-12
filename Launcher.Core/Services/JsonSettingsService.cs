using System.Text.Json;
using Launcher.Core.Models;

namespace Launcher.Core.Services;

public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string settingsPath;

    public JsonSettingsService(string? dataDirectory = null)
    {
        var root = dataDirectory ?? LauncherDefaults.DefaultDataDirectory;
        settingsPath = Path.Combine(root, "settings.json");
    }

    public async Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(settingsPath))
        {
            var settings = new LauncherSettings
            {
                DataDirectory = Path.GetDirectoryName(settingsPath) ?? LauncherDefaults.DefaultDataDirectory
            };
            await SaveAsync(settings, cancellationToken);
            return settings;
        }

        await using var stream = File.OpenRead(settingsPath);
        var loaded = await JsonSerializer.DeserializeAsync<LauncherSettings>(stream, JsonOptions, cancellationToken);
        return Normalize(loaded ?? new LauncherSettings());
    }

    public async Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        await using var stream = File.Create(settingsPath);
        await JsonSerializer.SerializeAsync(stream, normalized, JsonOptions, cancellationToken);
    }

    private static LauncherSettings Normalize(LauncherSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.OfflineUsername))
            settings.OfflineUsername = "Player";

        if (string.IsNullOrWhiteSpace(settings.DataDirectory))
            settings.DataDirectory = LauncherDefaults.DefaultDataDirectory;

        settings.DefaultMemoryMb = Math.Clamp(settings.DefaultMemoryMb, 1024, 32768);
        return settings;
    }
}
