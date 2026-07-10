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

public enum LocalShaderPackImportFailureReason
{
    None = 0,
    FileNotFound,
    UnsupportedArchive,
    UnexpectedError
}

public sealed class LocalShaderPackImportResult
{
    private LocalShaderPackImportResult(
        bool isSuccess,
        LocalShaderPackImportFailureReason failureReason,
        LocalShaderPack? importedShaderPack)
    {
        IsSuccess = isSuccess;
        FailureReason = failureReason;
        ImportedShaderPack = importedShaderPack;
    }

    public bool IsSuccess { get; }

    public LocalShaderPackImportFailureReason FailureReason { get; }

    public LocalShaderPack? ImportedShaderPack { get; }

    public static LocalShaderPackImportResult Success(LocalShaderPack shaderPack)
    {
        ArgumentNullException.ThrowIfNull(shaderPack);
        return new LocalShaderPackImportResult(true, LocalShaderPackImportFailureReason.None, shaderPack);
    }

    public static LocalShaderPackImportResult Failure(LocalShaderPackImportFailureReason failureReason)
    {
        return new LocalShaderPackImportResult(false, failureReason, null);
    }
}
