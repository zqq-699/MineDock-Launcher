using Launcher.App.Models;
using Launcher.Core.Models;
using Launcher.Core.Services;

namespace Launcher.App.Services;

public sealed class AccountStore : IAccountStore
{
    private readonly ISettingsService settingsService;
    private readonly IMicrosoftAccountService microsoftAccountService;

    public AccountStore(
        ISettingsService settingsService,
        IMicrosoftAccountService microsoftAccountService)
    {
        this.settingsService = settingsService;
        this.microsoftAccountService = microsoftAccountService;
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
        settings.Accounts = accounts
            .Select(AccountMapper.ToRecord)
            .ToList();

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
}
