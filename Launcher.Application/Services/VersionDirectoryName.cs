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

namespace Launcher.Application.Services;

public static class VersionDirectoryName
{
    public const string DefaultName = "Minecraft";

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON",
        "PRN",
        "AUX",
        "NUL",
        "COM1",
        "COM2",
        "COM3",
        "COM4",
        "COM5",
        "COM6",
        "COM7",
        "COM8",
        "COM9",
        "LPT1",
        "LPT2",
        "LPT3",
        "LPT4",
        "LPT5",
        "LPT6",
        "LPT7",
        "LPT8",
        "LPT9"
    };

    public static string Sanitize(string? value, string fallbackName = DefaultName)
    {
        var candidate = ExtractLeafSegment(value);
        candidate = ReplaceUnsafeCharacters(candidate).Trim().Trim('.');

        if (string.IsNullOrWhiteSpace(candidate)
            || candidate is "." or ".."
            || IsReservedDeviceName(candidate))
        {
            candidate = ReplaceUnsafeCharacters(fallbackName).Trim().Trim('.');
        }

        if (string.IsNullOrWhiteSpace(candidate)
            || candidate is "." or ".."
            || IsReservedDeviceName(candidate))
        {
            return DefaultName;
        }

        return candidate;
    }

    public static string NormalizeUserInput(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (!IsSafeDirectoryName(normalized))
            throw new ArgumentException($"Version name is not a safe directory name: {value}", nameof(value));

        return normalized;
    }

    public static bool IsSafeDirectoryName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
            return false;

        if (value is "." or "..")
            return false;

        if (value.StartsWith(".", StringComparison.Ordinal))
            return false;

        if (value.EndsWith(".", StringComparison.Ordinal))
            return false;

        if (Path.IsPathRooted(value))
            return false;

        if (value.IndexOfAny(new[] { '/', '\\', ':' }) >= 0)
            return false;

        if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return false;

        return !IsReservedDeviceName(value);
    }

    private static string ExtractLeafSegment(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        var segments = normalized
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(segment => segment is not "." and not "..")
            .ToArray();

        return segments.Length == 0 ? normalized : segments[^1];
    }

    private static string ReplaceUnsafeCharacters(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var builder = new char[value.Length];
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            builder[index] = character is '/' or '\\' or ':' || char.IsControl(character) || invalidCharacters.Contains(character)
                ? '-'
                : character;
        }

        return new string(builder);
    }

    private static bool IsReservedDeviceName(string value)
    {
        var stem = value.Split('.', 2)[0];
        return ReservedDeviceNames.Contains(stem);
    }
}
