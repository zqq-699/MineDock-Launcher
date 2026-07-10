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

namespace Launcher.Infrastructure.Accounts;

internal sealed class LaunchAccountSessionService : ILaunchAccountSessionService
{
    private readonly MicrosoftAuthProvider authProvider;
    private readonly IOfflineAccountUuidService offlineUuidService;

    public LaunchAccountSessionService(
        MicrosoftAuthProvider authProvider,
        IOfflineAccountUuidService offlineUuidService)
    {
        this.authProvider = authProvider;
        this.offlineUuidService = offlineUuidService;
    }

    public async Task<LaunchAccountSession> CreateSessionAsync(
        LauncherAccount account,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(account.DisplayName))
            throw new LaunchAccountSessionException("Launch account username is missing.");

        return account.IsOffline
            ? CreateOfflineSession(account)
            : await CreateMicrosoftSessionAsync(account, cancellationToken);
    }

    private LaunchAccountSession CreateOfflineSession(LauncherAccount account)
    {
        var uuid = string.IsNullOrWhiteSpace(account.Uuid)
            ? offlineUuidService.CreateUuid(
                account.DisplayName,
                account.OfflineUuidGenerationMode)
            : account.Uuid;

        if (!offlineUuidService.TryNormalizeUuid(uuid, out var normalizedUuid))
            throw new LaunchAccountSessionException("Offline account UUID is invalid.");

        var compactUuid = ToSessionUuid(normalizedUuid);
        return new LaunchAccountSession(
            account.DisplayName,
            compactUuid,
            compactUuid,
            IsOffline: true);
    }

    private async Task<LaunchAccountSession> CreateMicrosoftSessionAsync(
        LauncherAccount account,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(account.Uuid)
            || !offlineUuidService.TryNormalizeUuid(account.Uuid, out var normalizedUuid))
        {
            throw new LaunchAccountSessionException("Microsoft account UUID is missing.");
        }

        try
        {
            var accessToken = await authProvider.GetAccessTokenAsync(account, cancellationToken);
            return new LaunchAccountSession(
                account.DisplayName,
                accessToken,
                ToSessionUuid(normalizedUuid),
                IsOffline: false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new LaunchAccountSessionException("Microsoft account token is unavailable.", ex);
        }
    }

    private static string ToSessionUuid(string uuid)
    {
        return uuid.Replace("-", string.Empty, StringComparison.Ordinal);
    }
}
