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

namespace Launcher.Domain.Models;

public static class LauncherBackgroundEffects
{
    public const string None = "None";
    public const string Acrylic = "Acrylic";
    public const string Image = "Image";

    public static string Normalize(string? value)
    {
        if (string.Equals(value, None, StringComparison.OrdinalIgnoreCase))
            return None;
        if (string.Equals(value, Image, StringComparison.OrdinalIgnoreCase))
            return Image;
        return Acrylic;
    }

    public static bool IsAcrylic(string? value) => string.Equals(
        Normalize(value),
        Acrylic,
        StringComparison.Ordinal);

    public static bool IsImage(string? value) => string.Equals(
        Normalize(value),
        Image,
        StringComparison.Ordinal);
}
