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
using System.Text.Json.Nodes;

namespace Launcher.Infrastructure.Minecraft;

internal static class LoaderVersionDirectoryTransaction
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static HashSet<string> CaptureExistingVersions(string gameDirectory)
    {
        var versionsDirectory = Path.Combine(gameDirectory, "versions");
        if (!Directory.Exists(versionsDirectory))
            return [];

        return Directory.GetDirectories(versionsDirectory)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static void EnsureLauncherProfileExists(string gameDirectory)
    {
        Directory.CreateDirectory(gameDirectory);

        var launcherProfilesPath = Path.Combine(gameDirectory, "launcher_profiles.json");
        var microsoftStoreProfilesPath = Path.Combine(gameDirectory, "launcher_profiles_microsoft_store.json");
        if (File.Exists(launcherProfilesPath) || File.Exists(microsoftStoreProfilesPath))
            return;

        File.WriteAllText(
            launcherProfilesPath,
            """
            {
              "profiles": {}
            }
            """);
    }

    public static async Task WriteLauncherMetadataAsync(
        string gameDirectory,
        string versionName,
        string minecraftVersion,
        CancellationToken cancellationToken)
    {
        var versionJsonPath = Path.Combine(gameDirectory, "versions", versionName, $"{versionName}.json");
        JsonNode versionJson;
        await using (var jsonStream = File.OpenRead(versionJsonPath))
        {
            versionJson = await JsonNode.ParseAsync(jsonStream, cancellationToken: cancellationToken)
                ?? throw new InvalidDataException($"Version JSON is empty: {versionJsonPath}");
        }

        var versionObject = versionJson.AsObject();
        LauncherVersionMetadata.Apply(versionObject, minecraftVersion);
        await File.WriteAllTextAsync(
            versionJsonPath,
            versionObject.ToJsonString(JsonOptions),
            cancellationToken);
    }

    public static void CopyFinalVersionDirectory(
        string sourceGameDirectory,
        string destinationGameDirectory,
        string versionName)
    {
        MinecraftVersionDirectoryCopier.CopyVersionDirectory(
            sourceGameDirectory,
            destinationGameDirectory,
            versionName,
            allowExistingDestination: true);
    }

    public static void CleanupCreatedVersionDirectories(
        string gameDirectory,
        HashSet<string> existingVersionNames,
        string? preserveVersionName)
    {
        var versionsDirectory = Path.Combine(gameDirectory, "versions");
        if (!Directory.Exists(versionsDirectory))
            return;

        foreach (var directory in Directory.GetDirectories(versionsDirectory))
        {
            var versionName = Path.GetFileName(directory);
            if (string.IsNullOrWhiteSpace(versionName)
                || existingVersionNames.Contains(versionName)
                || string.Equals(versionName, preserveVersionName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            TryDeleteDirectory(directory);
        }
    }

    public static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
