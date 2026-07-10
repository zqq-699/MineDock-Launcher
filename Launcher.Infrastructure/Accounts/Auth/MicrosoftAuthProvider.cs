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
using Launcher.Application.Accounts;
using Launcher.Infrastructure;
using System.IO;

namespace Launcher.Infrastructure.Accounts;

internal sealed class MicrosoftAuthProvider
{
    private readonly JELoginHandler loginHandler;

    public MicrosoftAuthProvider(LauncherPathProvider pathProvider)
    {
        var accountDirectory = Path.Combine(pathProvider.DefaultAccountDataDirectory, "microsoft");
        var accountFile = Path.Combine(accountDirectory, "accounts.json");
        Directory.CreateDirectory(accountDirectory);

        loginHandler = new JELoginHandlerBuilder()
            .WithAccountManager(accountFile)
            .Build();
    }

    public IEnumerable<JEGameAccount> GetSavedAccounts()
    {
        return loginHandler.AccountManager.GetAccounts().OfType<JEGameAccount>();
    }

    public async Task<MicrosoftLoginResult> LoginInteractivelyAsync(CancellationToken cancellationToken)
    {
        var account = loginHandler.AccountManager.NewAccount();
        var session = await loginHandler.AuthenticateInteractively(account, cancellationToken);
        loginHandler.AccountManager.SaveAccounts();

        var refreshedAccount = JEGameAccount.FromSessionStorage(account.SessionStorage);
        var profile = refreshedAccount.Profile;
        var accessToken = refreshedAccount.Token?.AccessToken;
        return new MicrosoftLoginResult(profile, session.Username, session.UUID, accessToken);
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
        if (account.IsOffline || string.IsNullOrWhiteSpace(account.Uuid))
            throw new InvalidOperationException("\u53ea\u6709\u6b63\u7248\u8d26\u6237\u652f\u6301\u6b64\u64cd\u4f5c");

        var savedAccount = FindSavedAccount(account)
            ?? throw new InvalidOperationException("\u672a\u627e\u5230\u8fd9\u4e2a\u6b63\u7248\u8d26\u6237\u7684\u767b\u5f55\u7f13\u5b58\uff0c\u8bf7\u91cd\u65b0\u767b\u5f55");

        await loginHandler.AuthenticateSilently(savedAccount, cancellationToken);
        loginHandler.AccountManager.SaveAccounts();

        var refreshedAccount = JEGameAccount.FromSessionStorage(savedAccount.SessionStorage);
        var accessToken = refreshedAccount.Token?.AccessToken ?? savedAccount.Token?.AccessToken;
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("\u672a\u80fd\u83b7\u53d6\u6b63\u7248\u8bbf\u95ee\u4ee4\u724c\uff0c\u8bf7\u91cd\u65b0\u767b\u5f55");

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
}
