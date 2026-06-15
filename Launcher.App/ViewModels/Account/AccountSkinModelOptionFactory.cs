using Launcher.Application.Accounts;
using Launcher.App.Models;
using Launcher.App.Resources;

namespace Launcher.App.ViewModels.Account;

internal static class AccountSkinModelOptionFactory
{
    public static IEnumerable<AccountSkinModelOption> Create()
    {
        return
        [
            new AccountSkinModelOption
            {
                Model = MinecraftSkinModel.Classic,
                Title = Strings.Account_SkinModelClassicTitle,
                Description = Strings.Account_SkinModelClassicDescription
            },
            new AccountSkinModelOption
            {
                Model = MinecraftSkinModel.Slim,
                Title = Strings.Account_SkinModelSlimTitle,
                Description = Strings.Account_SkinModelSlimDescription
            }
        ];
    }
}

