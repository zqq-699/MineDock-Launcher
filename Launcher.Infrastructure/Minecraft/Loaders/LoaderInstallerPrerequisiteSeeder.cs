/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Launcher.Infrastructure.Minecraft;

internal sealed class LoaderInstallerPrerequisiteSeeder
{
    private static readonly string[] SharedDirectoryNames = ["libraries", "assets", "resources", "runtime"];

    public async Task<LoaderInstallerWorkspaceSnapshot> SeedAsync(
        string sharedMinecraftDirectory,
        string installerMinecraftDirectory,
        string minecraftVersion,
        string installerJarPath,
        CancellationToken cancellationToken)
    {
        var seededFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await SeedBaseVersionAsync(
            sharedMinecraftDirectory,
            installerMinecraftDirectory,
            minecraftVersion,
            seededFiles,
            cancellationToken).ConfigureAwait(false);
        await SeedInstallerLibrariesAsync(
            sharedMinecraftDirectory,
            installerMinecraftDirectory,
            installerJarPath,
            seededFiles,
            cancellationToken).ConfigureAwait(false);
        return new LoaderInstallerWorkspaceSnapshot(installerMinecraftDirectory, seededFiles);
    }

    public async Task PublishDeltaAsync(
        LoaderInstallerWorkspaceSnapshot snapshot,
        string destinationMinecraftDirectory,
        CancellationToken cancellationToken)
    {
        foreach (var directoryName in SharedDirectoryNames)
        {
            var sourceDirectory = Path.Combine(snapshot.WorkspaceMinecraftDirectory, directoryName);
            if (!Directory.Exists(sourceDirectory))
                continue;

            foreach (var sourcePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var confinedSourcePath = MinecraftPathGuard.EnsureWithin(
                    sourcePath,
                    sourceDirectory,
                    "Loader installer shared output");
                var relativePath = NormalizeRelativePath(Path.GetRelativePath(snapshot.WorkspaceMinecraftDirectory, sourcePath));
                if (snapshot.SeededFiles.TryGetValue(relativePath, out var seededSha1))
                {
                    var sourceSha1 = AtomicSharedFilePublisher.ComputeSha1(confinedSourcePath);
                    if (!string.Equals(sourceSha1, seededSha1, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"Loader installer modified a seeded prerequisite: {relativePath}");
                    continue;
                }

                await AtomicSharedFilePublisher.PublishCopyAsync(
                    confinedSourcePath,
                    MinecraftPathGuard.EnsureWithin(
                        Path.Combine(destinationMinecraftDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)),
                        Path.Combine(destinationMinecraftDirectory, directoryName),
                        "Loader installer shared destination"),
                    expectedSha1: null,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task SeedBaseVersionAsync(
        string sourceGameDirectory,
        string destinationGameDirectory,
        string minecraftVersion,
        IDictionary<string, string> seededFiles,
        CancellationToken cancellationToken)
    {
        var sourceVersionDirectory = Path.Combine(sourceGameDirectory, "versions", minecraftVersion);
        var sourceJsonPath = Path.Combine(sourceVersionDirectory, $"{minecraftVersion}.json");
        if (!File.Exists(sourceJsonPath))
            return;

        JsonObject root;
        try
        {
            root = JsonNode.Parse(await File.ReadAllTextAsync(sourceJsonPath, cancellationToken).ConfigureAwait(false)) as JsonObject
                ?? throw new InvalidDataException("Base version metadata is not an object.");
        }
        catch (JsonException)
        {
            return;
        }

        if (GetString(root["id"]) is { } id
            && !string.Equals(id, minecraftVersion, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await SeedFileAsync(
            sourceJsonPath,
            Path.Combine(destinationGameDirectory, "versions", minecraftVersion, $"{minecraftVersion}.json"),
            expectedSha1: null,
            expectedSize: null,
            destinationGameDirectory,
            seededFiles,
            cancellationToken).ConfigureAwait(false);

        var client = root["downloads"]?["client"] as JsonObject;
        await SeedFileAsync(
            Path.Combine(sourceVersionDirectory, $"{minecraftVersion}.jar"),
            Path.Combine(destinationGameDirectory, "versions", minecraftVersion, $"{minecraftVersion}.jar"),
            GetString(client?["sha1"]),
            GetLong(client?["size"]),
            destinationGameDirectory,
            seededFiles,
            cancellationToken).ConfigureAwait(false);

        await SeedLibrariesFromJsonAsync(
            root,
            sourceGameDirectory,
            destinationGameDirectory,
            seededFiles,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task SeedInstallerLibrariesAsync(
        string sourceGameDirectory,
        string destinationGameDirectory,
        string installerJarPath,
        IDictionary<string, string> seededFiles,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(installerJarPath))
            return;

        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(installerJarPath);
        }
        catch (InvalidDataException)
        {
            return;
        }

        using (archive)
        {
            foreach (var entryName in new[] { "install_profile.json", "version.json" })
            {
                var entry = archive.GetEntry(entryName);
                if (entry is null)
                    continue;

                await using var stream = entry.Open();
                JsonNode? node;
                try
                {
                    node = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (node is not null)
                {
                    await SeedLibrariesFromJsonAsync(
                        node,
                        sourceGameDirectory,
                        destinationGameDirectory,
                        seededFiles,
                        cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private static async Task SeedLibrariesFromJsonAsync(
        JsonNode root,
        string sourceGameDirectory,
        string destinationGameDirectory,
        IDictionary<string, string> seededFiles,
        CancellationToken cancellationToken)
    {
        foreach (var library in EnumerateLibraryObjects(root))
        {
            var artifacts = new List<JsonObject>();
            if (library["downloads"]?["artifact"] is JsonObject artifact)
                artifacts.Add(artifact);
            if (library["downloads"]?["classifiers"] is JsonObject classifiers)
                artifacts.AddRange(classifiers.Select(item => item.Value).OfType<JsonObject>());

            if (artifacts.Count == 0 && GetString(library["name"]) is { } coordinate)
            {
                var legacyPath = TryCreateMavenPath(coordinate);
                if (legacyPath is not null)
                    artifacts.Add(new JsonObject { ["path"] = legacyPath });
            }

            foreach (var item in artifacts)
            {
                var relativeLibraryPath = GetString(item["path"]);
                if (string.IsNullOrWhiteSpace(relativeLibraryPath))
                    continue;

                var normalizedRelativePath = relativeLibraryPath.Replace('/', Path.DirectorySeparatorChar);
                string sourcePath;
                string destinationPath;
                try
                {
                    sourcePath = MinecraftPathGuard.EnsureWithin(
                        Path.Combine(sourceGameDirectory, "libraries", normalizedRelativePath),
                        Path.Combine(sourceGameDirectory, "libraries"),
                        "Loader prerequisite source");
                    destinationPath = MinecraftPathGuard.EnsureWithin(
                        Path.Combine(destinationGameDirectory, "libraries", normalizedRelativePath),
                        Path.Combine(destinationGameDirectory, "libraries"),
                        "Loader prerequisite destination");
                }
                catch (InvalidDataException)
                {
                    continue;
                }

                await SeedFileAsync(
                    sourcePath,
                    destinationPath,
                    GetString(item["sha1"]),
                    GetLong(item["size"]),
                    destinationGameDirectory,
                    seededFiles,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static IEnumerable<JsonObject> EnumerateLibraryObjects(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            if (obj["libraries"] is JsonArray libraries)
            {
                foreach (var library in libraries.OfType<JsonObject>())
                    yield return library;
            }

            foreach (var child in obj.Select(item => item.Value).Where(value => value is not null))
            {
                foreach (var library in EnumerateLibraryObjects(child!))
                    yield return library;
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array.Where(value => value is not null))
            {
                foreach (var library in EnumerateLibraryObjects(child!))
                    yield return library;
            }
        }
    }

    private static async Task SeedFileAsync(
        string sourcePath,
        string destinationPath,
        string? expectedSha1,
        long? expectedSize,
        string destinationGameDirectory,
        IDictionary<string, string> seededFiles,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(sourcePath))
            return;
        if (expectedSize is > 0 && new FileInfo(sourcePath).Length != expectedSize.Value)
            return;

        var actualSha1 = AtomicSharedFilePublisher.ComputeSha1(sourcePath);
        if (!string.IsNullOrWhiteSpace(expectedSha1)
            && !string.Equals(actualSha1, expectedSha1, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await AtomicSharedFilePublisher.PublishCopyAsync(
            sourcePath,
            destinationPath,
            actualSha1,
            cancellationToken).ConfigureAwait(false);
        seededFiles[NormalizeRelativePath(Path.GetRelativePath(destinationGameDirectory, destinationPath))] = actualSha1;
    }

    private static string? TryCreateMavenPath(string coordinate)
    {
        var extensionSplit = coordinate.Split('@', 2);
        var extension = extensionSplit.Length == 2 ? extensionSplit[1] : "jar";
        var parts = extensionSplit[0].Split(':');
        if (parts.Length is < 3 or > 4 || parts.Any(string.IsNullOrWhiteSpace))
            return null;

        var groupPath = parts[0].Replace('.', '/');
        var classifier = parts.Length == 4 ? $"-{parts[3]}" : string.Empty;
        return $"{groupPath}/{parts[1]}/{parts[2]}/{parts[1]}-{parts[2]}{classifier}.{extension}";
    }

    private static string NormalizeRelativePath(string path) => path.Replace('\\', '/');

    private static string? GetString(JsonNode? node)
    {
        try
        {
            return node?.GetValue<string>();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static long? GetLong(JsonNode? node)
    {
        try
        {
            return node?.GetValue<long>();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

internal sealed record LoaderInstallerWorkspaceSnapshot(
    string WorkspaceMinecraftDirectory,
    IReadOnlyDictionary<string, string> SeededFiles);
