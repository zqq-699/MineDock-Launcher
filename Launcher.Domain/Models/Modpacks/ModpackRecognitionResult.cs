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

public enum ModpackRecognitionFailureReason
{
    None = 0,
    FileNotFound,
    UnsupportedArchive,
    InvalidManifest,
    UnsupportedLoader,
    UnexpectedError
}

public sealed class ModpackRecognitionResult
{
    private ModpackRecognitionResult(
        bool isSuccess,
        ModpackRecognitionFailureReason failureReason)
    {
        IsSuccess = isSuccess;
        FailureReason = failureReason;
    }

    public bool IsSuccess { get; }

    public ModpackRecognitionFailureReason FailureReason { get; }

    public static ModpackRecognitionResult Success()
    {
        return new ModpackRecognitionResult(true, ModpackRecognitionFailureReason.None);
    }

    public static ModpackRecognitionResult Failure(ModpackRecognitionFailureReason failureReason)
    {
        return new ModpackRecognitionResult(false, failureReason);
    }
}
