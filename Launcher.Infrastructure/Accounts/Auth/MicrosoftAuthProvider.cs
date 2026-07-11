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

using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.Auth.Microsoft.Sessions;
using Launcher.Infrastructure.Accounts.Credentials;
using Launcher.Application.Accounts;
using Launcher.Infrastructure;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using XboxAuthNet.Game.Accounts;
using XboxAuthNet.Game.SessionStorages;
using XboxAuthNet.OAuth;

namespace Launcher.Infrastructure.Accounts;

internal sealed class MicrosoftAuthProvider
{
    private readonly DpapiMicrosoftJsonStorage credentialStorage;
    private JELoginHandler loginHandler;

    public MicrosoftAuthProvider(LauncherPathProvider pathProvider)
    {
        credentialStorage = new DpapiMicrosoftJsonStorage(pathProvider);
        loginHandler = CreatePersistentLoginHandler();
    }

    public IEnumerable<JEGameAccount> GetSavedAccounts()
    {
        return loginHandler.AccountManager.GetAccounts().OfType<JEGameAccount>();
    }

    public async Task<MicrosoftLoginResult> LoginInteractivelyAsync(CancellationToken cancellationToken)
    {
        var (account, session) = await AuthenticateInMemoryAsync(cancellationToken);
        CommitSession(account.SessionStorage);
        var refreshedAccount = JEGameAccount.FromSessionStorage(account.SessionStorage);
        var profile = refreshedAccount.Profile;
        var accessToken = refreshedAccount.Token?.AccessToken;
        return new MicrosoftLoginResult(profile, session.Username, session.UUID, accessToken);
    }

    public async Task<MicrosoftLoginResult> ReauthenticateInteractivelyAsync(
        LauncherAccount existingAccount,
        CancellationToken cancellationToken)
    {
        var (account, session) = await AuthenticateInMemoryAsync(cancellationToken);
        var refreshedAccount = JEGameAccount.FromSessionStorage(account.SessionStorage);
        var refreshedUuid = MinecraftAccountHelpers.NormalizeUuid(
            refreshedAccount.Profile?.UUID ?? session.UUID);
        if (string.IsNullOrWhiteSpace(existingAccount.Uuid)
            || !string.Equals(refreshedUuid, existingAccount.Uuid, StringComparison.OrdinalIgnoreCase))
        {
            throw new MicrosoftAccountAuthenticationException(
                LaunchAccountSessionFailureReason.ReauthenticationRequired,
                "The signed-in Microsoft account does not match the selected launcher account.");
        }

        CommitSession(account.SessionStorage);
        return new MicrosoftLoginResult(
            refreshedAccount.Profile,
            session.Username,
            session.UUID,
            refreshedAccount.Token?.AccessToken);
    }

    public async Task<bool> DeleteAccountAsync(LauncherAccount account, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(account.Uuid))
            return false;

        foreach (var savedAccount in GetSavedAccounts())
        {
            var savedUuid = MinecraftAccountHelpers.NormalizeUuid(savedAccount.Profile?.UUID);
            if (!string.Equals(savedUuid, account.Uuid, StringComparison.OrdinalIgnoreCase))
                continue;

            await loginHandler.Signout(savedAccount, cancellationToken);
            loginHandler.AccountManager.SaveAccounts();
            return true;
        }

