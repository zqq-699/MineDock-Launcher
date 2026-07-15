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

using Launcher.Application.Repositories;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Minecraft;

internal interface ILoaderInstallerJavaRuntimeResolver
{
    Task<JavaRuntimeInfo> ResolveAsync(
        string minecraftVersion,
        string versionName,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves the validated launcher-managed Java runtime used by Forge-like installers.
/// Existing instances retain their per-instance selection; new installs inherit global settings.
/// </summary>
internal sealed class LoaderInstallerJavaRuntimeResolver : ILoaderInstallerJavaRuntimeResolver
{
    private readonly Func<CancellationToken, Task<LauncherSettings>> loadSettingsAsync;
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<GameInstance>>> loadInstancesAsync;
    private readonly IJavaRuntimeSelectionService javaRuntimeSelectionService;
    private readonly ILogger logger;

    public LoaderInstallerJavaRuntimeResolver(
        ISettingsService settingsService,
        IGameInstanceRepository instanceRepository,
        IJavaRuntimeSelectionService javaRuntimeSelectionService,
        ILogger<LoaderInstallerJavaRuntimeResolver>? logger = null)
    {
        loadSettingsAsync = settingsService.LoadAsync;
        loadInstancesAsync = instanceRepository.GetAllAsync;
        this.javaRuntimeSelectionService = javaRuntimeSelectionService;
        this.logger = logger ?? NullLogger<LoaderInstallerJavaRuntimeResolver>.Instance;
    }

    internal LoaderInstallerJavaRuntimeResolver(
        Func<CancellationToken, Task<LauncherSettings>> loadSettingsAsync,
        Func<string, CancellationToken, Task<IReadOnlyList<GameInstance>>> loadInstancesAsync,
        IJavaRuntimeSelectionService javaRuntimeSelectionService,
        ILogger? logger = null)
    {
        this.loadSettingsAsync = loadSettingsAsync;
        this.loadInstancesAsync = loadInstancesAsync;
        this.javaRuntimeSelectionService = javaRuntimeSelectionService;
        this.logger = logger ?? NullLogger.Instance;
    }

    public async Task<JavaRuntimeInfo> ResolveAsync(
        string minecraftVersion,
        string versionName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(versionName);

        var settings = await loadSettingsAsync(cancellationToken).ConfigureAwait(false);
        var instances = await loadInstancesAsync(settings.MinecraftDirectory, cancellationToken)
            .ConfigureAwait(false);
        var storedInstance = instances.FirstOrDefault(instance =>
                string.Equals(instance.VersionName, versionName, StringComparison.OrdinalIgnoreCase))
            ?? instances.FirstOrDefault(instance =>
                string.Equals(instance.Name, versionName, StringComparison.OrdinalIgnoreCase));
        var selectionInstance = CreateSelectionInstance(storedInstance, minecraftVersion, versionName);

        var runtime = await javaRuntimeSelectionService
            .SelectForLaunchAsync(selectionInstance, settings, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        logger.LogInformation(
            "Resolved Java runtime for loader installer. VersionName={VersionName} MinecraftVersion={MinecraftVersion} UsedStoredInstance={UsedStoredInstance} JavaPath={JavaPath} JavaVersion={JavaVersion} JavaArchitecture={JavaArchitecture} JavaSource={JavaSource}",
            versionName,
            minecraftVersion,
            storedInstance is not null,
            runtime.ExecutablePath,
            runtime.Version,
            runtime.Architecture,
            runtime.Source);
        return runtime;
    }

    private static GameInstance CreateSelectionInstance(
        GameInstance? storedInstance,
        string minecraftVersion,
        string versionName)
    {
        if (storedInstance is null)
        {
            return new GameInstance
            {
                MinecraftVersion = minecraftVersion,
                VersionName = versionName,
                JavaSettingsMode = LaunchSettingsMode.UseGlobal
            };
        }

        return new GameInstance
        {
            Id = storedInstance.Id,
            Name = storedInstance.Name,
            MinecraftVersion = string.IsNullOrWhiteSpace(storedInstance.MinecraftVersion)
                ? minecraftVersion
                : storedInstance.MinecraftVersion,
            VersionName = string.IsNullOrWhiteSpace(storedInstance.VersionName)
                ? versionName
                : storedInstance.VersionName,
            InstanceDirectory = storedInstance.InstanceDirectory,
            JavaSettingsMode = storedInstance.JavaSettingsMode,
            JavaSelectionMode = storedInstance.JavaSelectionMode,
            SelectedJavaExecutablePath = storedInstance.SelectedJavaExecutablePath
        };
    }
}
