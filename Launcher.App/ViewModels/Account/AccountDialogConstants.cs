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

namespace Launcher.App.ViewModels.Account;

internal static class AccountDialogSteps
{
    public const string AddAccountType = "Type";
    public const string AddAccountOfflineName = "OfflineName";
    public const string AddAccountThirdPartyCredentials = "ThirdPartyCredentials";
    public const string AddAccountThirdPartyReauthentication = "ThirdPartyReauthentication";
    public const string AddAccountThirdPartyProfileSelection = "ThirdPartyProfileSelection";
    public const string AddAccountThirdPartyImportProgress = "ThirdPartyImportProgress";
    public const string AddAccountThirdPartyImportResult = "ThirdPartyImportResult";
    public const string AddAccountMicrosoftLogin = "MicrosoftLogin";
    public const string AddAccountMicrosoftResult = "MicrosoftResult";
    public const string AddAccountMicrosoftReauthentication = "MicrosoftReauthentication";
    public const string AddAccountMicrosoftReauthenticationResult = "MicrosoftReauthenticationResult";
    public const string RenameInput = "Input";
    public const string RenameStatus = "Status";
    public const string RenameResult = "Result";
}

internal static class AccountTypeKinds
{
    public const string Offline = "Offline";
    public const string ThirdParty = "ThirdParty";
    public const string Microsoft = "Microsoft";
}

