using Launcher.Application.Accounts;
using Launcher.Domain.Models;
using Launcher.Application.Services;

namespace Launcher.Application.Accounts;

public sealed class AccountStore : IAccountStore
{
    private readonly ISettingsService settingsService;
    private readonly IMicrosoftAccountService microsoftAccountService;
    private readonly IOfflineAccountUuidService offlineUuidService;

    public AccountStore(
        ISettingsService settingsService,
        IMicrosoftAccountService microsoftAccountService,
        IOfflineAccountUuidService offlineUuidService)
    {
        this.settingsService = settingsService;
        this.microsoftAccountService = microsoftAccountService;
        this.offlineUuidService = offlineUuidService;
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
                accounts.Add(AccountMapper.MergeStoredRecord(microsoftAccount, account));
            else
                shouldPersistOrder = true;
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
            await SaveOrderAsync(settings, accounts);

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
}
