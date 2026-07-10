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

using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Launcher.Application;
using Microsoft.Extensions.Logging;

namespace Launcher.Infrastructure.Persistence;

internal sealed class VersionRenameTransaction
{
    private const int MaxMoveAttempts = 5;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(150);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly VersionDirectoryManager directoryManager;
    private readonly Func<string, string, CancellationToken, Task> moveDirectoryAsync;
    private readonly ILogger logger;

    public VersionRenameTransaction(
        VersionDirectoryManager directoryManager,
        ILogger logger,
        Func<string, string, CancellationToken, Task>? moveDirectoryAsync = null)
    {
        this.directoryManager = directoryManager;
        this.logger = logger;
        this.moveDirectoryAsync = moveDirectoryAsync ?? MoveDirectoryAsync;
    }

    public async Task ExecuteAsync(
        string minecraftDirectory,
        string oldVersionName,
        string newVersionName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(oldVersionName)
            || string.IsNullOrWhiteSpace(newVersionName)
            || string.Equals(oldVersionName, newVersionName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var sourceDirectory = directoryManager.GetVersionDirectory(minecraftDirectory, oldVersionName);
        var destinationDirectory = directoryManager.GetVersionDirectory(minecraftDirectory, newVersionName);
        var sourceJsonPath = Path.Combine(sourceDirectory, $"{oldVersionName}.json");
        var destinationJsonPath = Path.Combine(destinationDirectory, $"{newVersionName}.json");
        var destinationJarPath = Path.Combine(destinationDirectory, $"{newVersionName}.jar");
        var movedJsonPath = Path.Combine(destinationDirectory, $"{oldVersionName}.json");
        var movedJarPath = Path.Combine(destinationDirectory, $"{oldVersionName}.jar");
        var temporaryJsonPath = Path.Combine(destinationDirectory, $"{LauncherApplicationIdentity.StorageDirectoryName}-rename-{Guid.NewGuid():N}.json.tmp");
        var backupJsonPath = Path.Combine(destinationDirectory, $"{LauncherApplicationIdentity.StorageDirectoryName}-rename-{Guid.NewGuid():N}.json.bak");

        if (!Directory.Exists(sourceDirectory))
            throw new DirectoryNotFoundException($"Version directory not found: {sourceDirectory}");
        if (!File.Exists(sourceJsonPath))
            throw new FileNotFoundException($"Version JSON not found: {sourceJsonPath}", sourceJsonPath);
        if (Directory.Exists(destinationDirectory))
            throw new IOException($"Version directory already exists: {destinationDirectory}");

        var versionJson = await ReadVersionJsonAsync(sourceJsonPath, cancellationToken).ConfigureAwait(false);
        RewriteIdentity(versionJson, oldVersionName, newVersionName);
        var rewrittenJson = versionJson.ToJsonString(JsonOptions);
        var oldJarExists = File.Exists(Path.Combine(sourceDirectory, $"{oldVersionName}.jar"));
        var stopwatch = Stopwatch.StartNew();
        var directoryMoved = false;
        var jsonMoved = false;
        var jarMoved = false;
        var jsonReplaced = false;

        try
        {
            await MoveWithRetryAsync(sourceDirectory, destinationDirectory, cancellationToken).ConfigureAwait(false);
            directoryMoved = true;
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(movedJsonPath, destinationJsonPath);
            jsonMoved = true;
            cancellationToken.ThrowIfCancellationRequested();
            if (oldJarExists)
            {
                File.Move(movedJarPath, destinationJarPath);
                jarMoved = true;
            }
            cancellationToken.ThrowIfCancellationRequested();
            await File.WriteAllTextAsync(temporaryJsonPath, rewrittenJson, cancellationToken).ConfigureAwait(false);
            File.Replace(temporaryJsonPath, destinationJsonPath, backupJsonPath, ignoreMetadataErrors: true);
            jsonReplaced = true;
            TryDelete(backupJsonPath);
            logger.LogInformation(
                "Version directory renamed. OldVersionName={OldVersionName} NewVersionName={NewVersionName} ElapsedMs={ElapsedMs}",
                oldVersionName, newVersionName, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception exception)
        {
            var rollbackSucceeded = TryRollback(
                sourceDirectory, destinationDirectory, movedJsonPath, destinationJsonPath,
                movedJarPath, destinationJarPath, temporaryJsonPath, backupJsonPath,
                directoryMoved, jsonMoved, jarMoved, jsonReplaced);
            logger.LogError(
                exception,
                "Version directory rename failed. OldVersionName={OldVersionName} NewVersionName={NewVersionName} RollbackSucceeded={RollbackSucceeded}",
                oldVersionName, newVersionName, rollbackSucceeded);
            throw;
        }
    }

    private async Task MoveWithRetryAsync(string source, string destination, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await moveDirectoryAsync(source, destination, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception exception) when (attempt < MaxMoveAttempts && exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "Version directory move will be retried. Attempt={Attempt} MaxAttempts={MaxAttempts}", attempt, MaxMoveAttempts);
                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static Task MoveDirectoryAsync(string source, string destination, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.Move(source, destination);
        return Task.CompletedTask;
    }

    private static async Task<JsonObject> ReadVersionJsonAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return (await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException($"Version JSON is empty: {path}")).AsObject();
    }

    private static void RewriteIdentity(JsonObject json, string oldName, string newName)
    {
        json["id"] = newName;
        if (json["jar"] is JsonValue jarValue
            && string.Equals(jarValue.ToString(), oldName, StringComparison.OrdinalIgnoreCase))
        {
            json["jar"] = newName;
        }
    }

    private static bool TryRollback(
        string sourceDirectory, string destinationDirectory,
        string movedJsonPath, string destinationJsonPath,
        string movedJarPath, string destinationJarPath,
        string temporaryJsonPath, string backupJsonPath,
        bool directoryMoved, bool jsonMoved, bool jarMoved, bool jsonReplaced)
    {
        try
        {
            TryDelete(temporaryJsonPath);
            if (jsonReplaced && File.Exists(backupJsonPath))
            {
                TryDelete(destinationJsonPath);
                File.Move(backupJsonPath, destinationJsonPath);
            }
            if (jarMoved && File.Exists(destinationJarPath))
                File.Move(destinationJarPath, movedJarPath);
            if (jsonMoved && File.Exists(destinationJsonPath))
                File.Move(destinationJsonPath, movedJsonPath);
            if (directoryMoved && Directory.Exists(destinationDirectory) && !Directory.Exists(sourceDirectory))
                Directory.Move(destinationDirectory, sourceDirectory);
            TryDelete(backupJsonPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
