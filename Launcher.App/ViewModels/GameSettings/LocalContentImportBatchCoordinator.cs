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

namespace Launcher.App.ViewModels.GameSettings;

internal sealed record LocalContentImportBatchResult<TResult>(
    int SuccessCount,
    string? FailedPath,
    TResult? Failure)
    where TResult : class;

internal static class LocalContentImportBatchCoordinator
{
    public static async Task<LocalContentImportBatchResult<TResult>> ExecuteAsync<TResult>(
        IEnumerable<string> paths,
        Func<string, Task<TResult>> importAsync,
        Func<TResult, bool> isSuccess)
        where TResult : class
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(importAsync);
        ArgumentNullException.ThrowIfNull(isSuccess);

        var successCount = 0;
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var result = await importAsync(path);
            if (isSuccess(result))
            {
                successCount++;
                continue;
            }

            return new LocalContentImportBatchResult<TResult>(successCount, path, result);
        }

        return new LocalContentImportBatchResult<TResult>(successCount, null, null);
    }
}
