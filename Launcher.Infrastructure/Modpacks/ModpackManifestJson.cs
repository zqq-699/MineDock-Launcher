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

using System.IO.Compression;
using System.Text.Json;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Modpacks;

internal static class ModpackManifestJson
{
    public static async Task<JsonDocument> ReadAsync(
        ZipArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        if (entry.Length > ModpackArchiveUtility.MaxManifestBytes)
        {
            throw new ModpackImportException(
                ModpackImportFailureReason.InvalidManifest,
                $"Manifest entry is too large: {entry.FullName}");
        }

        await using var stream = entry.Open();
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public static JsonElement GetRequiredObject(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var property)
            && property.ValueKind is JsonValueKind.Object)
        {
            return property;
        }

        throw new ModpackImportException(
            ModpackImportFailureReason.InvalidManifest,
            $"Required object property '{propertyName}' is missing.");
    }

    public static string GetRequiredString(JsonElement element, string propertyName)
    {
        if (TryGetString(element, propertyName, out var value))
            return value;

        throw new ModpackImportException(
            ModpackImportFailureReason.InvalidManifest,
            $"Required string property '{propertyName}' is missing.");
    }

    public static string GetString(JsonElement element, string propertyName)
    {
        return TryGetString(element, propertyName, out var value) ? value : string.Empty;
    }

    public static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind is not JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }
}
