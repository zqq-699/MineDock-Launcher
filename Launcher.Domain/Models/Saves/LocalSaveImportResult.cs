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

public enum LocalSaveImportFailureReason
{
    None = 0,
    FileNotFound,
    InvalidMinecraftSaveArchive,
    UnsupportedArchive,
    UnexpectedError
}

public sealed class LocalSaveImportResult
{
    private LocalSaveImportResult(
        bool isSuccess,
        LocalSaveImportFailureReason failureReason,
        LocalSave? importedSave)
    {
        IsSuccess = isSuccess;
        FailureReason = failureReason;
        ImportedSave = importedSave;
    }

    public bool IsSuccess { get; }

    public LocalSaveImportFailureReason FailureReason { get; }

    public LocalSave? ImportedSave { get; }

    public static LocalSaveImportResult Success(LocalSave save)
    {
        ArgumentNullException.ThrowIfNull(save);
        return new LocalSaveImportResult(true, LocalSaveImportFailureReason.None, save);
    }

    public static LocalSaveImportResult Failure(LocalSaveImportFailureReason failureReason)
    {
        return new LocalSaveImportResult(false, failureReason, null);
    }
}
