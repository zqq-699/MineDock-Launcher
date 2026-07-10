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

public enum LocalResourcePackImportFailureReason
{
    None = 0,
    FileNotFound,
    UnsupportedArchive,
    UnexpectedError
}

public sealed class LocalResourcePackImportResult
{
    private LocalResourcePackImportResult(
        bool isSuccess,
        LocalResourcePackImportFailureReason failureReason,
        LocalResourcePack? importedResourcePack)
    {
        IsSuccess = isSuccess;
        FailureReason = failureReason;
        ImportedResourcePack = importedResourcePack;
    }

    public bool IsSuccess { get; }

    public LocalResourcePackImportFailureReason FailureReason { get; }

    public LocalResourcePack? ImportedResourcePack { get; }

    public static LocalResourcePackImportResult Success(LocalResourcePack resourcePack)
    {
        ArgumentNullException.ThrowIfNull(resourcePack);
        return new LocalResourcePackImportResult(true, LocalResourcePackImportFailureReason.None, resourcePack);
    }

    public static LocalResourcePackImportResult Failure(LocalResourcePackImportFailureReason failureReason)
    {
        return new LocalResourcePackImportResult(false, failureReason, null);
    }
}
