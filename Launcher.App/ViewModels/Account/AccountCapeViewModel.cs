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
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Utilities;
using Launcher.Application.Accounts;
using Microsoft.Extensions.Logging;

namespace Launcher.App.ViewModels.Account;

public sealed partial class AccountCapeViewModel : ObservableObject
{
    private readonly AccountListViewModel accountList;
    private readonly IMicrosoftAccountService microsoftAccountService;
    private readonly AccountProfileViewModel profile;
    private readonly ILogger logger;

    internal AccountCapeViewModel(
        AccountListViewModel accountList,
        IMicrosoftAccountService microsoftAccountService,
        AccountProfileViewModel profile,
        ILogger logger)
    {
        this.accountList = accountList;
        this.microsoftAccountService = microsoftAccountService;
        this.profile = profile;
        this.logger = logger;
        profile.PropertyChanged += (_, _) => NotifyState();
    }

    [ObservableProperty]
    private AccountCapeOption? selectedOption;

    public ObservableCollection<AccountCapeOption> Options { get; } = [];

    public AccountCapeOption? PreviousOption => GetAdjacent(-1);

    public AccountCapeOption? NextOption => GetAdjacent(1);

    public bool HasOptions => Options.Count > 0;

    public bool IsOffline => accountList.SelectedAccount?.IsOffline == true;

    public bool HasPreview => !IsOffline && SelectedOption is not null;

    public bool CanShowPreviewEmptyState => accountList.SelectedAccount is not null && !IsOffline && !HasPreview;

    public bool CanApply => accountList.SelectedAccount is { IsOffline: false }
        && !profile.IsBusy
        && SelectedOption is { IsActive: false };

    public bool CanRefresh => accountList.SelectedAccount is { IsOffline: false } && !profile.IsBusy;

    public void SetAccount(LauncherAccount? account)
    {
        if (account is null || account.IsOffline)
        {
            Options.Clear();
            SelectedOption = null;
        }
        else
        {
            Populate(account.CachedCapeOptions);
        }
        NotifyState();
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    public async Task RefreshAsync()
    {
        var account = accountList.SelectedAccount;
        if (account is null)
            return;
        if (account.IsOffline)
        {
            profile.SetMessage(Strings.Account_ProfileOfflineUnsupported);
            return;
        }
        var operation = profile.BeginOperation(account, Strings.Status_LoadingAccountProfile);
        try
        {
            var capes = await microsoftAccountService.GetCapesAsync(account, operation.Token);
            if (!profile.IsCurrent(account, operation))
                return;
            var hasCapes = capes.Any(cape => !cape.IsNone);
            Populate(capes);
            await StoreCacheAsync(account);
            profile.SetMessage(hasCapes ? Strings.Account_ProfileLoaded : Strings.Account_ProfileNoCapes);
        }
        catch (OperationCanceledException) when (!profile.IsCurrent(account, operation))
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Microsoft account cape load failed. AccountId={AccountId}", account.Id);
            profile.SetError(
                exception,
                AccountProfileViewModel.GetRefreshFailureMessage(exception, Strings.Status_LoadAccountProfileFailed),
                showFloating: true);
        }
        finally
        {
            profile.Complete(account, operation);
        }
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    public async Task ApplyAsync()
    {
        var account = accountList.SelectedAccount;
        var cape = SelectedOption;
        if (account is null || cape is null || !CanApply)
            return;
        var operation = profile.BeginOperation(account, Strings.Status_ChangingCape);
        try
        {
            await microsoftAccountService.SetActiveCapeAsync(account, cape.Id);
            if (!profile.IsCurrent(account, operation))
                return;
            var message = cape.IsNone
                ? Strings.Status_CapeRemoved
                : string.Format(Strings.Status_CapeChangedFormat, AccountCapeTextProvider.GetDisplayName(cape));
            MarkActive(cape);
            await StoreCacheAsync(account);
            profile.SetMessage(message, showFloating: true);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Microsoft account cape change failed. AccountId={AccountId}", account.Id);
            profile.SetMessage(Strings.Status_CapeChangeFailed, showFloating: true);
        }
        finally
        {
            profile.Complete(account, operation);
        }
    }

    [RelayCommand(CanExecute = nameof(CanSelectPrevious))]
    public void SelectPrevious()
    {
        if (PreviousOption is { } option)
            SelectedOption = option;
    }

    public bool CanSelectPrevious() => PreviousOption is not null;

    [RelayCommand(CanExecute = nameof(CanSelectNext))]
    public void SelectNext()
    {
        if (NextOption is { } option)
            SelectedOption = option;
    }

    public bool CanSelectNext() => NextOption is not null;

    partial void OnSelectedOptionChanged(AccountCapeOption? value) => NotifyState();

    private void Populate(IEnumerable<AccountCapeOption> capes)
    {
        var normalizedCapes = Normalize(capes).ToList();
        Options.Clear();
        foreach (var cape in normalizedCapes)
            Options.Add(cape);
        SelectedOption = Options.FirstOrDefault(cape => cape.IsActive) ?? Options.FirstOrDefault();
        NotifyState();
    }

    private void MarkActive(AccountCapeOption active)
    {
        Populate(Options.Select(cape => new AccountCapeOption
        {
            Id = cape.Id,
            DisplayName = cape.DisplayName,
            ImageUrl = cape.ImageUrl,
            IsNone = cape.IsNone,
            IsActive = cape.IsNone == active.IsNone && string.Equals(cape.Id, active.Id, StringComparison.OrdinalIgnoreCase)
        }));
    }

    private async Task StoreCacheAsync(LauncherAccount expectedAccount)
    {
        var current = accountList.SelectedAccount;
        if (current is null || !string.Equals(current.Id, expectedAccount.Id, StringComparison.Ordinal))
            return;
        accountList.ReplaceSelectedAccount(current, AccountMapper.WithCapeCache(current, Options.ToList()));
        await accountList.PersistAccountOrderAsync();
    }

    private AccountCapeOption? GetAdjacent(int offset)
    {
        if (SelectedOption is null || Options.Count < 2)
            return null;
        var index = Options.IndexOf(SelectedOption) + offset;
        return index >= 0 && index < Options.Count ? Options[index] : null;
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(PreviousOption));
        OnPropertyChanged(nameof(NextOption));
        OnPropertyChanged(nameof(HasOptions));
        OnPropertyChanged(nameof(IsOffline));
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(CanShowPreviewEmptyState));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanRefresh));
        ApplyCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
        SelectPreviousCommand.NotifyCanExecuteChanged();
        SelectNextCommand.NotifyCanExecuteChanged();
    }

    private static IEnumerable<AccountCapeOption> Normalize(IEnumerable<AccountCapeOption> capes)
    {
        var source = capes.ToList();
        var hasActive = source.Any(cape => !cape.IsNone && cape.IsActive);
        var none = source.FirstOrDefault(cape => cape.IsNone);
        yield return none is null
            ? new AccountCapeOption { DisplayName = string.Empty, IsNone = true, IsActive = !hasActive }
            : new AccountCapeOption
            {
                Id = null,
                DisplayName = string.Empty,
                ImageUrl = none.ImageUrl,
                IsActive = !hasActive && none.IsActive,
                IsNone = true
            };
        foreach (var cape in source.Where(cape => !cape.IsNone))
            yield return cape;
    }
}
