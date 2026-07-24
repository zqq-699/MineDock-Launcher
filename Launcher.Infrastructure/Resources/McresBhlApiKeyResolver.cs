/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO;
using Launcher.Application.Services;
using Launcher.Infrastructure.Minecraft;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Resources;

public sealed class McresBhlApiKeyResolver : IMcresBhlApiKeyResolver
{
    private const string ApiKeyEnvironmentVariable = "BHL_API_KEY";
    private const string LocalSecretsDirectoryName = ".local-secrets";
    private const string ApiKeyFileName = "mcres-bhl.key";
    private const string EmbeddedApiKeyResourceName = "Launcher.Infrastructure.Resources.mcres-bhl.key";

    private readonly LauncherPathProvider pathProvider;
    private readonly ISettingsService? settingsService;
    private readonly ILogger<McresBhlApiKeyResolver> logger;
    private readonly Func<string> currentDirectoryProvider;
    private readonly Func<string> userProfileDirectoryProvider;
    private readonly Func<string?> environmentApiKeyProvider;
    private readonly Func<CancellationToken, Task<string?>> embeddedApiKeyProvider;

    public McresBhlApiKeyResolver(
        LauncherPathProvider? pathProvider = null,
        ISettingsService? settingsService = null,
        ILogger<McresBhlApiKeyResolver>? logger = null,
        Func<string>? currentDirectoryProvider = null,
        Func<string>? userProfileDirectoryProvider = null,
        Func<string?>? environmentApiKeyProvider = null,
        Func<CancellationToken, Task<string?>>? embeddedApiKeyProvider = null)
    {
        this.pathProvider = pathProvider ?? new LauncherPathProvider();
        this.settingsService = settingsService;
        this.logger = logger ?? NullLogger<McresBhlApiKeyResolver>.Instance;
        this.currentDirectoryProvider = currentDirectoryProvider ?? Directory.GetCurrentDirectory;
        this.userProfileDirectoryProvider = userProfileDirectoryProvider
            ?? (() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        this.environmentApiKeyProvider = environmentApiKeyProvider
            ?? (() => Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable));
        this.embeddedApiKeyProvider = embeddedApiKeyProvider ?? ReadEmbeddedApiKeyAsync;
    }

    public async Task<string?> TryResolveAsync(CancellationToken cancellationToken = default)
    {
        var embeddedApiKey = await embeddedApiKeyProvider(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(embeddedApiKey))
        {
            logger.LogDebug(
                "Resolved MCRES BHL API key from embedded resource. ResourceName={ResourceName}",
                EmbeddedApiKeyResourceName);
            return embeddedApiKey.Trim();
        }

        foreach (var dataDirectory in await EnumerateLauncherDataDirectoriesAsync(cancellationToken).ConfigureAwait(false))
        {
            var keyPath = Path.Combine(dataDirectory, LocalSecretsDirectoryName, ApiKeyFileName);
            try
            {
                if (!File.Exists(keyPath))
                    continue;

                var value = (await File.ReadAllTextAsync(keyPath, cancellationToken).ConfigureAwait(false)).Trim();
                if (string.IsNullOrWhiteSpace(value))
                {
                    logger.LogDebug("Ignored empty MCRES BHL API key file. KeyPath={KeyPath}", keyPath);
                    continue;
                }

                logger.LogDebug("Resolved MCRES BHL API key from local secret file. KeyPath={KeyPath}", keyPath);
                return value;
            }
            catch (IOException exception)
            {
                logger.LogDebug(exception, "Failed to read local MCRES BHL API key file. KeyPath={KeyPath}", keyPath);
            }
            catch (UnauthorizedAccessException exception)
            {
                logger.LogDebug(exception, "Failed to access local MCRES BHL API key file. KeyPath={KeyPath}", keyPath);
            }
        }

        var environmentApiKey = environmentApiKeyProvider();
        if (!string.IsNullOrWhiteSpace(environmentApiKey))
        {
            logger.LogDebug(
                "Resolved MCRES BHL API key from environment variable. VariableName={VariableName}",
                ApiKeyEnvironmentVariable);
            return environmentApiKey.Trim();
        }

        return null;
    }

    private static async Task<string?> ReadEmbeddedApiKeyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var stream = typeof(McresBhlApiKeyResolver)
                .Assembly
                .GetManifestResourceStream(EmbeddedApiKeyResourceName);

            if (stream is null)
                return null;

            using var reader = new StreamReader(stream);
            var value = (await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false)).Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<string>> EnumerateLauncherDataDirectoriesAsync(
        CancellationToken cancellationToken)
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
                logger.LogDebug(exception, "Failed to load settings while resolving MCRES BHL API key paths.");
            }
        }

        return directories
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
