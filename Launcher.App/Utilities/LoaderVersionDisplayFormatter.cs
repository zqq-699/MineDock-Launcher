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

using Launcher.Domain.Models;

namespace Launcher.App.Utilities;

internal static class LoaderVersionDisplayFormatter
{
    public static string Format(LoaderKind loader, string? loaderVersion)
    {
        if (string.IsNullOrWhiteSpace(loaderVersion))
            return string.Empty;

        var normalized = loaderVersion.Trim();
        if (loader is not LoaderKind.Fabric)
            return normalized;

        var mixinIndex = normalized.IndexOf("+mixin", StringComparison.OrdinalIgnoreCase);
        if (mixinIndex >= 0)
            normalized = normalized[..mixinIndex];

        var separatorIndex = normalized.IndexOfAny([' ', '/', ',', '(']);
        if (separatorIndex > 0 && normalized.Contains("mixin", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..separatorIndex];

        return normalized.Trim();
    }
}
