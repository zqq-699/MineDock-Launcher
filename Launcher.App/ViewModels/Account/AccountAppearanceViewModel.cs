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
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Application.Accounts;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.Utilities;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.Account;

public sealed partial class AccountAppearanceViewModel : ObservableObject
{
    private readonly AccountListViewModel accountList;
    private readonly IMicrosoftAccountService microsoftAccountService;
    private readonly IAccountSkinLibraryService skinLibraryService;
    private readonly IAccountDialogService dialogService;
    private readonly IFilePickerService filePickerService;
    private readonly IMinecraftSkinFileValidator skinFileValidator;
    private readonly ILogger<AccountAppearanceViewModel> logger;
    private readonly IFloatingMessageService floatingMessageService;
    private LauncherSkinRecord? skinPendingModelChange;

    public AccountAppearanceViewModel(
        AccountListViewModel accountList,
        IMicrosoftAccountService microsoftAccountService,
        IAccountSkinLibraryService skinLibraryService,
        AccountSkinModelDialogViewModel skinModelDialog,
        IAccountDialogService dialogService,
        IFilePickerService filePickerService,
        IMinecraftSkinFileValidator skinFileValidator,
        ILogger<AccountAppearanceViewModel>? logger = null,
        IFloatingMessageService? floatingMessageService = null)
    {
        this.accountList = accountList;
        this.microsoftAccountService = microsoftAccountService;
        this.skinLibraryService = skinLibraryService;
        SkinModelDialog = skinModelDialog;
        this.dialogService = dialogService;
        this.filePickerService = filePickerService;
        this.skinFileValidator = skinFileValidator;
        this.logger = logger ?? NullLogger<AccountAppearanceViewModel>.Instance;
        this.floatingMessageService = floatingMessageService ?? NullFloatingMessageService.Instance;
        Profile = new AccountProfileViewModel();
        SkinLibrary = new AccountSkinLibraryViewModel();
        Cape = new AccountCapeViewModel();
        Profile.PropertyChanged += Profile_PropertyChanged;
        SkinLibrary.PropertyChanged += SkinLibrary_PropertyChanged;
        Cape.PropertyChanged += Cape_PropertyChanged;
        this.accountList.PropertyChanged += AccountList_PropertyChanged;
    }

    public AccountSkinModelDialogViewModel SkinModelDialog { get; }

    public AccountProfileViewModel Profile { get; }

    public AccountSkinLibraryViewModel SkinLibrary { get; }

    public AccountCapeViewModel Cape { get; }

    public bool CanChangeSelectedAccountSkin => accountList.SelectedAccount is not null
        && !accountList.SelectedAccount.IsOffline;

    public bool CanManageSelectedAccountSkins => accountList.SelectedAccount is not null
        && !accountList.SelectedAccount.IsOffline;

    public bool CanApplySelectedAccountSkin => accountList.SelectedAccount is not null
        && !accountList.SelectedAccount.IsOffline
        && !Profile.IsBusy
        && SkinLibrary.SelectedSkin is not null
        && !IsSelectedSkinAlreadyApplied(accountList.SelectedAccount, SkinLibrary.SelectedSkin);

    public bool CanEditSelectedAccountSkinLibraryItem => accountList.SelectedAccount is not null
        && !accountList.SelectedAccount.IsOffline
        && !Profile.IsBusy
        && SkinLibrary.SelectedSkin is not null;

    public bool CanDeleteSelectedAccountSkin => CanEditSelectedAccountSkinLibraryItem
        && accountList.SelectedAccount is not null
        && SkinLibrary.SelectedSkin is not null
        && !string.Equals(accountList.SelectedAccount.ActiveSkinId, SkinLibrary.SelectedSkin.Id, StringComparison.Ordinal);

    public bool CanEditSelectedMicrosoftAccount => accountList.SelectedAccount is not null
        && !accountList.SelectedAccount.IsOffline
        && !Profile.IsBusy;

    public bool CanShowSelectedAccountAppearanceOfflineState => accountList.SelectedAccount?.IsOffline == true;

    public bool CanApplySelectedCape => accountList.SelectedAccount is not null
        && !accountList.SelectedAccount.IsOffline
        && !Profile.IsBusy
        && Cape.SelectedOption is not null
        && !Cape.SelectedOption.IsActive;

    public bool HasSelectedAccountCapes => Cape.Options.Count > 0;

    public bool HasSelectedAccountCapePreview => !CanShowSelectedAccountAppearanceOfflineState
        && Cape.SelectedOption is not null;

    public bool CanShowSelectedAccountCapePreviewEmptyState => accountList.SelectedAccount is not null
        && !CanShowSelectedAccountAppearanceOfflineState
        && !HasSelectedAccountCapePreview;

    public bool HasSelectedAccountSkins => SkinLibrary.Skins.Count > 0;

    public bool CanShowSkinManagerEmptyState => !HasSelectedAccountSkins;

