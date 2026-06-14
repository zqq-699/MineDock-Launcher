using Launcher.App.Resources;

namespace Launcher.App.ViewModels;

internal static class AccountDialogText
{
    public static string GetRenameTitle(string step, bool isSuccessful)
    {
        return step switch
        {
            AccountDialogSteps.RenameStatus => Strings.Dialog_RenameAccountBusyTitle,
            AccountDialogSteps.RenameResult => isSuccessful ? Strings.Dialog_RenameAccountSuccessTitle : Strings.Dialog_RenameAccountFailedTitle,
            _ => Strings.Dialog_RenameAccountTitle
        };
    }

    public static string GetRenameSubtitle(string step, bool isMicrosoftAccount)
    {
        return step switch
        {
            AccountDialogSteps.RenameStatus => Strings.Dialog_RenameAccountBusySubtitle,
            AccountDialogSteps.RenameResult => Strings.Dialog_RenameAccountResultSubtitle,
            _ => isMicrosoftAccount
                ? Strings.Dialog_RenameMicrosoftAccountSubtitle
                : Strings.Dialog_RenameOfflineAccountSubtitle
        };
    }

    public static string GetAddTitle(string step, bool isMicrosoftAccountAlreadyAdded, bool isMicrosoftLoginSuccessful)
    {
        return step switch
        {
            AccountDialogSteps.AddAccountOfflineName => Strings.Dialog_AddOfflineAccountTitle,
            AccountDialogSteps.AddAccountMicrosoftLogin => Strings.Dialog_AddMicrosoftAccountTitle,
            AccountDialogSteps.AddAccountMicrosoftResult => isMicrosoftAccountAlreadyAdded
                ? Strings.Dialog_AddAccountAlreadyExistsTitle
                : isMicrosoftLoginSuccessful ? Strings.Dialog_LoginSuccessTitle : Strings.Dialog_LoginIncompleteTitle,
            _ => Strings.Dialog_AddAccountTitle
        };
    }

    public static string GetAddSubtitle(string step)
    {
        return step switch
        {
            AccountDialogSteps.AddAccountOfflineName => Strings.Dialog_AddOfflineAccountSubtitle,
            AccountDialogSteps.AddAccountMicrosoftLogin => Strings.Dialog_AddMicrosoftAccountSubtitle,
            AccountDialogSteps.AddAccountMicrosoftResult => Strings.Dialog_AddAccountResultSubtitle,
            _ => Strings.Dialog_AddAccountSubtitle
        };
    }
}
