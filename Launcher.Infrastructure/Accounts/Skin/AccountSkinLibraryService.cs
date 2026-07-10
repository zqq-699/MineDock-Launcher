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

using System.Net.Http;
using Launcher.Application.Accounts;
using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Accounts;

public sealed class AccountSkinLibraryService : IAccountSkinLibraryService
{
    private static readonly HttpClient HttpClient = new();
    private readonly AccountSkinCacheService skinCacheService;

    public AccountSkinLibraryService()
    {
        skinCacheService = new AccountSkinCacheService(HttpClient, new LauncherPathProvider());
    }

    public IReadOnlyList<LauncherSkinRecord> GetAvailableSkins(LauncherAccount account)
    {
        return skinCacheService.GetAvailableSkins(account);
    }

    public Task<LauncherSkinRecord> ImportSkinAsync(
        LauncherAccount account,
        string skinFilePath,
        MinecraftSkinModel skinModel,
        CancellationToken cancellationToken = default)
    {
        return skinCacheService.ImportSkinAsync(account, skinFilePath, skinModel, cancellationToken);
    }

    public Task DeleteSkinAsync(
        LauncherAccount account,
        LauncherSkinRecord skin,
        CancellationToken cancellationToken = default)
    {
        return skinCacheService.DeleteSkinAsync(account, skin, cancellationToken);
    }
}
