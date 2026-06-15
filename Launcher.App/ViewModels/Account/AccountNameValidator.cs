using System.Text.RegularExpressions;
using Launcher.App.Resources;

namespace Launcher.App.ViewModels.Account;

internal static class AccountNameValidator
{
    private static readonly Regex AccountNameRegex = new("^[A-Za-z0-9_]{3,16}$", RegexOptions.CultureInvariant);

    public static string ValidationMessage => Strings.Account_UsernameValidation;

    public static bool IsValid(string accountName)
    {
        return AccountNameRegex.IsMatch(accountName);
    }
}

