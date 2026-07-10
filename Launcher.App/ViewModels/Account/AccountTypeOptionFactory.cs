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

using Launcher.App.Models;
using Launcher.App.Resources;

namespace Launcher.App.ViewModels.Account;

internal static class AccountTypeOptionFactory
{
    public static IEnumerable<AccountTypeOption> Create()
    {
        return
        [
            new AccountTypeOption
            {
                Kind = AccountTypeKinds.Offline,
                Title = Strings.Account_TypeOfflineTitle,
                Description = Strings.Account_TypeOfflineDescription,
                Icon = "\uE77B",
                IconKey = "account_page/account_page_add_account_dialog_offline_user"
            },
            new AccountTypeOption
            {
                Kind = AccountTypeKinds.Microsoft,
                Title = Strings.Account_TypeMicrosoftTitle,
                Description = Strings.Account_TypeMicrosoftDescription,
                Icon = "\uE72E",
                IconKey = "account_page/account_page_add_account_dialog_online_user"
            }
        ];
    }
}

