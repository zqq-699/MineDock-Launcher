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

using Launcher.Application.Accounts;
using Launcher.Domain.Models;

namespace Launcher.Tests.Fakes;

internal sealed class FakeOfflineAccountUuidService : IOfflineAccountUuidService
{
    public string CreateUuid(
        string username,
        OfflineUuidGenerationMode mode,
        string? existingUuid = null)
    {
        return (mode == OfflineUuidGenerationMode.Random || mode == OfflineUuidGenerationMode.Manual)
            && !string.IsNullOrWhiteSpace(existingUuid)
            ? existingUuid
            : $"{mode}-{username}";
    }

    public bool TryNormalizeUuid(string input, out string normalizedUuid)
    {
        if (Guid.TryParse(input, out var parsed))
        {
            normalizedUuid = parsed.ToString();
            return true;
        }

        var compact = input.Replace("-", string.Empty, StringComparison.Ordinal);
        if (Guid.TryParseExact(compact, "N", out parsed))
        {
            normalizedUuid = parsed.ToString();
            return true;
        }

        normalizedUuid = string.Empty;
        return false;
    }
}

