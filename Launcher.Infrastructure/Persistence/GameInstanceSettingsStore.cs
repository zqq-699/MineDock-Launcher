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
using System.Text.Json;
using Launcher.Application;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Launcher.Infrastructure.Persistence;

internal sealed class GameInstanceSettingsStore(ILogger logger)
{
    private const string LauncherDirectoryName = LauncherApplicationIdentity.StorageDirectoryName;
    private const string InstanceSettingsFileName = "instance-settings.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<IReadOnlyList<GameInstance>> LoadAsync(
        string minecraftDirectory,
        CancellationToken cancellationToken)
    {
        var versionsDirectory = Path.Combine(minecraftDirectory, "versions");
        if (!Directory.Exists(versionsDirectory))
            return [];

        var storedInstances = new List<GameInstance>();
        foreach (var versionDirectory in EnumerateDirectories(versionsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ShouldIgnoreDirectory(versionDirectory))
                continue;

            var settingsPath = GetSettingsPath(versionDirectory);
            if (!File.Exists(settingsPath))
                continue;

            var instance = await TryLoadAsync(settingsPath, cancellationToken).ConfigureAwait(false);
            if (instance is null)
                continue;

            var versionName = Path.GetFileName(versionDirectory);
            if (string.IsNullOrWhiteSpace(instance.VersionName))
                instance.VersionName = versionName;

            instance.InstanceDirectory = versionDirectory;
            storedInstances.Add(instance);
        }

        return storedInstances;
    }

    public async Task<int> SaveAsync(
        IReadOnlyCollection<GameInstance> instances,
        string minecraftDirectory,
        CancellationToken cancellationToken)
    {
        var persistedCount = 0;
        var persistedVersionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var instance in instances)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var versionName = GetVersionName(instance);
            if (string.IsNullOrWhiteSpace(versionName) || !persistedVersionNames.Add(versionName))
                continue;

            var versionDirectory = Path.Combine(minecraftDirectory, "versions", versionName);
            if (!Directory.Exists(versionDirectory))
                continue;

            var settingsPath = GetSettingsPath(versionDirectory);
            if (File.Exists(settingsPath))
            {
                var current = await TryLoadAsync(settingsPath, cancellationToken).ConfigureAwait(false);
                if (current is null
                    || !string.Equals(current.Id, instance.Id, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning(
                        "Skipped stale game instance settings update. ExpectedInstanceId={ExpectedInstanceId} CurrentInstanceId={CurrentInstanceId} VersionName={VersionName}",
                        instance.Id,
                        current?.Id,
                        versionName);
                    continue;
                }
            }

            var snapshot = CreateStorageSnapshot(instance, versionDirectory, versionName);
            await AtomicJsonFileWriter.WriteAsync(settingsPath, snapshot, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            persistedCount++;
        }

        return persistedCount;
    }

    public async Task UpdateAsync(
        GameInstance instance,
        string minecraftDirectory,
        CancellationToken cancellationToken)
    {
        var versionName = GetVersionName(instance);
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", versionName);
        await EnsureIdentityAsync(versionDirectory, instance.Id, cancellationToken).ConfigureAwait(false);
        var snapshot = CreateStorageSnapshot(instance, versionDirectory, versionName);
        await AtomicJsonFileWriter.WriteAsync(
                GetSettingsPath(versionDirectory),
                snapshot,
                JsonOptions,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task EnsureIdentityAsync(
        string versionDirectory,
        string expectedInstanceId,
        CancellationToken cancellationToken)
    {
        var settingsPath = GetSettingsPath(versionDirectory);
        var current = File.Exists(settingsPath)
            ? await TryLoadAsync(settingsPath, cancellationToken).ConfigureAwait(false)
            : null;
        if (current is null
            || !string.Equals(current.Id, expectedInstanceId, StringComparison.OrdinalIgnoreCase))
        {
            throw new GameInstanceMutationConflictException(
                expectedInstanceId,
                Path.GetFileName(Path.TrimEndingDirectorySeparator(versionDirectory)));
        }
    }

    public static bool HasIdentity(string versionDirectory, string expectedInstanceId)
    {
        try
        {
            var settingsPath = GetSettingsPath(versionDirectory);
            if (!File.Exists(settingsPath))
                return false;
            using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase))
                {
                    return property.Value.ValueKind == JsonValueKind.String
                        && string.Equals(
                            property.Value.GetString(),
                            expectedInstanceId,
                            StringComparison.OrdinalIgnoreCase);
                }
            }

            return false;
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or JsonException)
        {
            return false;
        }
    }

    public async Task CompleteRenameAsync(
        string versionDirectory,
        string instanceId,
        string newVersionName,
        string? newIconSource,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        var settingsPath = GetSettingsPath(versionDirectory);
        if (!File.Exists(settingsPath))
            throw new FileNotFoundException("Instance settings were not found while completing rename.", settingsPath);

        var instance = await TryLoadAsync(settingsPath, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Instance settings could not be read while completing rename.");
        if (!string.Equals(instance.Id, instanceId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Pending rename marker does not match the instance settings.");

        instance.Name = newVersionName;
        instance.VersionName = newVersionName;
        instance.InstanceDirectory = versionDirectory;
        instance.IconSource = newIconSource;
        instance.UpdatedAt = updatedAt;
        var snapshot = CreateStorageSnapshot(instance, versionDirectory, newVersionName);
        await AtomicJsonFileWriter.WriteAsync(settingsPath, snapshot, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task PrepareInstallAsync(
        string pendingDirectory,
        string finalDirectory,
        string logicalVersionName,
        GameInstance instance,
        CancellationToken cancellationToken)
    {
        var settingsPath = GetSettingsPath(pendingDirectory);
        var snapshot = CreateStorageSnapshot(instance, finalDirectory, logicalVersionName);
        return AtomicJsonFileWriter.WriteAsync(settingsPath, snapshot, JsonOptions, cancellationToken);
    }

    private async Task<GameInstance?> TryLoadAsync(string settingsPath, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(settingsPath);
            var instance = await JsonSerializer.DeserializeAsync<GameInstance>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            if (instance is not null)
                Normalize(instance);
            return instance;
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Ignored invalid game instance settings. SettingsPath={SettingsPath}", settingsPath);
            return null;
        }
        catch (IOException exception)
        {
            logger.LogWarning(exception, "Failed to read game instance settings. SettingsPath={SettingsPath}", settingsPath);
            return null;
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning(exception, "Access denied while reading game instance settings. SettingsPath={SettingsPath}", settingsPath);
            return null;
        }
    }

    private static void Normalize(GameInstance instance)
    {
        if (!Enum.IsDefined(instance.MemorySettingsMode))
            instance.MemorySettingsMode = MemorySettingsMode.Manual;
        if (instance.MemoryMb <= 0)
            instance.MemoryMb = LauncherDefaults.DefaultMemoryMb;
    }

    private static GameInstance CreateStorageSnapshot(GameInstance instance, string versionDirectory, string versionName)
    {
        return new GameInstance
        {
            Id = instance.Id,
            Name = instance.Name,
            MinecraftVersion = instance.MinecraftVersion,
            Loader = instance.Loader,
            LoaderVersion = instance.LoaderVersion,
            VersionName = versionName,
            VersionType = instance.VersionType,
            Description = instance.Description,
            IconSource = instance.IconSource,
            InstanceDirectory = versionDirectory,
            BackupDirectory = instance.BackupDirectory,
            MemorySettingsMode = instance.MemorySettingsMode,
            MemoryMb = instance.MemoryMb,
            WindowWidth = instance.WindowWidth,
            WindowHeight = instance.WindowHeight,
            PreLaunchCommand = instance.PreLaunchCommand,
            WaitForPreLaunchCommand = instance.WaitForPreLaunchCommand,
            PostExitCommand = instance.PostExitCommand,
            JvmArguments = instance.JvmArguments,
            GameArguments = instance.GameArguments,
            LaunchSettingsMode = instance.LaunchSettingsMode,
            JavaSettingsMode = instance.JavaSettingsMode,
            JavaSelectionMode = instance.JavaSelectionMode,
            SelectedJavaExecutablePath = instance.SelectedJavaExecutablePath,
            CheckFilesBeforeLaunch = instance.CheckFilesBeforeLaunch,
            AutoRepairMissingFiles = instance.AutoRepairMissingFiles,
            MinimizeLauncherAfterLaunch = instance.MinimizeLauncherAfterLaunch,
            LaunchFullScreen = instance.LaunchFullScreen,
            CreatedAt = instance.CreatedAt,
            UpdatedAt = instance.UpdatedAt
        };
    }

    private static string GetSettingsPath(string versionDirectory)
        => Path.Combine(versionDirectory, LauncherDirectoryName, InstanceSettingsFileName);

    private static bool ShouldIgnoreDirectory(string versionDirectory)
    {
        if (PendingInstanceDeletionDirectory.IsPending(versionDirectory)
            || PendingInstanceInstallDirectory.IsPending(versionDirectory)
            || PendingInstanceRenameDirectory.IsPending(versionDirectory))
        {
            return true;
        }

        var markerStatus = PendingInstanceRenameMarkerFile.Read(versionDirectory).Status;
        return markerStatus is PendingInstanceRenameMarkerStatus.Valid
            or PendingInstanceRenameMarkerStatus.Unreadable;
    }

    private static string GetVersionName(GameInstance instance)
        => string.IsNullOrWhiteSpace(instance.VersionName) ? instance.MinecraftVersion : instance.VersionName;

    private static IEnumerable<string> EnumerateDirectories(string directory)
    {
        try
        {
            return Directory.EnumerateDirectories(directory).ToArray();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }
}
