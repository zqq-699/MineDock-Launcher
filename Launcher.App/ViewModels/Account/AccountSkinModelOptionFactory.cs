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
using Launcher.App.Models;
using Launcher.App.Resources;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Account;

internal static class AccountSkinModelOptionFactory
{
    public static IEnumerable<AccountSkinModelOption> Create()
    {
        return
        [
            new AccountSkinModelOption
            {
                Model = MinecraftSkinModel.Classic,
                Title = Strings.Account_SkinModelClassicTitle,
                Description = Strings.Account_SkinModelClassicDescription
            },
            new AccountSkinModelOption
            {
                Model = MinecraftSkinModel.Slim,
                Title = Strings.Account_SkinModelSlimTitle,
                Description = Strings.Account_SkinModelSlimDescription
            }
        ];
    }
}

