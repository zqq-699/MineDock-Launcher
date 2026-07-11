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

using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.App.Services;
using Launcher.Application.Accounts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.Account;

public sealed class AccountAppearanceViewModel : ObservableObject, IDisposable
{
    private readonly AccountListViewModel accountList;
    private readonly AccountAppearanceOperationCoordinator operations;

    public AccountAppearanceViewModel(
        AccountListViewModel accountList,
        IMicrosoftAccountService microsoftAccountService,
        IThirdPartyAccountService thirdPartyAccountService,
        IAccountSkinLibraryService skinLibraryService,
        AccountSkinModelDialogViewModel skinModelDialog,
        IAccountDialogService dialogService,
        IFilePickerService filePickerService,
        IMinecraftSkinFileValidator skinFileValidator,
        ILogger<AccountAppearanceViewModel>? logger = null,
        IFloatingMessageService? floatingMessageService = null)
    {
        this.accountList = accountList;
        var resolvedLogger = logger ?? NullLogger<AccountAppearanceViewModel>.Instance;
        operations = new AccountAppearanceOperationCoordinator();
        Profile = new AccountProfileViewModel(
            accountList,
            microsoftAccountService,
            thirdPartyAccountService,
            operations,
            floatingMessageService,
            resolvedLogger);
        SkinLibrary = new AccountSkinLibraryViewModel(
            accountList,
            microsoftAccountService,
            skinLibraryService,
            skinModelDialog,
            dialogService,
            filePickerService,
            skinFileValidator,
            Profile,
            resolvedLogger);
        Cape = new AccountCapeViewModel(accountList, microsoftAccountService, Profile, resolvedLogger);

        Profile.PropertyChanged += Child_PropertyChanged;
        SkinLibrary.PropertyChanged += Child_PropertyChanged;
        Cape.PropertyChanged += Child_PropertyChanged;
        accountList.PropertyChanged += AccountList_PropertyChanged;
        ApplySelectedAccount(accountList.SelectedAccount);
    }

    public AccountProfileViewModel Profile { get; }

    public AccountSkinLibraryViewModel SkinLibrary { get; }

    public AccountCapeViewModel Cape { get; }

    public Task RefreshAccountsSilentlyAsync() => Profile.RefreshAccountsSilentlyAsync();

    public Task RefreshCurrentSecondaryContentAsync()
    {
        return accountList.SelectedAccount is null || Profile.IsBusy
            ? Task.CompletedTask
            : Cape.RefreshAsync();
    }

    public void Dispose()
    {
        accountList.PropertyChanged -= AccountList_PropertyChanged;
        Profile.PropertyChanged -= Child_PropertyChanged;
        SkinLibrary.PropertyChanged -= Child_PropertyChanged;
        Cape.PropertyChanged -= Child_PropertyChanged;
        operations.Dispose();
    }

    private void AccountList_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AccountListViewModel.SelectedAccount))
            ApplySelectedAccount(accountList.SelectedAccount);
    }

    private void ApplySelectedAccount(LauncherAccount? account)
    {
        Profile.SetAccount(account);
        SkinLibrary.SetAccount(account);
        Cape.SetAccount(account);
        OnPropertyChanged(nameof(Profile));
        OnPropertyChanged(nameof(SkinLibrary));
        OnPropertyChanged(nameof(Cape));
    }

    private void Child_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, Profile))
            OnPropertyChanged(nameof(Profile));
        else if (ReferenceEquals(sender, SkinLibrary))
            OnPropertyChanged(nameof(SkinLibrary));
        else if (ReferenceEquals(sender, Cape))
            OnPropertyChanged(nameof(Cape));
    }
}
