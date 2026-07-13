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

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Application.Accounts;
using Launcher.App.Models;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.Account;

public sealed partial class AccountDialogViewModel
{
    public void SelectAllThirdPartyProfiles() => ThirdParty.SelectAllProfiles();

    public Task RetryThirdPartyProfileImportAsync(string password) =>
        ImportThirdPartyProfilesAsync(thirdPartyFailedProfiles.ToArray(), password);

    private async Task ImportThirdPartyProfilesAsync(
        IReadOnlyList<ThirdPartyProfileOptionViewModel> profiles,
        string password)
    {
        if (profiles.Count == 0)
            return;
        thirdPartyFailedProfiles.Clear();
        ThirdPartyImportFailedCount = 0;
        ThirdPartyImportCompletedCount = 0;
        ThirdPartyImportTotalCount = profiles.Count;
        AddAccountDialogStep = AccountDialogSteps.AddAccountThirdPartyImportProgress;
        IsAddAccountDialogBusy = true;
        using var cancellation = new CancellationTokenSource();
        thirdPartyImportCancellationTokenSource = cancellation;
        try
        {
            foreach (var profile in profiles)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                ThirdPartyImportCurrentProfileName = profile.Name;
                var imported = await ThirdParty.ImportEmailProfileAsync(profile, password, cancellation.Token);
                if (imported is null)
                {
                    thirdPartyFailedProfiles.Add(profile);
                }
                else if (thirdPartySuccessfulAccounts.All(account => !string.Equals(account.Id, imported.Id, StringComparison.Ordinal)))
                {
                    thirdPartySuccessfulAccounts.Add(imported);
                }
                ThirdPartyImportCompletedCount++;
            }

            await SelectFirstSuccessfulThirdPartyAccountAsync();
            ThirdPartyImportFailedCount = thirdPartyFailedProfiles.Count;
            if (thirdPartyFailedProfiles.Count == 0)
            {
                IsAddAccountDialogOpen = false;
                await ThirdParty.CancelEmailLoginAsync();
            }
            else
            {
                AddAccountDialogStep = AccountDialogSteps.AddAccountThirdPartyImportResult;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            await SelectFirstSuccessfulThirdPartyAccountAsync();
            IsAddAccountDialogOpen = false;
            await ThirdParty.CancelEmailLoginAsync();
        }
        finally
        {
            thirdPartyImportCancellationTokenSource = null;
            IsAddAccountDialogBusy = false;
        }
    }
}