    public bool HasSelectedAccountSkinPreview => !CanShowSelectedAccountAppearanceOfflineState
        && SkinLibrary.SelectedSkin is not null;

    public bool CanShowSelectedAccountSkinPreviewEmptyState => accountList.SelectedAccount is not null
        && !CanShowSelectedAccountAppearanceOfflineState
        && !HasSelectedAccountSkinPreview;

    public LauncherSkinRecord? PreviousAccountSkin => SkinLibrary.PreviousSkin;

    public LauncherSkinRecord? NextAccountSkin => SkinLibrary.NextSkin;

    public bool HasPreviousAccountSkin => PreviousAccountSkin is not null;

    public bool HasNextAccountSkin => NextAccountSkin is not null;

    public AccountCapeOption? PreviousAccountCape => Cape.PreviousOption;

    public AccountCapeOption? NextAccountCape => Cape.NextOption;

    public bool HasPreviousAccountCape => PreviousAccountCape is not null;

    public bool HasNextAccountCape => NextAccountCape is not null;

    public bool HasAccountProfileErrorCode => !string.IsNullOrWhiteSpace(Profile.ErrorCodeMessage);

    public string? ActiveSkinId => accountList.SelectedAccount?.ActiveSkinId;

    public async Task ConfirmSkinModelDialogAsync()
    {
        if (SkinModelDialog.IsSkinFormatError)
        {
            SkinModelDialog.Cancel();
            return;
        }

        if (!SkinModelDialog.TryConsumeSelection(out var skinFilePath, out var skinModel))
            return;

        if (skinPendingModelChange is not null)
        {
            var skin = skinPendingModelChange;
            skinPendingModelChange = null;
            await ChangeSelectedAccountSkinModelAsync(skin, skinModel);
            return;
        }

        await AddSelectedAccountSkinAsync(skinFilePath, skinModel);
    }

