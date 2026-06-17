using Launcher.Application.Accounts;
using Launcher.App.Resources;

namespace Launcher.App.Utilities;

internal static class AccountCapeTextProvider
{
    public static string GetDisplayName(AccountCapeOption cape)
    {
        if (cape.IsNone)
            return Strings.Cape_NoneState;

        return string.IsNullOrWhiteSpace(cape.DisplayName)
            ? Strings.Cape_UnnamedDisplayName
            : cape.DisplayName;
    }
}
