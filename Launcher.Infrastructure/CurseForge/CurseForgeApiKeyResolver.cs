/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

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
    private const string EmbeddedCurseForgeApiKeyResourceName = "Launcher.Infrastructure.CurseForge.curseforge.key";

    private readonly LauncherPathProvider pathProvider;
    private readonly ISettingsService? settingsService;
    private readonly ILogger<CurseForgeApiKeyResolver> logger;
    private readonly Func<string> currentDirectoryProvider;
    private readonly Func<string> userProfileDirectoryProvider;
    private readonly Func<string?> environmentApiKeyProvider;
    private readonly Func<CancellationToken, Task<string?>> embeddedApiKeyProvider;

    public CurseForgeApiKeyResolver(
        LauncherPathProvider? pathProvider = null,
        ISettingsService? settingsService = null,
        ILogger<CurseForgeApiKeyResolver>? logger = null,
        Func<string>? currentDirectoryProvider = null,
        Func<string>? userProfileDirectoryProvider = null,
        Func<string?>? environmentApiKeyProvider = null,
        Func<CancellationToken, Task<string?>>? embeddedApiKeyProvider = null)
    {
        this.pathProvider = pathProvider ?? new LauncherPathProvider();
        this.settingsService = settingsService;
        this.logger = logger ?? NullLogger<CurseForgeApiKeyResolver>.Instance;
        this.currentDirectoryProvider = currentDirectoryProvider ?? Directory.GetCurrentDirectory;
        this.userProfileDirectoryProvider = userProfileDirectoryProvider
            ?? (() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        this.environmentApiKeyProvider = environmentApiKeyProvider
            ?? (() => Environment.GetEnvironmentVariable(CurseForgeApiKeyEnvironmentVariable));
        this.embeddedApiKeyProvider = embeddedApiKeyProvider ?? ReadEmbeddedApiKeyAsync;
    }

    public async Task<string?> TryResolveAsync(CancellationToken cancellationToken = default)
    {
        var embeddedApiKey = await embeddedApiKeyProvider(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(embeddedApiKey))
        {
            logger.LogDebug(
                "Resolved CurseForge API key from embedded resource. ResourceName={ResourceName}",
                EmbeddedCurseForgeApiKeyResourceName);
            return embeddedApiKey.Trim();
        }

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
                    logger.LogDebug("Ignored empty CurseForge API key file. KeyPath={KeyPath}", keyPath);
                    continue;
                }

                logger.LogDebug("Resolved CurseForge API key from local secret file. KeyPath={KeyPath}", keyPath);
                return value;
            }
            catch (IOException exception)
            {
                logger.LogDebug(exception, "Failed to read local CurseForge API key file. KeyPath={KeyPath}", keyPath);
            }
            catch (UnauthorizedAccessException exception)
            {
                logger.LogDebug(exception, "Failed to access local CurseForge API key file. KeyPath={KeyPath}", keyPath);
            }
        }

        var apiKey = environmentApiKeyProvider();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogDebug(
                "Resolved CurseForge API key from environment variable. VariableName={VariableName}",
                CurseForgeApiKeyEnvironmentVariable);
            return apiKey.Trim();
        }

        return null;
    }

    private async Task<string?> ReadEmbeddedApiKeyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var stream = typeof(CurseForgeApiKeyResolver)
                .Assembly
                .GetManifestResourceStream(EmbeddedCurseForgeApiKeyResourceName);

            if (stream is null)
                return null;

            using var reader = new StreamReader(stream);
            var value = (await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false)).Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                logger.LogDebug(
                    "Ignored empty embedded CurseForge API key resource. ResourceName={ResourceName}",
                    EmbeddedCurseForgeApiKeyResourceName);
                return null;
            }

            return value;
        }
        catch (IOException exception)
        {
            logger.LogDebug(
                exception,
                "Failed to read embedded CurseForge API key resource. ResourceName={ResourceName}",
                EmbeddedCurseForgeApiKeyResourceName);
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogDebug(
                exception,
                "Failed to access embedded CurseForge API key resource. ResourceName={ResourceName}",
                EmbeddedCurseForgeApiKeyResourceName);
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
                logger.LogDebug(exception, "Failed to load settings while resolving local CurseForge API key path.");
            }
        }

        return directories
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
