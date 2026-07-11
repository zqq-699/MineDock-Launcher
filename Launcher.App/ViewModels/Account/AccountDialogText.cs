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

using Launcher.App.Resources;

namespace Launcher.App.ViewModels.Account;

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
            AccountDialogSteps.AddAccountThirdPartyCredentials => Strings.Dialog_AddThirdPartyAccountTitle,
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
            AccountDialogSteps.AddAccountThirdPartyCredentials => Strings.Dialog_AddThirdPartyAccountSubtitle,
            AccountDialogSteps.AddAccountMicrosoftLogin => Strings.Dialog_AddMicrosoftAccountSubtitle,
            AccountDialogSteps.AddAccountMicrosoftResult => Strings.Dialog_AddAccountResultSubtitle,
            _ => Strings.Dialog_AddAccountSubtitle
        };
    }
}

