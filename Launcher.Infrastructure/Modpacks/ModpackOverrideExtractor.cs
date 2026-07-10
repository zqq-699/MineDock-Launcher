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
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Modpacks;

internal static class ModpackOverrideExtractor
{
    public static bool HasModrinthOverrides(ZipArchive archive)
    {
        return ValidateOverrides(archive, includeClientOverrides: true);
    }

    public static bool HasCurseForgeOverrides(ZipArchive archive)
    {
        return ValidateOverrides(archive, includeClientOverrides: false);
    }

    public static async Task CopyOverridesAsync(
        PreparedModpack preparedModpack,
        string instanceDirectory,
        CancellationToken cancellationToken)
    {
        if (preparedModpack.PackageKind is ModpackPackageKind.CurseForge)
        {
            await using var stream = File.OpenRead(preparedModpack.SourceArchivePath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            await ExtractOverridesAsync(archive, instanceDirectory, includeClientOverrides: false, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (!string.IsNullOrWhiteSpace(preparedModpack.EmbeddedModrinthEntryName))
        {
            await using var outerStream = File.OpenRead(preparedModpack.SourceArchivePath);
            using var outerArchive = new ZipArchive(outerStream, ZipArchiveMode.Read, leaveOpen: false);
            var mrpackEntry = outerArchive.Entries.FirstOrDefault(entry =>
                string.Equals(entry.FullName, preparedModpack.EmbeddedModrinthEntryName, StringComparison.Ordinal));
            if (mrpackEntry is null)
            {
                throw new ModpackImportException(
                    ModpackImportFailureReason.InvalidManifest,
                    "Embedded Modrinth archive was not found.");
            }

            await using var innerStream = await ModpackArchiveUtility.CopyZipEntryToMemoryAsync(
                mrpackEntry,
                ModpackArchiveUtility.MaxEmbeddedModpackBytes,
                cancellationToken).ConfigureAwait(false);
            using var innerArchive = new ZipArchive(innerStream, ZipArchiveMode.Read, leaveOpen: false);
            await ExtractOverridesAsync(innerArchive, instanceDirectory, includeClientOverrides: true, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await using var modrinthStream = File.OpenRead(preparedModpack.SourceArchivePath);
        using var modrinthArchive = new ZipArchive(modrinthStream, ZipArchiveMode.Read, leaveOpen: false);
        await ExtractOverridesAsync(modrinthArchive, instanceDirectory, includeClientOverrides: true, cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool ValidateOverrides(ZipArchive archive, bool includeClientOverrides)
    {
        var foundAny = false;
        var extractionBudget = new ZipExtractionBudget(ModpackArchiveUtility.MaxOverrideTotalBytes);
        foreach (var entry in archive.Entries.Where(entry => !string.IsNullOrWhiteSpace(entry.Name)))
        {
            var relativePath = ResolveOverridePath(entry, includeClientOverrides);
            if (string.IsNullOrWhiteSpace(relativePath))
                continue;

            if (entry.Length > ModpackArchiveUtility.MaxOverrideEntryBytes)
            {
                throw new ModpackImportException(
                    ModpackImportFailureReason.InvalidManifest,
                    $"Archive entry is too large: {entry.FullName}");
            }

            extractionBudget.Reserve(entry.Length);
            _ = ModpackArchiveUtility.GetValidatedTargetPath(Path.GetTempPath(), relativePath);
            foundAny = true;
        }

        return foundAny;
    }

    private static async Task ExtractOverridesAsync(
        ZipArchive archive,
        string instanceDirectory,
        bool includeClientOverrides,
        CancellationToken cancellationToken)
    {
        var extractionBudget = new ZipExtractionBudget(ModpackArchiveUtility.MaxOverrideTotalBytes);
        foreach (var entry in archive.Entries.Where(entry => !string.IsNullOrWhiteSpace(entry.Name)))
        {
            var relativePath = ResolveOverridePath(entry, includeClientOverrides);
            if (string.IsNullOrWhiteSpace(relativePath))
                continue;

            await ModpackArchiveUtility.ExtractZipEntryAsync(
                entry,
                instanceDirectory,
                relativePath,
                extractionBudget,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static string ResolveOverridePath(ZipArchiveEntry entry, bool includeClientOverrides)
    {
        var normalizedPath = ModpackArchiveUtility.NormalizeArchivePath(entry.FullName);
        var relativePath = ModpackArchiveUtility.RemovePrefix(normalizedPath, "overrides");
        if (!string.IsNullOrWhiteSpace(relativePath) || !includeClientOverrides)
            return relativePath;

        return ModpackArchiveUtility.RemovePrefix(normalizedPath, "client-overrides");
    }
}
