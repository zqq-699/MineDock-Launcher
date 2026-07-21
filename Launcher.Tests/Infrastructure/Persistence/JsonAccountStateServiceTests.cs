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

using Launcher.Domain.Models;
using Launcher.Infrastructure.Persistence;

namespace Launcher.Tests.Infrastructure.Persistence;

public sealed class JsonAccountStateServiceTests : TestTempDirectory
{
    [Fact]
    public async Task AccountStateServiceWritesAndLoadsDefaults()
    {
        var service = new JsonAccountStateService(accountDataDirectory: TempRoot);

        var state = await service.LoadAsync();

        Assert.True(state.AccountsInitialized);
        Assert.False(state.MicrosoftAccountsImported);
        Assert.Equal(LauncherDefaults.DefaultOfflineUsername, state.OfflineUsername);
        Assert.Empty(state.Accounts);
        Assert.True(File.Exists(Path.Combine(TempRoot, "account-state.json")));
    }

    [Fact]
    public async Task AccountStateServicePreservesMixedAccountOrderAndSelectedAccount()
    {
        var service = new JsonAccountStateService(accountDataDirectory: TempRoot);
        var state = await service.LoadAsync();
        state.SelectedAccountId = "microsoft-alex";
        state.MicrosoftAccountsImported = true;
        state.Accounts =
        [
            new LauncherAccountRecord
            {
                Id = "offline-first",
                DisplayName = "First",
                IsOffline = true
            },
            new LauncherAccountRecord
            {
                Id = "microsoft-alex",
                DisplayName = "Alex",
                Uuid = "alexuuid",
                IsOffline = false
            },
            new LauncherAccountRecord
            {
                Id = "offline-last",
                DisplayName = "Last",
                IsOffline = true
            }
        ];

        await service.SaveAsync(state);
        var loaded = await service.LoadAsync();

        Assert.Equal("microsoft-alex", loaded.SelectedAccountId);
        Assert.True(loaded.MicrosoftAccountsImported);
        Assert.Equal(
            ["offline-first", "microsoft-alex", "offline-last"],
            loaded.Accounts.Select(account => account.Id));
    }

}
