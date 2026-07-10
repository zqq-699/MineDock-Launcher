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
using System.IO.Compression;
using System.Text.Json;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Modpacks;

internal static class CurseForgeModpackFormatReader
{
    public static bool HasManifest(ZipArchive archive)
    {
        return FindManifest(archive) is not null;
    }

    public static async Task ValidateAsync(ZipArchive archive, CancellationToken cancellationToken)
    {
        _ = await ReadCoreAsync(
            archive,
            string.Empty,
            validateOverrides: false,
            cancellationToken).ConfigureAwait(false);
    }

    public static Task<PreparedModpack> ReadAsync(
        ZipArchive archive,
        string sourceArchivePath,
        CancellationToken cancellationToken)
    {
        return ReadCoreAsync(archive, sourceArchivePath, validateOverrides: true, cancellationToken);
    }

    private static async Task<PreparedModpack> ReadCoreAsync(
        ZipArchive archive,
        string sourceArchivePath,
        bool validateOverrides,
        CancellationToken cancellationToken)
    {
        var manifestEntry = FindManifest(archive);
        if (manifestEntry is null)
        {
            throw new ModpackImportException(
                ModpackImportFailureReason.InvalidManifest,
                "manifest.json was not found.");
        }

        using var manifest = await ModpackManifestJson.ReadAsync(manifestEntry, cancellationToken).ConfigureAwait(false);
        var packageName = ModpackManifestJson.GetString(manifest.RootElement, "name");
        var minecraft = ModpackManifestJson.GetRequiredObject(manifest.RootElement, "minecraft");
        var minecraftVersion = ModpackManifestJson.GetRequiredString(minecraft, "version");
        var (loader, loaderVersion) = ParseLoader(minecraft);

        return new PreparedModpack
        {
            PackageKind = ModpackPackageKind.CurseForge,
            SourceArchivePath = sourceArchivePath,
            PackageName = string.IsNullOrWhiteSpace(packageName)
                ? Path.GetFileNameWithoutExtension(sourceArchivePath)
                : packageName,
            MinecraftVersion = minecraftVersion,
            Loader = loader,
            LoaderVersion = loaderVersion,
            HasOverrides = validateOverrides && ModpackOverrideExtractor.HasCurseForgeOverrides(archive),
            Files = ParseFiles(manifest.RootElement)
        };
    }

    private static ZipArchiveEntry? FindManifest(ZipArchive archive)
    {
        return archive.Entries.FirstOrDefault(entry =>
            string.Equals(
                ModpackArchiveUtility.NormalizeArchivePath(entry.FullName),
                "manifest.json",
                StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<PreparedModpackDownload> ParseFiles(JsonElement root)
    {
        if (!root.TryGetProperty("files", out var files) || files.ValueKind is not JsonValueKind.Array)
            return [];

        var manifestFiles = new List<PreparedModpackDownload>(files.GetArrayLength());
        foreach (var file in files.EnumerateArray())
        {
            if (!file.TryGetProperty("projectID", out var projectIdProperty)
                || !projectIdProperty.TryGetInt64(out var projectId)
                || !file.TryGetProperty("fileID", out var fileIdProperty)
                || !fileIdProperty.TryGetInt64(out var fileId))
            {
                throw new ModpackImportException(
                    ModpackImportFailureReason.InvalidManifest,
                    "CurseForge manifest file entry is missing projectID or fileID.");
            }

            manifestFiles.Add(new PreparedModpackDownload
            {
                ProjectId = projectId,
                FileId = fileId,
                TargetDirectory = "mods"
            });
        }

        return manifestFiles;
    }

    private static (LoaderKind Loader, string? LoaderVersion) ParseLoader(JsonElement minecraft)
    {
        if (!minecraft.TryGetProperty("modLoaders", out var modLoaders)
            || modLoaders.ValueKind is not JsonValueKind.Array
            || modLoaders.GetArrayLength() == 0)
        {
            return (LoaderKind.Vanilla, null);
        }

        JsonElement? selected = null;
        foreach (var modLoader in modLoaders.EnumerateArray())
        {
            if (modLoader.TryGetProperty("primary", out var primaryProperty)
                && primaryProperty.ValueKind is JsonValueKind.True)
            {
                selected = modLoader;
                break;
            }

            selected ??= modLoader;
        }

        if (selected is null)
            return (LoaderKind.Vanilla, null);

        var id = ModpackManifestJson.GetRequiredString(selected.Value, "id");
        if (id.StartsWith("forge-", StringComparison.OrdinalIgnoreCase))
            return (LoaderKind.Forge, id["forge-".Length..]);
        if (id.StartsWith("fabric-", StringComparison.OrdinalIgnoreCase))
            return (LoaderKind.Fabric, id["fabric-".Length..]);
        if (id.StartsWith("neoforge-", StringComparison.OrdinalIgnoreCase))
            return (LoaderKind.NeoForge, id["neoforge-".Length..]);
        if (id.StartsWith("quilt-", StringComparison.OrdinalIgnoreCase))
            return (LoaderKind.Quilt, id["quilt-".Length..]);

        throw new ModpackImportException(
            ModpackImportFailureReason.UnsupportedLoader,
            $"Unsupported CurseForge loader: {id}");
    }
}
