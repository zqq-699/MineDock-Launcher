using Launcher.App.Resources;
using Launcher.Application.Accounts;

namespace Launcher.App.ViewModels.Account;

internal static class AccountErrorCodeMessageFormatter
{
    public static string Format(Exception exception)
    {
        var errorCode = exception switch
        {
            MicrosoftAccountNameChangeException { ErrorCode: { Length: > 0 } code } => code,
            MicrosoftAccountSkinUpdateException { ErrorCode: { Length: > 0 } code } => code,
            MicrosoftAccountProfileRefreshException { ErrorCode: { Length: > 0 } code } => code,
            _ => null
        };

        return string.IsNullOrWhiteSpace(errorCode)
            ? string.Empty
            : string.Format(Strings.Status_ErrorCodeFormat, errorCode);
    }
}
