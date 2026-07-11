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
using Launcher.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Application.Accounts;

public sealed class AccountStore : IAccountStore
{
    private readonly IAccountStateService accountStateService;
    private readonly IMicrosoftAccountService microsoftAccountService;
    private readonly IOfflineAccountUuidService offlineUuidService;
    private readonly ILogger<AccountStore> logger;

    public AccountStore(
        IAccountStateService accountStateService,
        IMicrosoftAccountService microsoftAccountService,
        IOfflineAccountUuidService offlineUuidService,
        ILogger<AccountStore>? logger = null)
    {
        this.accountStateService = accountStateService;
        this.microsoftAccountService = microsoftAccountService;
        this.offlineUuidService = offlineUuidService;
        this.logger = logger ?? NullLogger<AccountStore>.Instance;
    }

    public async Task<AccountStoreSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var state = await accountStateService.LoadAsync(cancellationToken);
        var accounts = new List<LauncherAccount>();
        var microsoftAccounts = new Dictionary<string, LauncherAccount>(StringComparer.OrdinalIgnoreCase);
        foreach (var account in await microsoftAccountService.GetSavedAccountsAsync(cancellationToken))
        {
            if (!microsoftAccounts.ContainsKey(account.Id))
                microsoftAccounts.Add(account.Id, account);
        }

        var shouldImportMicrosoftAccounts = !state.MicrosoftAccountsImported;
        var shouldPersistOrder = false;

        foreach (var account in state.Accounts)
        {
            var kind = account.Kind ?? (account.IsOffline
                ? LauncherAccountKind.Offline
                : LauncherAccountKind.Microsoft);
            if (kind == LauncherAccountKind.Offline)
            {
                shouldPersistOrder |= EnsureOfflineUuid(account);
                accounts.Add(AccountMapper.FromOfflineRecord(account));
                continue;
            }

            if (kind == LauncherAccountKind.ThirdParty)
            {
                accounts.Add(AccountMapper.FromRecord(account));
                continue;
            }

            if (microsoftAccounts.Remove(account.Id, out var microsoftAccount))
            {
                var mergedAccount = AccountMapper.MergeStoredRecord(microsoftAccount, account);
                accounts.Add(mergedAccount);
                shouldPersistOrder |= ShouldPersistMergedMicrosoftAccount(account, mergedAccount);
            }
            else
            {
                shouldPersistOrder = true;
            }
        }

        foreach (var account in microsoftAccounts.Values)
        {
            if (shouldImportMicrosoftAccounts && accounts.All(item => item.Id != account.Id))
            {
                accounts.Add(account);
                shouldPersistOrder = true;
            }
        }

        if (shouldPersistOrder || shouldImportMicrosoftAccounts)
        {
            logger.LogInformation(
                "Persisting account order after load. AccountCount={AccountCount} ImportedMicrosoftAccounts={ImportedMicrosoftAccounts}",
                accounts.Count,
                shouldImportMicrosoftAccounts);
            await SaveOrderAsync(state.SelectedAccountId, accounts, cancellationToken);
            state = await accountStateService.LoadAsync(cancellationToken);
        }

        logger.LogInformation(
            "Accounts loaded. AccountCount={AccountCount} MicrosoftAccountCount={MicrosoftAccountCount}",
            accounts.Count,
            accounts.Count(account => account.IsMicrosoft));
        return new AccountStoreSnapshot(accounts, state.SelectedAccountId);
    }

    public async Task SaveOrderAsync(
        string? selectedAccountId,
        IEnumerable<LauncherAccount> accounts,
        CancellationToken cancellationToken = default)
    {
        var state = await accountStateService.LoadAsync(cancellationToken);
        state.AccountsInitialized = true;
        state.MicrosoftAccountsImported = true;
        var records = accounts
            .Select(AccountMapper.ToRecord)
            .ToList();
        foreach (var account in records.Where(account => account.IsOffline))
            EnsureOfflineUuid(account);

        state.Accounts = records;
        state.SelectedAccountId = selectedAccountId;

        if (!string.IsNullOrWhiteSpace(state.SelectedAccountId)
            && state.Accounts.All(account => !string.Equals(account.Id, state.SelectedAccountId, StringComparison.Ordinal)))
        {
            state.SelectedAccountId = null;
        }

        var firstOfflineAccount = state.Accounts.FirstOrDefault(account => account.IsOffline);
        if (firstOfflineAccount is not null)
            state.OfflineUsername = firstOfflineAccount.DisplayName;

        await accountStateService.SaveAsync(state, cancellationToken);
        logger.LogInformation(
            "Account order saved. AccountCount={AccountCount} SelectedAccountId={SelectedAccountId}",
            state.Accounts.Count,
            state.SelectedAccountId);
    }

    private bool EnsureOfflineUuid(LauncherAccountRecord account)
    {
        var uuid = offlineUuidService.CreateUuid(
            account.DisplayName,
            account.OfflineUuidGenerationMode,
            account.Uuid);

        if (string.Equals(account.Uuid, uuid, StringComparison.Ordinal))
            return false;

        account.Uuid = uuid;
        return true;
    }

    private static bool ShouldPersistMergedMicrosoftAccount(
        LauncherAccountRecord storedAccount,
        LauncherAccount mergedAccount)
    {
        var mergedRecord = AccountMapper.ToRecord(mergedAccount);
        return !string.Equals(storedAccount.DisplayName, mergedRecord.DisplayName, StringComparison.Ordinal)
            || !string.Equals(storedAccount.Uuid, mergedRecord.Uuid, StringComparison.Ordinal)
            || !string.Equals(storedAccount.AvatarSource, mergedRecord.AvatarSource, StringComparison.Ordinal)
            || !string.Equals(storedAccount.SkinSource, mergedRecord.SkinSource, StringComparison.Ordinal)
            || storedAccount.SkinModel != mergedRecord.SkinModel
            || !string.Equals(storedAccount.ActiveSkinId, mergedRecord.ActiveSkinId, StringComparison.Ordinal)
            || !SkinRecordsEqual(storedAccount.Skins, mergedRecord.Skins)
            || !CapeRecordsEqual(storedAccount.Capes, mergedRecord.Capes);
    }

    private static bool SkinRecordsEqual(
        IReadOnlyList<LauncherSkinRecord> left,
        IReadOnlyList<LauncherSkinRecord> right)
    {
        if (left.Count != right.Count)
            return false;

        return left
            .Zip(right)
            .All(pair => string.Equals(pair.First.Id, pair.Second.Id, StringComparison.Ordinal)
                && string.Equals(pair.First.Source, pair.Second.Source, StringComparison.Ordinal)
                && pair.First.SkinModel == pair.Second.SkinModel
                && string.Equals(pair.First.ContentHash, pair.Second.ContentHash, StringComparison.Ordinal)
                && pair.First.AddedAtUtc == pair.Second.AddedAtUtc);
    }

    private static bool CapeRecordsEqual(
        IReadOnlyList<LauncherCapeRecord> left,
        IReadOnlyList<LauncherCapeRecord> right)
    {
        if (left.Count != right.Count)
            return false;

        return left
            .Zip(right)
            .All(pair => string.Equals(pair.First.Id, pair.Second.Id, StringComparison.Ordinal)
                && string.Equals(pair.First.DisplayName, pair.Second.DisplayName, StringComparison.Ordinal)
                && string.Equals(pair.First.ImageUrl, pair.Second.ImageUrl, StringComparison.Ordinal)
                && pair.First.IsActive == pair.Second.IsActive
                && pair.First.IsNone == pair.Second.IsNone);
    }
}
