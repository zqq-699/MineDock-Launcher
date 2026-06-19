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
    private LauncherSkinRecord? skinPendingModelChange;

    [ObservableProperty]
    private AccountCapeOption? selectedAccountCapeOption;

    [ObservableProperty]
    private LauncherSkinRecord? selectedAccountSkin;

    [ObservableProperty]
    private bool isAccountProfileBusy;

    [ObservableProperty]
    private string accountProfileMessage = string.Empty;

    [ObservableProperty]
    private string accountProfileErrorCodeMessage = string.Empty;

    public AccountAppearanceViewModel(
        AccountListViewModel accountList,
        IMicrosoftAccountService microsoftAccountService,
        IAccountSkinLibraryService skinLibraryService,
        AccountSkinModelDialogViewModel skinModelDialog,
        IAccountDialogService dialogService,
        IFilePickerService filePickerService,
        IMinecraftSkinFileValidator skinFileValidator,
        ILogger<AccountAppearanceViewModel>? logger = null)
    {
        this.accountList = accountList;
        this.microsoftAccountService = microsoftAccountService;
        this.skinLibraryService = skinLibraryService;
        SkinModelDialog = skinModelDialog;
        this.dialogService = dialogService;
        this.filePickerService = filePickerService;
        this.skinFileValidator = skinFileValidator;
        this.logger = logger ?? NullLogger<AccountAppearanceViewModel>.Instance;
        this.accountList.PropertyChanged += AccountList_PropertyChanged;
    }

    public ObservableCollection<AccountCapeOption> SelectedAccountCapeOptions { get; } = [];

    public ObservableCollection<LauncherSkinRecord> SelectedAccountSkins { get; } = [];

    public AccountSkinModelDialogViewModel SkinModelDialog { get; }

    public bool CanChangeSelectedAccountSkin => accountList.SelectedAccount is not null
        && !accountList.SelectedAccount.IsOffline;

    public bool CanApplySelectedAccountSkin => accountList.SelectedAccount is not null
        && !accountList.SelectedAccount.IsOffline
        && !IsAccountProfileBusy
        && SelectedAccountSkin is not null
        && !IsSelectedSkinAlreadyApplied(accountList.SelectedAccount, SelectedAccountSkin);

    public bool CanEditSelectedAccountSkinLibraryItem => accountList.SelectedAccount is not null
        && !accountList.SelectedAccount.IsOffline
        && !IsAccountProfileBusy
        && SelectedAccountSkin is not null;

    public bool CanDeleteSelectedAccountSkin => CanEditSelectedAccountSkinLibraryItem
        && accountList.SelectedAccount is not null
        && SelectedAccountSkin is not null
        && !string.Equals(accountList.SelectedAccount.ActiveSkinId, SelectedAccountSkin.Id, StringComparison.Ordinal);

    public bool CanEditSelectedMicrosoftAccount => accountList.SelectedAccount is not null
        && !accountList.SelectedAccount.IsOffline
        && !IsAccountProfileBusy;

    public bool CanApplySelectedCape => accountList.SelectedAccount is not null
        && !accountList.SelectedAccount.IsOffline
        && SelectedAccountCapeOption is not null;

    public bool HasSelectedAccountCapes => SelectedAccountCapeOptions.Count > 0;

    public bool HasSelectedAccountSkinPreview => SelectedAccountSkin is not null;

    public bool CanShowSelectedAccountSkinPreviewEmptyState => accountList.SelectedAccount is not null
        && !HasSelectedAccountSkinPreview;

    public LauncherSkinRecord? PreviousAccountSkin => GetAdjacentSkin(-1);

    public LauncherSkinRecord? NextAccountSkin => GetAdjacentSkin(1);

    public bool HasPreviousAccountSkin => PreviousAccountSkin is not null;

    public bool HasNextAccountSkin => NextAccountSkin is not null;

    public bool HasAccountProfileErrorCode => !string.IsNullOrWhiteSpace(AccountProfileErrorCodeMessage);

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
            AccountProfileMessage = Strings.Status_SkinOfflineUnsupported;
            return;
        }

        try
        {
            IsAccountProfileBusy = true;
            AccountProfileErrorCodeMessage = string.Empty;
            AccountProfileMessage = Strings.Status_AddingSkin;
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
            AccountProfileMessage = Strings.Status_SkinAdded;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Microsoft account skin change failed. AccountId={AccountId} SkinModel={SkinModel} ErrorCode={ErrorCode}",
                account.Id,
                skinModel,
                AccountErrorCodeMessageFormatter.Format(ex));
            AccountProfileMessage = Strings.Status_SkinUpdateFailed;
            AccountProfileErrorCodeMessage = AccountErrorCodeMessageFormatter.Format(ex);
        }
        finally
        {
            IsAccountProfileBusy = false;
        }
    }

    [RelayCommand]
    public async Task ApplySelectedAccountSkinAsync()
    {
        var account = accountList.SelectedAccount;
        var skin = SelectedAccountSkin;
        if (account is null || skin is null || !CanApplySelectedAccountSkin)
            return;

        if (account.IsOffline)
        {
            AccountProfileMessage = Strings.Status_SkinOfflineUnsupported;
            return;
        }

        try
        {
            IsAccountProfileBusy = true;
            AccountProfileErrorCodeMessage = string.Empty;
            AccountProfileMessage = Strings.Status_UploadingSkin;
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
            AccountProfileMessage = Strings.Status_SkinUpdated;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Microsoft account skin apply failed. AccountId={AccountId} SkinId={SkinId} SkinModel={SkinModel} ErrorCode={ErrorCode}",
                account.Id,
                skin.Id,
                skin.SkinModel,
                AccountErrorCodeMessageFormatter.Format(ex));
            AccountProfileMessage = Strings.Status_SkinUpdateFailed;
            AccountProfileErrorCodeMessage = AccountErrorCodeMessageFormatter.Format(ex);
        }
        finally
        {
            IsAccountProfileBusy = false;
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

    [RelayCommand(CanExecute = nameof(CanEditSelectedAccountSkinLibraryItem))]
    public void ChangeSelectedAccountSkinModel()
    {
        var skin = SelectedAccountSkin;
        if (skin is null || !CanEditSelectedAccountSkinLibraryItem)
            return;

        skinPendingModelChange = skin;
        dialogService.ShowSkinModelDialog(skin.SkinModel);
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelectedAccountSkin))]
    public async Task DeleteSelectedAccountSkinAsync()
    {
        var account = accountList.SelectedAccount;
        var skin = SelectedAccountSkin;
        if (account is null || skin is null || !CanDeleteSelectedAccountSkin)
            return;

        try
        {
            IsAccountProfileBusy = true;
            AccountProfileErrorCodeMessage = string.Empty;
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
            AccountProfileMessage = Strings.Status_SkinDeleted;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Microsoft account skin delete failed. AccountId={AccountId} SkinId={SkinId} ErrorCode={ErrorCode}",
                account.Id,
                skin.Id,
                AccountErrorCodeMessageFormatter.Format(ex));
            AccountProfileMessage = Strings.Status_SkinDeleteFailed;
            AccountProfileErrorCodeMessage = AccountErrorCodeMessageFormatter.Format(ex);
        }
        finally
        {
            IsAccountProfileBusy = false;
        }
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
            AccountProfileMessage = Strings.Status_AccountProfileRefreshOfflineUnsupported;
            return;
        }

        try
        {
            IsAccountProfileBusy = true;
            AccountProfileErrorCodeMessage = string.Empty;
            AccountProfileMessage = Strings.Status_RefreshingAccountProfile;
            var refreshedAccount = await microsoftAccountService.RefreshAccountProfileAsync(account);
            var updatedAccount = AccountMapper.WithCapeCache(refreshedAccount, account.CachedCapeOptions);
            accountList.ReplaceSelectedAccount(account, updatedAccount);
            PopulateSelectedAccountSkins(updatedAccount, updatedAccount.ActiveSkinId);
            await accountList.PersistAccountOrderAsync();
            AccountProfileMessage = Strings.Status_AccountProfileRefreshed;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Microsoft account profile refresh failed. AccountId={AccountId} ErrorCode={ErrorCode}",
                account.Id,
                AccountErrorCodeMessageFormatter.Format(ex));
            AccountProfileMessage = Strings.Status_AccountProfileRefreshFailed;
            AccountProfileErrorCodeMessage = AccountErrorCodeMessageFormatter.Format(ex);
        }
        finally
        {
            IsAccountProfileBusy = false;
        }
    }

    public async Task RefreshMicrosoftAccountsSilentlyAsync()
    {
        var accounts = accountList.Accounts
            .Where(account => !account.IsOffline)
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

            if (IsAccountProfileBusy && IsSelectedAccount(currentAccount))
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
        if (accountList.SelectedAccount is null || IsAccountProfileBusy)
            return;

        await RefreshSelectedAccountProfileAsync();
    }

    [RelayCommand]
    public async Task ApplySelectedAccountCapeAsync()
    {
        var account = accountList.SelectedAccount;
        var cape = SelectedAccountCapeOption;
        if (account is null || cape is null)
            return;

        if (account.IsOffline)
        {
            AccountProfileMessage = Strings.Status_CapeOfflineUnsupported;
            return;
        }

        try
        {
            IsAccountProfileBusy = true;
            AccountProfileErrorCodeMessage = string.Empty;
            AccountProfileMessage = Strings.Status_ChangingCape;
            await microsoftAccountService.SetActiveCapeAsync(account, cape.Id);
            AccountProfileMessage = cape.IsNone
                ? Strings.Status_CapeRemoved
                : string.Format(Strings.Status_CapeChangedFormat, AccountCapeTextProvider.GetDisplayName(cape));
            MarkSelectedCapeActive(cape);
            await StoreSelectedAccountCapeCacheAsync();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Microsoft account cape change failed. AccountId={AccountId} HasCape={HasCape}", account.Id, !cape.IsNone);
            AccountProfileMessage = Strings.Status_CapeChangeFailed;
            AccountProfileErrorCodeMessage = string.Empty;
        }
        finally
        {
            IsAccountProfileBusy = false;
        }
    }

    partial void OnSelectedAccountCapeOptionChanged(AccountCapeOption? value)
    {
        OnPropertyChanged(nameof(CanApplySelectedCape));
    }

    partial void OnSelectedAccountSkinChanged(LauncherSkinRecord? value)
    {
        NotifySelectedAccountSkinPropertiesChanged();
    }

    partial void OnIsAccountProfileBusyChanged(bool value)
    {
        NotifySelectedAccountProfileActionPropertiesChanged();
    }

    partial void OnAccountProfileErrorCodeMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasAccountProfileErrorCode));
    }

    private async Task LoadSelectedAccountProfileAsync(LauncherAccount account)
    {
        SelectedAccountCapeOptions.Clear();
        SelectedAccountCapeOption = null;
        OnPropertyChanged(nameof(HasSelectedAccountCapes));
        OnPropertyChanged(nameof(CanApplySelectedCape));

        if (!IsSelectedAccount(account))
            return;

        if (account.IsOffline)
        {
            AccountProfileMessage = Strings.Account_ProfileOfflineUnsupported;
            return;
        }

        try
        {
            IsAccountProfileBusy = true;
            AccountProfileErrorCodeMessage = string.Empty;
            AccountProfileMessage = Strings.Status_LoadingAccountProfile;
            var capes = await microsoftAccountService.GetCapesAsync(account);
            if (!IsSelectedAccount(account))
                return;

            PopulateSelectedAccountCapeOptions(capes);
            await StoreSelectedAccountCapeCacheAsync();
            OnPropertyChanged(nameof(CanApplySelectedCape));
            AccountProfileMessage = SelectedAccountCapeOptions.Count == 0
                ? Strings.Account_ProfileNoCapes
                : Strings.Account_ProfileLoaded;
        }
        catch (Exception ex)
        {
            if (IsSelectedAccount(account))
            {
                logger.LogWarning(
                    ex,
                    "Microsoft account profile load failed. AccountId={AccountId} ErrorCode={ErrorCode}",
                    account.Id,
                    AccountErrorCodeMessageFormatter.Format(ex));
                AccountProfileMessage = Strings.Status_LoadAccountProfileFailed;
                AccountProfileErrorCodeMessage = AccountErrorCodeMessageFormatter.Format(ex);
            }
        }
        finally
        {
            if (IsSelectedAccount(account))
                IsAccountProfileBusy = false;
        }
    }

    private bool IsSelectedAccount(LauncherAccount account)
    {
        var selectedAccount = accountList.SelectedAccount;
        return ReferenceEquals(selectedAccount, account)
            || (selectedAccount is not null
                && string.Equals(selectedAccount.Id, account.Id, StringComparison.Ordinal));
    }

    private void ResetSelectedAccountProfileState(LauncherAccount? account)
    {
        SelectedAccountCapeOptions.Clear();
        SelectedAccountCapeOption = null;
        SelectedAccountSkins.Clear();
        SelectedAccountSkin = null;

        if (account is null)
        {
            AccountProfileMessage = string.Empty;
            AccountProfileErrorCodeMessage = string.Empty;
            NotifyAccountSelectionPropertiesChanged();
            return;
        }

        PopulateSelectedAccountCapeOptions(account.CachedCapeOptions);
        PopulateSelectedAccountSkins(account, account.ActiveSkinId);
        AccountProfileErrorCodeMessage = string.Empty;
        AccountProfileMessage = account.IsOffline
            ? Strings.Account_ProfileOfflineUnsupported
            : SelectedAccountCapeOptions.Count > 0
                ? Strings.Account_ProfileCacheLoaded
                : Strings.Account_ProfileRefreshHint;
        NotifyAccountSelectionPropertiesChanged();
    }

    private void PopulateSelectedAccountCapeOptions(IEnumerable<AccountCapeOption> capes)
    {
        SelectedAccountCapeOptions.Clear();
        foreach (var cape in capes)
            SelectedAccountCapeOptions.Add(cape);

        SelectedAccountCapeOption = SelectedAccountCapeOptions.FirstOrDefault(cape => cape.IsActive)
            ?? SelectedAccountCapeOptions.FirstOrDefault();
        OnPropertyChanged(nameof(HasSelectedAccountCapes));
        OnPropertyChanged(nameof(CanApplySelectedCape));
    }

    private void PopulateSelectedAccountSkins(LauncherAccount account, string? preferredSkinId)
    {
        SelectedAccountSkins.Clear();
        foreach (var skin in DistinctSkins(skinLibraryService.GetAvailableSkins(account)))
            SelectedAccountSkins.Add(skin);

        SelectedAccountSkin = SelectedAccountSkins.FirstOrDefault(skin =>
                string.Equals(skin.Id, preferredSkinId, StringComparison.Ordinal))
            ?? SelectedAccountSkins.FirstOrDefault(skin =>
                string.Equals(skin.Id, account.ActiveSkinId, StringComparison.Ordinal))
            ?? SelectedAccountSkins.FirstOrDefault();
        NotifySelectedAccountSkinPropertiesChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSelectPreviousAccountSkin))]
    public void SelectPreviousAccountSkin()
    {
        if (PreviousAccountSkin is not null)
            SelectedAccountSkin = PreviousAccountSkin;
    }

    public bool CanSelectPreviousAccountSkin()
    {
        return PreviousAccountSkin is not null;
    }

    [RelayCommand(CanExecute = nameof(CanSelectNextAccountSkin))]
    public void SelectNextAccountSkin()
    {
        if (NextAccountSkin is not null)
            SelectedAccountSkin = NextAccountSkin;
    }

    public bool CanSelectNextAccountSkin()
    {
        return NextAccountSkin is not null;
    }

    private void MarkSelectedCapeActive(AccountCapeOption activeCape)
    {
        var updatedCapes = SelectedAccountCapeOptions
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
            AccountMapper.WithCapeCache(account, SelectedAccountCapeOptions.ToList()));
        await accountList.PersistAccountOrderAsync();
    }

    private LauncherSkinRecord? GetAdjacentSkin(int offset)
    {
        if (SelectedAccountSkins.Count < 2 || SelectedAccountSkin is null)
            return null;

        var index = SelectedAccountSkins.IndexOf(SelectedAccountSkin);
        if (index < 0)
            return null;

        var adjacentIndex = index + offset;
        if (adjacentIndex < 0 || adjacentIndex >= SelectedAccountSkins.Count)
            return null;

        return SelectedAccountSkins[adjacentIndex];
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
        AccountProfileMessage = Strings.Status_SkinModelChanged;
    }

    private string? GetPreferredSkinIdAfterDelete(LauncherSkinRecord deletedSkin)
    {
        var index = SelectedAccountSkins.ToList().FindIndex(skin =>
            string.Equals(skin.Id, deletedSkin.Id, StringComparison.Ordinal));
        if (index < 0)
            return null;

        if (index + 1 < SelectedAccountSkins.Count)
            return SelectedAccountSkins[index + 1].Id;

        return index - 1 >= 0
            ? SelectedAccountSkins[index - 1].Id
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

    private void AccountList_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AccountListViewModel.SelectedAccount))
            ResetSelectedAccountProfileState(accountList.SelectedAccount);
    }

    private void NotifyAccountSelectionPropertiesChanged()
    {
        OnPropertyChanged(nameof(CanChangeSelectedAccountSkin));
        NotifySelectedAccountSkinPropertiesChanged();
        NotifySelectedAccountProfileActionPropertiesChanged();
    }

    private void NotifySelectedAccountProfileActionPropertiesChanged()
    {
        OnPropertyChanged(nameof(CanEditSelectedMicrosoftAccount));
        OnPropertyChanged(nameof(CanApplySelectedCape));
        OnPropertyChanged(nameof(CanApplySelectedAccountSkin));
        OnPropertyChanged(nameof(CanEditSelectedAccountSkinLibraryItem));
        OnPropertyChanged(nameof(CanDeleteSelectedAccountSkin));
        ChangeSelectedAccountSkinModelCommand.NotifyCanExecuteChanged();
        DeleteSelectedAccountSkinCommand.NotifyCanExecuteChanged();
    }

    private void NotifySelectedAccountSkinPropertiesChanged()
    {
        OnPropertyChanged(nameof(CanApplySelectedAccountSkin));
        OnPropertyChanged(nameof(CanEditSelectedAccountSkinLibraryItem));
        OnPropertyChanged(nameof(CanDeleteSelectedAccountSkin));
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
    }

}

