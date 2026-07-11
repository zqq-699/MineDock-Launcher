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
    private readonly IThirdPartyLaunchSessionService thirdPartyLaunchSessionService;

    public LaunchAccountSessionService(
        MicrosoftAuthProvider authProvider,
        IOfflineAccountUuidService offlineUuidService,
        IThirdPartyLaunchSessionService thirdPartyLaunchSessionService)
    {
        this.authProvider = authProvider;
        this.offlineUuidService = offlineUuidService;
        this.thirdPartyLaunchSessionService = thirdPartyLaunchSessionService;
    }

    public async Task<LaunchAccountSession> CreateSessionAsync(
        LauncherAccount account,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(account.DisplayName))
            throw new LaunchAccountSessionException("Launch account username is missing.");

        return account.Kind switch
        {
            Launcher.Domain.Models.LauncherAccountKind.Offline => CreateOfflineSession(account),
            Launcher.Domain.Models.LauncherAccountKind.Microsoft => await CreateMicrosoftSessionAsync(account, cancellationToken),
            Launcher.Domain.Models.LauncherAccountKind.ThirdParty => await CreateThirdPartySessionAsync(account, cancellationToken),
            _ => throw new LaunchAccountSessionException("Launch account type is unsupported.")
        };
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
            IsOffline: true,
            Kind: Launcher.Domain.Models.LauncherAccountKind.Offline);
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
                IsOffline: false,
                Kind: Launcher.Domain.Models.LauncherAccountKind.Microsoft);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (MicrosoftAccountAuthenticationException ex)
        {
            throw new LaunchAccountSessionException(
                ex.Reason,
                "Microsoft account session is unavailable.",
                ex);
        }
        catch (Exception ex)
        {
            throw new LaunchAccountSessionException("Microsoft account token is unavailable.", ex);
        }
    }

    private async Task<LaunchAccountSession> CreateThirdPartySessionAsync(
        LauncherAccount account,
        CancellationToken cancellationToken)
    {
        var session = await thirdPartyLaunchSessionService.CreateAsync(account, cancellationToken).ConfigureAwait(false);
        return new LaunchAccountSession(
            session.Username,
            session.AccessToken,
            session.Uuid,
            IsOffline: false,
            Kind: Launcher.Domain.Models.LauncherAccountKind.ThirdParty,
            ThirdParty: new ThirdPartyLaunchContext(
                session.AuthenticationServerUrl,
                session.PrefetchedMetadata));
    }

    private static string ToSessionUuid(string uuid)
    {
        return uuid.Replace("-", string.Empty, StringComparison.Ordinal);
    }
}
