using System.IO;
using Launcher.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.CurseForge;

public sealed class CurseForgeApiKeyResolver : ICurseForgeApiKeyResolver
{
    private const string CurseForgeApiKeyEnvironmentVariable = "CURSEFORGE_API_KEY";
    private const string LocalSecretsDirectoryName = ".local-secrets";
    private const string CurseForgeApiKeyFileName = "curseforge.key";

    private readonly LauncherPathProvider pathProvider;
    private readonly ISettingsService? settingsService;
    private readonly ILogger<CurseForgeApiKeyResolver> logger;
    private readonly Func<string> currentDirectoryProvider;
    private readonly Func<string> userProfileDirectoryProvider;

    public CurseForgeApiKeyResolver(
        LauncherPathProvider? pathProvider = null,
        ISettingsService? settingsService = null,
        ILogger<CurseForgeApiKeyResolver>? logger = null,
        Func<string>? currentDirectoryProvider = null,
        Func<string>? userProfileDirectoryProvider = null)
    {
        this.pathProvider = pathProvider ?? new LauncherPathProvider();
        this.settingsService = settingsService;
        this.logger = logger ?? NullLogger<CurseForgeApiKeyResolver>.Instance;
        this.currentDirectoryProvider = currentDirectoryProvider ?? Directory.GetCurrentDirectory;
        this.userProfileDirectoryProvider = userProfileDirectoryProvider
            ?? (() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    public async Task<string?> TryResolveAsync(CancellationToken cancellationToken = default)
    {
        foreach (var dataDirectory in await EnumerateLauncherDataDirectoriesAsync(cancellationToken).ConfigureAwait(false))
        {
            var keyPath = Path.Combine(dataDirectory, LocalSecretsDirectoryName, CurseForgeApiKeyFileName);
            try
            {
                if (!File.Exists(keyPath))
                    continue;

                var value = (await File.ReadAllTextAsync(keyPath, cancellationToken).ConfigureAwait(false)).Trim();
                if (string.IsNullOrWhiteSpace(value))
                {
                    logger.LogWarning("Ignored empty CurseForge API key file. KeyPath={KeyPath}", keyPath);
                    continue;
                }

                logger.LogInformation("Resolved CurseForge API key from local secret file. KeyPath={KeyPath}", keyPath);
                return value;
            }
            catch (IOException exception)
            {
                logger.LogWarning(exception, "Failed to read local CurseForge API key file. KeyPath={KeyPath}", keyPath);
            }
            catch (UnauthorizedAccessException exception)
            {
                logger.LogWarning(exception, "Failed to access local CurseForge API key file. KeyPath={KeyPath}", keyPath);
            }
        }

        var apiKey = Environment.GetEnvironmentVariable(CurseForgeApiKeyEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogInformation(
                "Resolved CurseForge API key from environment variable. VariableName={VariableName}",
                CurseForgeApiKeyEnvironmentVariable);
            return apiKey.Trim();
        }

        return null;
    }

    private async Task<IReadOnlyList<string>> EnumerateLauncherDataDirectoriesAsync(CancellationToken cancellationToken)
    {
        var directories = new List<string> { pathProvider.DefaultDataDirectory };
        var currentDirectory = currentDirectoryProvider();
        if (!string.IsNullOrWhiteSpace(currentDirectory))
            directories.Add(currentDirectory);

        var userProfileDirectory = userProfileDirectoryProvider();
        if (!string.IsNullOrWhiteSpace(userProfileDirectory))
            directories.Add(Path.Combine(userProfileDirectory, "Documents", "launcher"));

        if (settingsService is not null)
        {
            try
            {
                var settings = await settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(settings.DataDirectory))
                    directories.Add(settings.DataDirectory);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Failed to load settings while resolving local CurseForge API key path.");
            }
        }

        return directories
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
