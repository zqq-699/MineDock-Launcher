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

internal static class ModrinthModpackFormatReader
{
    public static async Task ValidateAsync(ZipArchive archive, CancellationToken cancellationToken)
    {
        _ = await ReadCoreAsync(
            archive,
            string.Empty,
            embeddedEntryName: null,
            validateOverrides: false,
            cancellationToken).ConfigureAwait(false);
    }

    public static Task<PreparedModpack> ReadAsync(
        ZipArchive archive,
        string sourceArchivePath,
        string? embeddedEntryName,
        CancellationToken cancellationToken)
    {
        return ReadCoreAsync(
            archive,
            sourceArchivePath,
            embeddedEntryName,
            validateOverrides: true,
            cancellationToken);
    }

    private static async Task<PreparedModpack> ReadCoreAsync(
        ZipArchive archive,
        string sourceArchivePath,
        string? embeddedEntryName,
        bool validateOverrides,
        CancellationToken cancellationToken)
    {
        var indexEntry = archive.Entries.FirstOrDefault(entry =>
            string.Equals(
                ModpackArchiveUtility.NormalizeArchivePath(entry.FullName),
                "modrinth.index.json",
                StringComparison.OrdinalIgnoreCase));
        if (indexEntry is null)
        {
            throw new ModpackImportException(
                ModpackImportFailureReason.InvalidManifest,
                "modrinth.index.json was not found.");
        }

        using var index = await ModpackManifestJson.ReadAsync(indexEntry, cancellationToken).ConfigureAwait(false);
        var packageName = ModpackManifestJson.GetString(index.RootElement, "name");
        var dependencies = ModpackManifestJson.GetRequiredObject(index.RootElement, "dependencies");
        var minecraftVersion = ModpackManifestJson.GetRequiredString(dependencies, "minecraft");
        var (loader, loaderVersion) = ParseLoader(dependencies);

        return new PreparedModpack
        {
            PackageKind = ModpackPackageKind.Modrinth,
            SourceArchivePath = sourceArchivePath,
            EmbeddedModrinthEntryName = embeddedEntryName,
            PackageName = string.IsNullOrWhiteSpace(packageName)
                ? Path.GetFileNameWithoutExtension(sourceArchivePath)
                : packageName,
            MinecraftVersion = minecraftVersion,
            Loader = loader,
            LoaderVersion = loaderVersion,
            HasOverrides = validateOverrides && ModpackOverrideExtractor.HasModrinthOverrides(archive),
            Files = ParseFiles(index.RootElement)
        };
    }

    private static IReadOnlyList<PreparedModpackDownload> ParseFiles(JsonElement root)
    {
        if (!root.TryGetProperty("files", out var files) || files.ValueKind is not JsonValueKind.Array)
            return [];

        var downloads = new List<PreparedModpackDownload>();
        foreach (var file in files.EnumerateArray())
        {
            if (ShouldSkipClientFile(file))
                continue;

            var relativePath = ModpackManifestJson.GetRequiredString(file, "path");
            var sourceUrl = ResolveDownloadUrl(file);
            var hashes = ModpackManifestJson.GetRequiredObject(file, "hashes");
            var sha1 = ModpackManifestJson.GetString(hashes, "sha1");
            var sha512 = ModpackManifestJson.GetString(hashes, "sha512");
            if (string.IsNullOrWhiteSpace(sha1) && string.IsNullOrWhiteSpace(sha512))
            {
                throw new ModpackImportException(
                    ModpackImportFailureReason.InvalidManifest,
                    $"Modrinth file is missing supported hashes: {relativePath}");
            }

            downloads.Add(new PreparedModpackDownload
            {
                FileName = Path.GetFileName(relativePath),
                RelativePath = relativePath,
                SourceUrl = sourceUrl,
                Sha1 = string.IsNullOrWhiteSpace(sha1) ? null : sha1,
                Sha512 = string.IsNullOrWhiteSpace(sha512) ? null : sha512
            });
        }

        return downloads;
    }

    private static (LoaderKind Loader, string? LoaderVersion) ParseLoader(JsonElement dependencies)
    {
        var loaderEntries = new List<(LoaderKind Loader, string? LoaderVersion)>();
        if (ModpackManifestJson.TryGetString(dependencies, "fabric-loader", out var fabricVersion))
            loaderEntries.Add((LoaderKind.Fabric, fabricVersion));
        if (ModpackManifestJson.TryGetString(dependencies, "forge", out var forgeVersion))
            loaderEntries.Add((LoaderKind.Forge, forgeVersion));
        if (ModpackManifestJson.TryGetString(dependencies, "neoforge", out var neoForgeVersion))
            loaderEntries.Add((LoaderKind.NeoForge, neoForgeVersion));
        if (ModpackManifestJson.TryGetString(dependencies, "quilt-loader", out var quiltVersion))
            loaderEntries.Add((LoaderKind.Quilt, quiltVersion));

        return loaderEntries.Count switch
        {
            0 => (LoaderKind.Vanilla, null),
            1 => loaderEntries[0],
            _ => throw new ModpackImportException(
                ModpackImportFailureReason.InvalidManifest,
                "Modrinth modpack declares multiple loaders.")
        };
    }

    private static bool ShouldSkipClientFile(JsonElement file)
    {
        return file.TryGetProperty("env", out var env)
            && env.ValueKind is JsonValueKind.Object
            && env.TryGetProperty("client", out var clientProperty)
            && clientProperty.ValueKind is JsonValueKind.String
            && string.Equals(clientProperty.GetString(), "unsupported", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveDownloadUrl(JsonElement file)
    {
        if (!file.TryGetProperty("downloads", out var downloads) || downloads.ValueKind is not JsonValueKind.Array)
        {
            throw new ModpackImportException(
                ModpackImportFailureReason.InvalidManifest,
                "Modrinth file entry is missing downloads.");
        }

        foreach (var download in downloads.EnumerateArray())
        {
            var url = download.ValueKind is JsonValueKind.String ? download.GetString() : null;
            if (!string.IsNullOrWhiteSpace(url) && ModpackArchiveUtility.IsSupportedHttpUrl(url))
                return url;
        }

        throw new ModpackImportException(
            ModpackImportFailureReason.InvalidManifest,
            "Modrinth file entry does not contain a supported download URL.");
    }
}