    public async Task AddSelectedAccountSkinAsync(string skinFilePath, MinecraftSkinModel skinModel)
    {
        var account = accountList.SelectedAccount;
        if (account is null)
            return;

        if (account.IsOffline)
        {
            Profile.Message = Strings.Status_SkinOfflineUnsupported;
            return;
        }

        try
        {
            BeginAccountProfileOperation(Strings.Status_AddingSkin);
            var importedSkin = await skinLibraryService.ImportSkinAsync(account, skinFilePath, skinModel);
            var skins = AddOrReplaceSkin(account.SkinLibrary, importedSkin);
            var updatedAccount = AccountMapper.WithSkinLibrary(
                account,
                skins,
                account.ActiveSkinId,
                account.SkinSource,
                account.SkinModel);
            accountList.ReplaceSelectedAccount(account, updatedAccount);
            PopulateSelectedAccountSkins(updatedAccount, importedSkin.Id);
            await accountList.PersistAccountOrderAsync();
            SetAccountProfileMessage(Strings.Status_SkinAdded);
        }
        catch (Exception ex)
        {
            var errorCodeMessage = FormatAccountProfileError(ex);
            logger.LogWarning(
                ex,
                "Microsoft account skin change failed. AccountId={AccountId} SkinModel={SkinModel} ErrorCode={ErrorCode}",
                account.Id,
                skinModel,
                errorCodeMessage);
            SetAccountProfileError(ex, Strings.Status_SkinUpdateFailed);
        }
        finally
        {
            Profile.IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanApplySelectedAccountSkin))]
    public async Task ApplySelectedAccountSkinAsync()
    {
        var account = accountList.SelectedAccount;
        var skin = SkinLibrary.SelectedSkin;
        if (account is null || skin is null || !CanApplySelectedAccountSkin)
            return;

        if (account.IsOffline)
        {
            Profile.Message = Strings.Status_SkinOfflineUnsupported;
            return;
        }

        try
        {
            BeginAccountProfileOperation(Strings.Status_UploadingSkin);
            var skinPath = ResolveLocalSkinPath(skin.Source);
            var uploadedAccount = await microsoftAccountService.UploadSkinAsync(account, skinPath, skin.SkinModel);
            var updatedAccount = AccountMapper.WithCapeCache(
                AccountMapper.WithSkinLibrary(
                    AccountMapper.WithAppearanceFallback(uploadedAccount, account),
                    account.SkinLibrary,
                    skin.Id,
                    skin.Source,
                    skin.SkinModel),
                account.CachedCapeOptions);
            accountList.ReplaceSelectedAccount(account, updatedAccount);
            PopulateSelectedAccountSkins(updatedAccount, skin.Id);
            await accountList.PersistAccountOrderAsync();
            SetAccountProfileMessage(Strings.Status_SkinUpdated, showFloating: true);
        }
        catch (Exception ex)
        {
            var errorCodeMessage = FormatAccountProfileError(ex);
            logger.LogWarning(
                ex,
                "Microsoft account skin apply failed. AccountId={AccountId} SkinId={SkinId} SkinModel={SkinModel} ErrorCode={ErrorCode}",
                account.Id,
                skin.Id,
                skin.SkinModel,
                errorCodeMessage);
            SetAccountProfileError(ex, Strings.Status_SkinUpdateFailed, showFloating: true);
        }
        finally
        {
            Profile.IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task PickAndChangeSelectedAccountSkinAsync()
    {
        if (!CanChangeSelectedAccountSkin)
            return;

        var skinFilePath = filePickerService.PickMinecraftSkin();
        if (string.IsNullOrWhiteSpace(skinFilePath))
            return;

        var validation = await skinFileValidator.ValidateAsync(skinFilePath);
        if (validation.IsValid)
            dialogService.ShowSkinModelDialog(skinFilePath);
        else
            dialogService.ShowSkinFormatErrorDialog();
    }

    [RelayCommand(CanExecute = nameof(CanManageSelectedAccountSkins))]
    public void RequestOpenSkinManagerDialog()
    {
        dialogService.ShowSkinManagerDialog();
    }

    [RelayCommand]
    public void RequestCancelSkinManagerDialog()
    {
        dialogService.CancelSkinManagerDialog();
    }

    public void OpenSkinManagerDialog()
    {
        if (!CanManageSelectedAccountSkins)
            return;

        SkinLibrary.IsManagerDialogOpen = true;
    }

    public void CloseSkinManagerDialog()
    {
        SkinLibrary.IsManagerDialogOpen = false;
    }

    [RelayCommand]
    public void SelectAccountSkin(LauncherSkinRecord? skin)
    {
        if (skin is null || !SkinLibrary.Skins.Any(candidate =>
            string.Equals(candidate.Id, skin.Id, StringComparison.Ordinal)))
        {
            return;
        }

        SkinLibrary.SelectedSkin = skin;
    }

    [RelayCommand(CanExecute = nameof(CanEditSelectedAccountSkinLibraryItem))]
    public void ChangeSelectedAccountSkinModel()
    {
        var skin = SkinLibrary.SelectedSkin;
        if (skin is null || !CanEditSelectedAccountSkinLibraryItem)
            return;

        skinPendingModelChange = skin;
        dialogService.ShowSkinModelDialog(skin.SkinModel);
    }

    [RelayCommand(CanExecute = nameof(CanChangeAccountSkinModel))]
    public void ChangeAccountSkinModel(LauncherSkinRecord? skin)
    {
        if (!CanChangeAccountSkinModel(skin))
            return;

        SkinLibrary.SelectedSkin = skin;
        ChangeSelectedAccountSkinModel();
    }

    public bool CanChangeAccountSkinModel(LauncherSkinRecord? skin)
    {
        return accountList.SelectedAccount is not null
            && !accountList.SelectedAccount.IsOffline
            && !Profile.IsBusy
            && skin is not null;
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelectedAccountSkin))]
    public async Task DeleteSelectedAccountSkinAsync()
    {
        var account = accountList.SelectedAccount;
        var skin = SkinLibrary.SelectedSkin;
        if (account is null || skin is null || !CanDeleteSelectedAccountSkin)
            return;

        try
        {
            BeginAccountProfileOperation();
            await skinLibraryService.DeleteSkinAsync(account, skin);

            var updatedSkins = RemoveMatchingSkin(account.SkinLibrary, skin);
            var updatedAccount = AccountMapper.WithSkinLibrary(
                account,
                updatedSkins,
                account.ActiveSkinId,
                account.SkinSource,
                account.SkinModel);
            accountList.ReplaceSelectedAccount(account, updatedAccount);
            PopulateSelectedAccountSkins(updatedAccount, GetPreferredSkinIdAfterDelete(skin));
            await accountList.PersistAccountOrderAsync();
            SetAccountProfileMessage(Strings.Status_SkinDeleted);
        }
        catch (Exception ex)
        {
            var errorCodeMessage = FormatAccountProfileError(ex);
            logger.LogWarning(
                ex,
                "Microsoft account skin delete failed. AccountId={AccountId} SkinId={SkinId} ErrorCode={ErrorCode}",
                account.Id,
                skin.Id,
                errorCodeMessage);
            SetAccountProfileError(ex, Strings.Status_SkinDeleteFailed);
        }
        finally
        {
            Profile.IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanDeleteAccountSkin))]
    public async Task DeleteAccountSkinAsync(LauncherSkinRecord? skin)
    {
        if (!CanDeleteAccountSkin(skin))
            return;

        SkinLibrary.SelectedSkin = skin;
        await DeleteSelectedAccountSkinAsync();
    }

    public bool CanDeleteAccountSkin(LauncherSkinRecord? skin)
    {
        return accountList.SelectedAccount is not null
            && !accountList.SelectedAccount.IsOffline
            && !Profile.IsBusy
            && skin is not null
            && !string.Equals(accountList.SelectedAccount.ActiveSkinId, skin.Id, StringComparison.Ordinal);
    }

    [RelayCommand]
    public void RequestCancelSkinModelDialog()
    {
        skinPendingModelChange = null;
        dialogService.CancelSkinModelDialog();
    }

    [RelayCommand]
    public Task RequestConfirmSkinModelDialogAsync()
    {
        return dialogService.ConfirmSkinModelDialogAsync();
    }

    [RelayCommand]
    public async Task RefreshSelectedAccountProfileAsync()
    {
        if (accountList.SelectedAccount is not null)
            await LoadSelectedAccountProfileAsync(accountList.SelectedAccount);
    }

    [RelayCommand]
    public async Task RefreshSelectedAccountInfoAsync()
    {
        var account = accountList.SelectedAccount;
        if (account is null)
            return;

        if (account.IsOffline)
        {
            Profile.Message = Strings.Status_AccountProfileRefreshOfflineUnsupported;
            return;
        }

        var cancellationToken = Profile.BeginOperation(account.Id, Strings.Status_RefreshingAccountProfile);
        try
        {
            var refreshedAccount = await microsoftAccountService.RefreshAccountProfileAsync(account, cancellationToken);
            if (!Profile.IsCurrent(account, cancellationToken))
                return;
            var updatedAccount = AccountMapper.WithCapeCache(refreshedAccount, account.CachedCapeOptions);
            accountList.ReplaceSelectedAccount(account, updatedAccount);
            PopulateSelectedAccountSkins(updatedAccount, updatedAccount.ActiveSkinId);
            await accountList.PersistAccountOrderAsync();
            SetAccountProfileMessage(Strings.Status_AccountProfileRefreshed);
        }
        catch (OperationCanceledException) when (!Profile.IsCurrent(account, cancellationToken))
        {
        }
        catch (Exception ex)
        {
            var errorCodeMessage = FormatAccountProfileError(ex);
            logger.LogWarning(
                ex,
                "Microsoft account profile refresh failed. AccountId={AccountId} ErrorCode={ErrorCode}",
                account.Id,
                errorCodeMessage);
            SetAccountProfileError(
                ex,
                GetProfileRefreshFailedMessage(ex, Strings.Status_AccountProfileRefreshFailed),
                showFloating: true);
        }
        finally
        {
            Profile.Complete(account, cancellationToken);
        }
    }

    public async Task RefreshMicrosoftAccountsSilentlyAsync()
    {
        var accounts = accountList.Accounts
            .Where(account => !account.IsOffline)
            .Select(account => account.Account)
            .ToList();
        var hasChanges = false;

        foreach (var account in accounts)
        {
            LauncherAccount refreshedAccount;
            try
            {
                refreshedAccount = await microsoftAccountService.RefreshAccountProfileAsync(account);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "Silent Microsoft account refresh failed. AccountId={AccountId}", account.Id);
                continue;
            }

            var currentAccount = accountList.FindAccount(account.Id);
            if (currentAccount is null)
                continue;

            if (Profile.IsBusy && IsSelectedAccount(currentAccount))
                continue;

            var updatedAccount = AccountMapper.WithCapeCache(
                refreshedAccount,
                currentAccount.CachedCapeOptions);
            hasChanges |= accountList.TryReplaceAccount(currentAccount.Id, updatedAccount);
        }

        if (!hasChanges)
            return;

        try
        {
            await accountList.PersistAccountOrderAsync();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Persisting silently refreshed Microsoft accounts failed.");
        }
    }

    public async Task RefreshCurrentSecondaryContentAsync()
    {
        if (accountList.SelectedAccount is null || Profile.IsBusy)
            return;

        await RefreshSelectedAccountProfileAsync();
    }

    [RelayCommand(CanExecute = nameof(CanApplySelectedCape))]
    public async Task ApplySelectedAccountCapeAsync()
    {
        var account = accountList.SelectedAccount;
        var cape = Cape.SelectedOption;
        if (account is null || cape is null || !CanApplySelectedCape)
            return;

        if (account.IsOffline)
        {
            Profile.Message = Strings.Status_CapeOfflineUnsupported;
            return;
        }

        try
        {
            BeginAccountProfileOperation(Strings.Status_ChangingCape);
            await microsoftAccountService.SetActiveCapeAsync(account, cape.Id);
            var successMessage = cape.IsNone
                ? Strings.Status_CapeRemoved
                : string.Format(Strings.Status_CapeChangedFormat, AccountCapeTextProvider.GetDisplayName(cape));
            MarkSelectedCapeActive(cape);
            await StoreSelectedAccountCapeCacheAsync();
            SetAccountProfileMessage(successMessage, showFloating: true);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Microsoft account cape change failed. AccountId={AccountId} HasCape={HasCape}", account.Id, !cape.IsNone);
            SetAccountProfileMessage(Strings.Status_CapeChangeFailed, showFloating: true);
            Profile.ErrorCodeMessage = string.Empty;
        }
        finally
        {
            Profile.IsBusy = false;
        }
    }

    private void BeginAccountProfileOperation()
    {
        Profile.IsBusy = true;
        Profile.ErrorCodeMessage = string.Empty;
    }

    private void BeginAccountProfileOperation(string message)
    {
        BeginAccountProfileOperation();
        Profile.Message = message;
    }

    private void SetAccountProfileMessage(string message, bool showFloating = false)
    {
        Profile.Message = message;
        if (showFloating)
            floatingMessageService.Show(message);
    }

    private void SetAccountProfileError(Exception exception, string message, bool showFloating = false)
    {
        SetAccountProfileMessage(message, showFloating);
        Profile.ErrorCodeMessage = FormatAccountProfileError(exception);
    }

    private static string FormatAccountProfileError(Exception exception)
    {
        return AccountErrorCodeMessageFormatter.Format(exception);
    }

    private async Task LoadSelectedAccountProfileAsync(LauncherAccount account)
    {
        if (!IsSelectedAccount(account))
            return;

        if (account.IsOffline)
        {
            Profile.Message = Strings.Account_ProfileOfflineUnsupported;
            return;
        }

        var cancellationToken = Profile.BeginOperation(account.Id, Strings.Status_LoadingAccountProfile);
        try
        {
            var capes = await microsoftAccountService.GetCapesAsync(account, cancellationToken);
            if (!Profile.IsCurrent(account, cancellationToken) || !IsSelectedAccount(account))
                return;

            var hasAvailableCapes = capes.Any(cape => !cape.IsNone);
            PopulateSelectedAccountCapeOptions(capes);
            await StoreSelectedAccountCapeCacheAsync();
            OnPropertyChanged(nameof(CanApplySelectedCape));
            Profile.Message = hasAvailableCapes
                ? Strings.Account_ProfileLoaded
                : Strings.Account_ProfileNoCapes;
        }
        catch (OperationCanceledException) when (!Profile.IsCurrent(account, cancellationToken))
        {
        }
        catch (Exception ex)
        {
            if (IsSelectedAccount(account))
            {
                var errorCodeMessage = FormatAccountProfileError(ex);
                logger.LogWarning(
                    ex,
                    "Microsoft account profile load failed. AccountId={AccountId} ErrorCode={ErrorCode}",
                    account.Id,
                    errorCodeMessage);
                SetAccountProfileError(
                    ex,
                    GetProfileRefreshFailedMessage(ex, Strings.Status_LoadAccountProfileFailed),
                    showFloating: true);
            }
        }
        finally
        {
            Profile.Complete(account, cancellationToken);
        }
    }

    private bool IsSelectedAccount(LauncherAccount account)
    {
        var selectedAccount = accountList.SelectedAccount;
        return ReferenceEquals(selectedAccount, account)
            || (selectedAccount is not null
                && string.Equals(selectedAccount.Id, account.Id, StringComparison.Ordinal));
    }

    private static string GetProfileRefreshFailedMessage(Exception exception, string fallbackMessage)
    {
        return IsTooManyRequestsError(exception)
            ? Strings.Status_AccountProfileRefreshTooFrequent
            : fallbackMessage;
    }

    private static bool IsTooManyRequestsError(Exception exception)
    {
        var errorText = exception switch
        {
            MicrosoftAccountProfileRefreshException { ErrorCode: { Length: > 0 } code } => code,
            _ => exception.Message
        };

        if (string.IsNullOrWhiteSpace(errorText))
            return false;

        return errorText.Contains("429", StringComparison.OrdinalIgnoreCase)
            && (errorText.Contains("too many request", StringComparison.OrdinalIgnoreCase)
                || errorText.Contains("too_many_request", StringComparison.OrdinalIgnoreCase)
                || errorText.Contains("too_many_requests", StringComparison.OrdinalIgnoreCase));
    }

    private void ResetSelectedAccountProfileState(LauncherAccount? account)
    {
        Profile.SetAccount(account);
        Cape.Options.Clear();
        Cape.SelectedOption = null;
        SkinLibrary.Skins.Clear();
        SkinLibrary.SelectedSkin = null;
        Cape.NotifyCollectionChanged();
        SkinLibrary.NotifyCollectionChanged();

        if (account is null)
        {
            Profile.Message = string.Empty;
            Profile.ErrorCodeMessage = string.Empty;
            CloseSkinManagerDialog();
            NotifyAccountSelectionPropertiesChanged();
            return;
        }

        if (!account.IsOffline)
            PopulateSelectedAccountCapeOptions(account.CachedCapeOptions);

        PopulateSelectedAccountSkins(account, account.ActiveSkinId);
        Profile.ErrorCodeMessage = string.Empty;
        Profile.Message = account.IsOffline
            ? Strings.Account_ProfileOfflineUnsupported
            : account.CachedCapeOptions.Count > 0
                ? Strings.Account_ProfileCacheLoaded
                : Strings.Account_ProfileRefreshHint;
        NotifyAccountSelectionPropertiesChanged();
    }

    private void PopulateSelectedAccountCapeOptions(IEnumerable<AccountCapeOption> capes)
    {
        var normalizedCapes = NormalizeCapeOptions(capes).ToList();
        Cape.Options.Clear();
        foreach (var cape in normalizedCapes)
            Cape.Options.Add(cape);

        Cape.SelectedOption = Cape.Options.FirstOrDefault(cape => cape.IsActive)
            ?? Cape.Options.FirstOrDefault();
        Cape.NotifyCollectionChanged();
        NotifySelectedAccountCapePropertiesChanged();
    }

    private void PopulateSelectedAccountSkins(LauncherAccount account, string? preferredSkinId)
    {
        SkinLibrary.Skins.Clear();
        foreach (var skin in DistinctSkins(skinLibraryService.GetAvailableSkins(account)))
            SkinLibrary.Skins.Add(skin);

        SkinLibrary.SelectedSkin = SkinLibrary.Skins.FirstOrDefault(skin =>
                string.Equals(skin.Id, preferredSkinId, StringComparison.Ordinal))
            ?? SkinLibrary.Skins.FirstOrDefault(skin =>
                string.Equals(skin.Id, account.ActiveSkinId, StringComparison.Ordinal))
            ?? SkinLibrary.Skins.FirstOrDefault();
        SkinLibrary.NotifyCollectionChanged();
        OnPropertyChanged(nameof(HasSelectedAccountSkins));
        OnPropertyChanged(nameof(ActiveSkinId));
        NotifySelectedAccountSkinPropertiesChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSelectPreviousAccountSkin))]
    public void SelectPreviousAccountSkin()
    {
        SkinLibrary.SelectPrevious();
    }

    public bool CanSelectPreviousAccountSkin()
    {
        return PreviousAccountSkin is not null;
    }

    [RelayCommand(CanExecute = nameof(CanSelectNextAccountSkin))]
    public void SelectNextAccountSkin()
    {
        SkinLibrary.SelectNext();
    }

    public bool CanSelectNextAccountSkin()
    {
        return NextAccountSkin is not null;
    }

    private void MarkSelectedCapeActive(AccountCapeOption activeCape)
    {
        var updatedCapes = Cape.Options
            .Select(cape => new AccountCapeOption
            {
                Id = cape.Id,
                DisplayName = cape.DisplayName,
                ImageUrl = cape.ImageUrl,
                IsNone = cape.IsNone,
                IsActive = cape.IsNone == activeCape.IsNone
                    && string.Equals(cape.Id, activeCape.Id, StringComparison.OrdinalIgnoreCase)
            })
            .ToList();

        PopulateSelectedAccountCapeOptions(updatedCapes);
    }

    private async Task StoreSelectedAccountCapeCacheAsync()
    {
        var account = accountList.SelectedAccount;
        if (account is null)
            return;

        accountList.ReplaceSelectedAccount(
            account,
            AccountMapper.WithCapeCache(account, Cape.Options.ToList()));
        await accountList.PersistAccountOrderAsync();
    }

    [RelayCommand(CanExecute = nameof(CanSelectPreviousAccountCape))]
    public void SelectPreviousAccountCape()
    {
        Cape.SelectPrevious();
    }

    public bool CanSelectPreviousAccountCape()
    {
        return PreviousAccountCape is not null;
    }

    [RelayCommand(CanExecute = nameof(CanSelectNextAccountCape))]
    public void SelectNextAccountCape()
    {
        Cape.SelectNext();
    }

    public bool CanSelectNextAccountCape()
    {
        return NextAccountCape is not null;
    }

    private static IEnumerable<AccountCapeOption> NormalizeCapeOptions(IEnumerable<AccountCapeOption> capes)
    {
        var source = capes.ToList();
        var hasActiveCape = source.Any(cape => !cape.IsNone && cape.IsActive);
        var noneCape = source.FirstOrDefault(cape => cape.IsNone);

        yield return noneCape is null
            ? CreateNoneCapeOption(!hasActiveCape)
            : new AccountCapeOption
            {
                Id = null,
                DisplayName = string.Empty,
                ImageUrl = noneCape.ImageUrl,
                IsActive = !hasActiveCape && noneCape.IsActive,
                IsNone = true
            };

        foreach (var cape in source.Where(cape => !cape.IsNone))
            yield return cape;
    }

    private static AccountCapeOption CreateNoneCapeOption(bool isActive)
    {
        return new AccountCapeOption
        {
            Id = null,
            DisplayName = string.Empty,
            IsActive = isActive,
            IsNone = true
        };
    }

    private static List<LauncherSkinRecord> AddOrReplaceSkin(
        IReadOnlyList<LauncherSkinRecord> skins,
        LauncherSkinRecord skin)
    {
        var updatedSkins = skins.ToList();
        var index = updatedSkins.FindIndex(existing =>
            string.Equals(existing.Id, skin.Id, StringComparison.Ordinal)
            || (!string.IsNullOrWhiteSpace(existing.ContentHash)
                && string.Equals(existing.ContentHash, skin.ContentHash, StringComparison.OrdinalIgnoreCase)
                && existing.SkinModel == skin.SkinModel));
        if (index >= 0)
            updatedSkins[index] = skin;
        else
            updatedSkins.Add(skin);

        return updatedSkins;
    }

    private static string ResolveLocalSkinPath(string source)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.IsFile)
            return uri.LocalPath;

        return source;
    }

    private async Task ChangeSelectedAccountSkinModelAsync(
        LauncherSkinRecord skin,
        MinecraftSkinModel skinModel)
    {
        var account = accountList.SelectedAccount;
        if (account is null)
            return;

        var updatedSkin = CopySkinWithModel(skin, skinModel);
        var updatedSkins = ReplaceMatchingSkin(account.SkinLibrary, updatedSkin);
        var updatedAccount = AccountMapper.WithSkinLibrary(
            account,
            updatedSkins,
            account.ActiveSkinId,
            account.SkinSource,
            account.SkinModel);
        accountList.ReplaceSelectedAccount(account, updatedAccount);
        PopulateSelectedAccountSkins(updatedAccount, updatedSkin.Id);
        await accountList.PersistAccountOrderAsync();
        Profile.Message = Strings.Status_SkinModelChanged;
    }

    private string? GetPreferredSkinIdAfterDelete(LauncherSkinRecord deletedSkin)
    {
        var index = SkinLibrary.Skins.ToList().FindIndex(skin =>
            string.Equals(skin.Id, deletedSkin.Id, StringComparison.Ordinal));
        if (index < 0)
            return null;

        if (index + 1 < SkinLibrary.Skins.Count)
            return SkinLibrary.Skins[index + 1].Id;

        return index - 1 >= 0
            ? SkinLibrary.Skins[index - 1].Id
            : null;
    }

    private static List<LauncherSkinRecord> ReplaceMatchingSkin(
        IReadOnlyList<LauncherSkinRecord> skins,
        LauncherSkinRecord updatedSkin)
    {
        return skins
            .Select(skin => string.Equals(skin.Id, updatedSkin.Id, StringComparison.Ordinal)
                || (!string.IsNullOrWhiteSpace(skin.ContentHash)
                    && string.Equals(skin.ContentHash, updatedSkin.ContentHash, StringComparison.OrdinalIgnoreCase)
                    && skin.SkinModel == updatedSkin.SkinModel)
                    ? updatedSkin
                    : skin)
            .ToList();
    }

    private static List<LauncherSkinRecord> RemoveMatchingSkin(
        IReadOnlyList<LauncherSkinRecord> skins,
        LauncherSkinRecord skinToRemove)
    {
        return skins
            .Where(skin => !string.Equals(skin.Id, skinToRemove.Id, StringComparison.Ordinal)
                && (string.IsNullOrWhiteSpace(skin.ContentHash)
                    || !string.Equals(skin.ContentHash, skinToRemove.ContentHash, StringComparison.OrdinalIgnoreCase)
                    || skin.SkinModel != skinToRemove.SkinModel))
            .ToList();
    }

    private static LauncherSkinRecord CopySkinWithModel(
        LauncherSkinRecord skin,
        MinecraftSkinModel skinModel)
    {
        return new LauncherSkinRecord
        {
            Id = skin.Id,
            Source = skin.Source,
            SkinModel = skinModel,
            ContentHash = skin.ContentHash,
            AddedAtUtc = skin.AddedAtUtc
        };
    }

    private static bool IsSelectedSkinAlreadyApplied(LauncherAccount account, LauncherSkinRecord skin)
    {
        if (string.Equals(account.ActiveSkinId, skin.Id, StringComparison.Ordinal))
            return account.SkinModel == skin.SkinModel;

        var activeSkin = account.SkinLibrary.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, account.ActiveSkinId, StringComparison.Ordinal));
        if (activeSkin is not null && SkinsRepresentSameContent(activeSkin, skin))
            return true;

        return account.SkinModel == skin.SkinModel
            && string.Equals(account.SkinSource, skin.Source, StringComparison.Ordinal);
    }

    private static IEnumerable<LauncherSkinRecord> DistinctSkins(IEnumerable<LauncherSkinRecord> skins)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var skin in skins)
        {
            var key = string.IsNullOrWhiteSpace(skin.ContentHash)
                ? $"{skin.Source}|{skin.SkinModel}"
                : $"{skin.ContentHash}|{skin.SkinModel}";
            if (seen.Add(key))
                yield return skin;
        }
    }

    private static bool SkinsRepresentSameContent(LauncherSkinRecord left, LauncherSkinRecord right)
    {
        if (left.SkinModel != right.SkinModel)
            return false;

        if (!string.IsNullOrWhiteSpace(left.ContentHash)
            && !string.IsNullOrWhiteSpace(right.ContentHash))
        {
            return string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(left.Source, right.Source, StringComparison.Ordinal);
    }

    private void Profile_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AccountProfileViewModel.IsBusy))
            NotifySelectedAccountProfileActionPropertiesChanged();
        if (e.PropertyName == nameof(AccountProfileViewModel.ErrorCodeMessage))
            OnPropertyChanged(nameof(HasAccountProfileErrorCode));
    }

    private void SkinLibrary_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AccountSkinLibraryViewModel.SelectedSkin))
            NotifySelectedAccountSkinPropertiesChanged();
    }

    private void Cape_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AccountCapeViewModel.SelectedOption))
            NotifySelectedAccountCapePropertiesChanged();
    }

    private void AccountList_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AccountListViewModel.SelectedAccount))
            ResetSelectedAccountProfileState(accountList.SelectedAccount);
    }

    private void NotifyAccountSelectionPropertiesChanged()
    {
        OnPropertyChanged(nameof(CanChangeSelectedAccountSkin));
        OnPropertyChanged(nameof(CanManageSelectedAccountSkins));
        OnPropertyChanged(nameof(CanShowSelectedAccountAppearanceOfflineState));
        OnPropertyChanged(nameof(HasSelectedAccountSkins));
        OnPropertyChanged(nameof(CanShowSkinManagerEmptyState));
        OnPropertyChanged(nameof(ActiveSkinId));
        RequestOpenSkinManagerDialogCommand.NotifyCanExecuteChanged();
        NotifySelectedAccountSkinPropertiesChanged();
        NotifySelectedAccountProfileActionPropertiesChanged();
    }

    private void NotifySelectedAccountProfileActionPropertiesChanged()
    {
        OnPropertyChanged(nameof(CanEditSelectedMicrosoftAccount));
        OnPropertyChanged(nameof(CanApplySelectedAccountSkin));
        OnPropertyChanged(nameof(CanEditSelectedAccountSkinLibraryItem));
        OnPropertyChanged(nameof(CanDeleteSelectedAccountSkin));
        NotifySelectedAccountCapePropertiesChanged();
        ApplySelectedAccountSkinCommand.NotifyCanExecuteChanged();
        ChangeSelectedAccountSkinModelCommand.NotifyCanExecuteChanged();
        DeleteSelectedAccountSkinCommand.NotifyCanExecuteChanged();
        ChangeAccountSkinModelCommand.NotifyCanExecuteChanged();
        DeleteAccountSkinCommand.NotifyCanExecuteChanged();
    }

    private void NotifySelectedAccountCapePropertiesChanged()
    {
        OnPropertyChanged(nameof(CanApplySelectedCape));
        OnPropertyChanged(nameof(HasSelectedAccountCapes));
        OnPropertyChanged(nameof(HasSelectedAccountCapePreview));
        OnPropertyChanged(nameof(CanShowSelectedAccountCapePreviewEmptyState));
        OnPropertyChanged(nameof(PreviousAccountCape));
        OnPropertyChanged(nameof(NextAccountCape));
        OnPropertyChanged(nameof(HasPreviousAccountCape));
        OnPropertyChanged(nameof(HasNextAccountCape));
        SelectPreviousAccountCapeCommand.NotifyCanExecuteChanged();
        SelectNextAccountCapeCommand.NotifyCanExecuteChanged();
        ApplySelectedAccountCapeCommand.NotifyCanExecuteChanged();
    }

    private void NotifySelectedAccountSkinPropertiesChanged()
    {
        OnPropertyChanged(nameof(CanApplySelectedAccountSkin));
        OnPropertyChanged(nameof(CanEditSelectedAccountSkinLibraryItem));
        OnPropertyChanged(nameof(CanDeleteSelectedAccountSkin));
        OnPropertyChanged(nameof(HasSelectedAccountSkins));
        OnPropertyChanged(nameof(CanShowSkinManagerEmptyState));
        OnPropertyChanged(nameof(HasSelectedAccountSkinPreview));
        OnPropertyChanged(nameof(CanShowSelectedAccountSkinPreviewEmptyState));
        OnPropertyChanged(nameof(PreviousAccountSkin));
        OnPropertyChanged(nameof(NextAccountSkin));
        OnPropertyChanged(nameof(HasPreviousAccountSkin));
        OnPropertyChanged(nameof(HasNextAccountSkin));
        SelectPreviousAccountSkinCommand.NotifyCanExecuteChanged();
        SelectNextAccountSkinCommand.NotifyCanExecuteChanged();
        ApplySelectedAccountSkinCommand.NotifyCanExecuteChanged();
        ChangeSelectedAccountSkinModelCommand.NotifyCanExecuteChanged();
        DeleteSelectedAccountSkinCommand.NotifyCanExecuteChanged();
        ChangeAccountSkinModelCommand.NotifyCanExecuteChanged();
        DeleteAccountSkinCommand.NotifyCanExecuteChanged();
    }

    private sealed class NullFloatingMessageService : IFloatingMessageService
    {
        public static NullFloatingMessageService Instance { get; } = new();

        public event Action<string>? MessageRequested
        {
            add { }
            remove { }
        }

        public void Show(string message)
        {
        }
    }
}