        return false;
    }

    public async Task<string> GetAccessTokenAsync(LauncherAccount account, CancellationToken cancellationToken)
    {
        if (!account.IsMicrosoft || string.IsNullOrWhiteSpace(account.Uuid))
            throw new InvalidOperationException("\u53ea\u6709\u6b63\u7248\u8d26\u6237\u652f\u6301\u6b64\u64cd\u4f5c");

        JEGameAccount? savedAccount;
        try
        {
            savedAccount = FindSavedAccount(account);
        }
        catch (MicrosoftCredentialStorageException exception)
        {
            throw new MicrosoftAccountAuthenticationException(
                LaunchAccountSessionFailureReason.CredentialStorageFailed,
                "Microsoft account credentials could not be read.",
                exception);
        }
        if (savedAccount is null)
        {
            throw new MicrosoftAccountAuthenticationException(
                LaunchAccountSessionFailureReason.ReauthenticationRequired,
                "Microsoft account credentials are missing.");
        }

        try
        {
            await loginHandler.AuthenticateSilently(savedAccount, cancellationToken);
            loginHandler.AccountManager.SaveAccounts();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (MicrosoftCredentialStorageException exception)
        {
            throw new MicrosoftAccountAuthenticationException(
                LaunchAccountSessionFailureReason.CredentialStorageFailed,
                "Microsoft account credentials could not be saved.",
                exception);
        }
        catch (MicrosoftOAuthException exception) when (RequiresInteractiveLogin(exception))
        {
            throw new MicrosoftAccountAuthenticationException(
                LaunchAccountSessionFailureReason.ReauthenticationRequired,
                "Microsoft account credentials have expired.",
                exception);
        }
        catch (MicrosoftOAuthException exception)
        {
            var reason = exception.StatusCode >= 500
                ? LaunchAccountSessionFailureReason.AuthenticationServerUnavailable
                : LaunchAccountSessionFailureReason.InvalidAuthenticationResponse;
            throw new MicrosoftAccountAuthenticationException(
                reason,
                "Microsoft authentication failed.",
                exception);
        }
        catch (JEAuthException exception) when (exception.StatusCode is 401 or 403)
        {
            throw new MicrosoftAccountAuthenticationException(
                LaunchAccountSessionFailureReason.ReauthenticationRequired,
                "Microsoft account credentials were rejected.",
                exception);
        }
        catch (JEAuthException exception)
        {
            var reason = exception.StatusCode >= 500
                ? LaunchAccountSessionFailureReason.AuthenticationServerUnavailable
                : LaunchAccountSessionFailureReason.InvalidAuthenticationResponse;
            throw new MicrosoftAccountAuthenticationException(
                reason,
                "Minecraft authentication failed.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new MicrosoftAccountAuthenticationException(
                LaunchAccountSessionFailureReason.AuthenticationServerUnavailable,
                "Microsoft authentication services are unavailable.",
                exception);
        }
        catch (JsonException exception)
        {
            throw new MicrosoftAccountAuthenticationException(
                LaunchAccountSessionFailureReason.InvalidAuthenticationResponse,
                "Microsoft authentication returned an invalid response.",
                exception);
        }

        var refreshedAccount = JEGameAccount.FromSessionStorage(savedAccount.SessionStorage);
        var accessToken = refreshedAccount.Token?.AccessToken ?? savedAccount.Token?.AccessToken;
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new MicrosoftAccountAuthenticationException(
                LaunchAccountSessionFailureReason.ReauthenticationRequired,
                "Microsoft account access token is missing.");

        return accessToken;
    }

    public void UpdateSavedProfile(LauncherAccount account, string displayName, string uuid)
    {
        if (string.IsNullOrWhiteSpace(uuid) || string.IsNullOrWhiteSpace(displayName))
            return;

        var savedAccount = FindSavedAccount(account);
        if (savedAccount?.Profile is null)
            return;

        savedAccount.Profile.Username = displayName;
        savedAccount.Profile.UUID = uuid;
        loginHandler.AccountManager.SaveAccounts();
    }

    private JEGameAccount? FindSavedAccount(LauncherAccount account)
    {
        var targetUuid = MinecraftAccountHelpers.NormalizeUuid(account.Uuid);
        return GetSavedAccounts()
            .FirstOrDefault(savedAccount =>
            {
                var savedUuid = MinecraftAccountHelpers.NormalizeUuid(savedAccount.Profile?.UUID);
                if (string.IsNullOrWhiteSpace(savedUuid))
                {
                    savedUuid = MinecraftAccountHelpers.NormalizeUuid(
                        JEGameAccount.FromSessionStorage(savedAccount.SessionStorage).Profile?.UUID);
                }

                return string.Equals(savedUuid, targetUuid, StringComparison.OrdinalIgnoreCase);
            });
    }

    private JELoginHandler CreatePersistentLoginHandler()
    {
        var accountManager = new JsonXboxGameAccountManager(
            credentialStorage,
            JEGameAccount.FromSessionStorage,
            JsonXboxGameAccountManager.DefaultSerializerOption);
        return new JELoginHandlerBuilder()
            .WithAccountManager(accountManager)
            .Build();
    }

    private static async Task<(JEGameAccount Account, CmlLib.Core.Auth.MSession Session)> AuthenticateInMemoryAsync(
        CancellationToken cancellationToken)
    {
        var accountManager = new InMemoryXboxGameAccountManager(JEGameAccount.FromSessionStorage);
        var handler = new JELoginHandlerBuilder()
            .WithAccountManager(accountManager)
            .Build();
        var account = (JEGameAccount)accountManager.NewAccount();
        var session = await handler.AuthenticateInteractively(account, cancellationToken);
        return (account, session);
    }

    private void CommitSession(ISessionStorage source)
    {
        try
        {
            var sessionStorage = JsonSessionStorage.CreateEmpty(
                JsonXboxGameAccountManager.DefaultSerializerOption);
            foreach (var key in source.Keys.ToArray())
            {
                var value = source.Get<object>(key);
                sessionStorage.Set(key, value);
                sessionStorage.SetKeyMode(key, source.GetKeyMode(key));
            }

            var account = JEGameAccount.FromSessionStorage(sessionStorage);
            if (string.IsNullOrWhiteSpace(account.Identifier))
                throw new InvalidDataException("Microsoft account identifier is missing.");

            var root = credentialStorage.ReadAsJsonNode() as JsonObject ?? new JsonObject();
            root[account.Identifier] = sessionStorage.ToJsonObjectForStoring();
            credentialStorage.Write(root, JsonXboxGameAccountManager.DefaultSerializerOption);
            loginHandler = CreatePersistentLoginHandler();
        }
        catch (MicrosoftCredentialStorageException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new MicrosoftCredentialStorageException(
                "Microsoft account credentials could not be saved.",
                exception);
        }
    }

    private static bool RequiresInteractiveLogin(MicrosoftOAuthException exception)
    {
        return exception.StatusCode is 0 or (int)HttpStatusCode.BadRequest or (int)HttpStatusCode.Unauthorized
            || string.Equals(exception.Error, "invalid_grant", StringComparison.OrdinalIgnoreCase)
            || string.Equals(exception.Error, "interaction_required", StringComparison.OrdinalIgnoreCase)
            || string.Equals(exception.Error, "login_required", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("Interactive Microsoft authentication is required", StringComparison.OrdinalIgnoreCase);
    }
}
