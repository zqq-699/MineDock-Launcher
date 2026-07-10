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

namespace Launcher.Infrastructure.Accounts;

internal sealed class MinecraftSkinService
{
    private readonly MicrosoftAuthProvider authProvider;
    private readonly MinecraftProfileClient profileClient;
    private readonly MicrosoftAccountFactory accountFactory;
    private readonly AccountSkinCacheService skinCacheService;

    public MinecraftSkinService(
        MicrosoftAuthProvider authProvider,
        MinecraftProfileClient profileClient,
        MicrosoftAccountFactory accountFactory,
        AccountSkinCacheService skinCacheService)
    {
        this.authProvider = authProvider;
        this.profileClient = profileClient;
        this.accountFactory = accountFactory;
        this.skinCacheService = skinCacheService;
    }

    public async Task<LauncherAccount> UploadSkinAsync(
        LauncherAccount account,
        string skinFilePath,
        MinecraftSkinModel skinModel,
        CancellationToken cancellationToken)
    {
        var accessToken = await authProvider.GetAccessTokenAsync(account, cancellationToken);
        await profileClient.UploadSkinAsync(accessToken, skinFilePath, skinModel, cancellationToken);

        var profile = await profileClient.GetProfileAsync(accessToken, cancellationToken);
        var uuid = MinecraftAccountHelpers.NormalizeUuid(profile.Id);
        var skinSource = await skinCacheService.StoreUploadedSkinAsync(
            uuid,
            skinFilePath,
            skinModel,
            cancellationToken);
        var updatedAccount = await accountFactory.CreateAccountFromProfileAsync(
            profile,
            forceRefreshAvatar: true,
            cancellationToken,
            account.SkinLibrary);

        return new LauncherAccount
        {
            Id = updatedAccount.Id,
            DisplayName = updatedAccount.DisplayName,
            Uuid = updatedAccount.Uuid,
            OfflineUuidGenerationMode = updatedAccount.OfflineUuidGenerationMode,
            AvatarSource = updatedAccount.AvatarSource,
            SkinSource = skinSource ?? updatedAccount.SkinSource,
            SkinModel = skinModel,
            IsOffline = updatedAccount.IsOffline,
            HasFreshProfile = updatedAccount.HasFreshProfile,
            CachedCapeOptions = updatedAccount.CachedCapeOptions
        };
    }
}
