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

using Launcher.App.Resources;
using Launcher.Application.Accounts;

namespace Launcher.App.ViewModels.Account;

internal static class AccountErrorCodeMessageFormatter
{
    public static string Format(Exception exception)
    {
        var errorCode = exception switch
        {
            MicrosoftAccountNameChangeException { ErrorCode: { Length: > 0 } code } => code,
            MicrosoftAccountSkinUpdateException { ErrorCode: { Length: > 0 } code } => code,
            MicrosoftAccountProfileRefreshException { ErrorCode: { Length: > 0 } code } => code,
            _ => null
        };

        return string.IsNullOrWhiteSpace(errorCode)
            ? string.Empty
            : string.Format(Strings.Status_ErrorCodeFormat, errorCode);
    }
}
