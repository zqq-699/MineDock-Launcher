/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Application.Accounts;

public interface IThirdPartyAccountService
{
    Task<LauncherAccount> LoginWithUsernameAsync(
        string authenticationServer,
        string username,
        string password,
        CancellationToken cancellationToken = default);

    Task<ThirdPartyEmailLoginSession> BeginEmailLoginAsync(
        string authenticationServer,
        string email,
        string password,
        CancellationToken cancellationToken = default);

    Task<LauncherAccount> ImportEmailProfileAsync(
        string attemptId,
        string profileUuid,
        string password,
        CancellationToken cancellationToken = default);

    Task CancelEmailLoginAsync(string attemptId, CancellationToken cancellationToken = default);

    Task<LauncherAccount> RefreshAccountProfileAsync(
        LauncherAccount account,
        CancellationToken cancellationToken = default);

    Task<LauncherAccount> ReauthenticateAsync(
        LauncherAccount account,
        string password,
        CancellationToken cancellationToken = default);

    Task DeleteCredentialsAsync(string accountId, CancellationToken cancellationToken = default);
}
