using Launcher.Application.Accounts;
using Launcher.Domain.Models;
using Launcher.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Application.Accounts;

public sealed class AccountStore : IAccountStore
{
    private readonly ISettingsService settingsService;
    private readonly IMicrosoftAccountService microsoftAccountService;
    private readonly IOfflineAccountUuidService offlineUuidService;
    private readonly ILogger<AccountStore> logger;

    public AccountStore(
        ISettingsService settingsService,
        IMicrosoftAccountService microsoftAccountService,
        IOfflineAccountUuidService offlineUuidService,
        ILogger<AccountStore>? logger = null)
    {
        this.settingsService = settingsService;
        this.microsoftAccountService = microsoftAccountService;
        this.offlineUuidService = offlineUuidService;
        this.logger = logger ?? NullLogger<AccountStore>.Instance;
    }

    public async Task<IReadOnlyList<LauncherAccount>> LoadAsync(LauncherSettings settings)
    {
        var accounts = new List<LauncherAccount>();
        var microsoftAccounts = new Dictionary<string, LauncherAccount>(StringComparer.OrdinalIgnoreCase);
        foreach (var account in await microsoftAccountService.GetSavedAccountsAsync())
        {
            if (!microsoftAccounts.ContainsKey(account.Id))
                microsoftAccounts.Add(account.Id, account);
        }

        var shouldImportMicrosoftAccounts = !settings.MicrosoftAccountsImported;
        var shouldPersistOrder = false;

        foreach (var account in settings.Accounts)
        {
            if (account.IsOffline)
            {
                shouldPersistOrder |= EnsureOfflineUuid(account);
                accounts.Add(AccountMapper.FromOfflineRecord(account));
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
            await SaveOrderAsync(settings, accounts);
        }

        logger.LogInformation(
            "Accounts loaded. AccountCount={AccountCount} MicrosoftAccountCount={MicrosoftAccountCount}",
            accounts.Count,
            accounts.Count(account => !account.IsOffline));
        return accounts;
    }

    public async Task SaveOrderAsync(LauncherSettings settings, IEnumerable<LauncherAccount> accounts)
    {
        settings.AccountsInitialized = true;
        settings.MicrosoftAccountsImported = true;
        var records = accounts
            .Select(AccountMapper.ToRecord)
            .ToList();
        foreach (var account in records.Where(account => account.IsOffline))
            EnsureOfflineUuid(account);

        settings.Accounts = records;

        if (!string.IsNullOrWhiteSpace(settings.SelectedAccountId)
            && settings.Accounts.All(account => !string.Equals(account.Id, settings.SelectedAccountId, StringComparison.Ordinal)))
        {
            settings.SelectedAccountId = null;
        }

        var firstOfflineAccount = settings.Accounts.FirstOrDefault(account => account.IsOffline);
        if (firstOfflineAccount is not null)
            settings.OfflineUsername = firstOfflineAccount.DisplayName;

        await settingsService.SaveAsync(settings);
        logger.LogInformation(
            "Account order saved. AccountCount={AccountCount} SelectedAccountId={SelectedAccountId}",
            settings.Accounts.Count,
            settings.SelectedAccountId);
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
            || !CapeRecordsEqual(storedAccount.Capes, mergedRecord.Capes);
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
