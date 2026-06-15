using Launcher.App.Models;
using Launcher.App.Resources;

namespace Launcher.App.ViewModels.Account;

internal static class AccountTypeOptionFactory
{
    public static IEnumerable<AccountTypeOption> Create()
    {
        return
        [
            new AccountTypeOption
            {
                Kind = AccountTypeKinds.Offline,
                Title = Strings.Account_TypeOfflineTitle,
                Description = Strings.Account_TypeOfflineDescription,
                Icon = "\uE77B",
                IconKey = "account_page/account_page_add_account_dialog_offline_user"
            },
            new AccountTypeOption
            {
                Kind = AccountTypeKinds.Microsoft,
                Title = Strings.Account_TypeMicrosoftTitle,
                Description = Strings.Account_TypeMicrosoftDescription,
                Icon = "\uE72E",
                IconKey = "account_page/account_page_add_account_dialog_online_user"
            }
        ];
    }
}

