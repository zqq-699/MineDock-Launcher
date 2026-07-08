using System.Windows;
using Launcher.App.Controls;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.ViewModels.Account;
using Launcher.Application.Accounts;
using Launcher.Domain.Models;

namespace Launcher.Tests.Accounts;

public sealed class AccountPageViewModelTests
{
    private const string AccountNameValidationMessage = "\u7528\u6237\u540d\u9700\u4e3a 3-16 \u4f4d\u5b57\u6bcd\u3001\u6570\u5b57\u6216\u4e0b\u5212\u7ebf";

    [Theory]
    [InlineData("ab")]
    [InlineData("abcdefghijklmnopq")]
    [InlineData("bad-name")]
    [InlineData("bad name")]
    public async Task ConfirmAddOfflineAccountRejectsInvalidNames(string invalidName)
    {
        var accountStore = new FakeAccountStore();
        var statusService = new FakeStatusService();
        var viewModel = CreateViewModel(accountStore, statusService);

        viewModel.Dialog.OpenAddAccountDialog();
        viewModel.Dialog.SelectedAccountTypeOption = viewModel.Dialog.AccountTypeOptions[0];
        await viewModel.Dialog.ConfirmAddAccountDialogAsync();
        viewModel.Dialog.NewOfflineAccountName = invalidName;

        await viewModel.Dialog.ConfirmAddAccountDialogAsync();

        Assert.True(viewModel.Dialog.IsNewOfflineAccountNameInvalid);
        Assert.Empty(viewModel.AccountList.Accounts);
        Assert.Equal(0, accountStore.SaveCount);
        Assert.Equal(AccountNameValidationMessage, statusService.LastMessage);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("abcdefghijklmnopq")]
    [InlineData("bad-name")]
    [InlineData("bad name")]
    public async Task ConfirmRenameAccountRejectsInvalidNames(string invalidName)
    {
        var accountStore = new FakeAccountStore();
        var statusService = new FakeStatusService();
        var viewModel = CreateViewModel(accountStore, statusService);
        var account = new LauncherAccount
        {
            Id = "offline-1",
            DisplayName = "Valid_Name",
            IsOffline = true
        };
        viewModel.AccountList.Accounts.Add(account);
        viewModel.SelectAccount(account);
        viewModel.Dialog.OpenRenameAccountDialog();
        viewModel.Dialog.RenameAccountName = invalidName;
        var saveCountBeforeRename = accountStore.SaveCount;

        await viewModel.Dialog.ConfirmRenameAccountDialogAsync();

        Assert.True(viewModel.Dialog.IsRenameAccountNameInvalid);
        Assert.Equal("Valid_Name", viewModel.SelectedAccount?.DisplayName);
        Assert.Equal(saveCountBeforeRename, accountStore.SaveCount);
        Assert.Equal(AccountNameValidationMessage, statusService.LastMessage);
    }

    [Fact]
    public async Task ConfirmDeleteAccountClosesDialogBeforeSaveCompletes()
    {
        var saveCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var accountStore = new FakeAccountStore(saveCompletion);
        var statusService = new FakeStatusService();
        var viewModel = CreateViewModel(accountStore, statusService);
        var account = new LauncherAccount
        {
            Id = "offline-1",
            DisplayName = "DeleteMe",
            IsOffline = true
        };
        viewModel.AccountList.Accounts.Add(account);
        viewModel.Dialog.OpenDeleteAccountDialog(account);

        var deleteTask = viewModel.Dialog.ConfirmDeleteAccountDialogAsync();

        Assert.False(viewModel.Dialog.IsDeleteAccountDialogOpen);
        Assert.Null(viewModel.Dialog.AccountPendingDelete);
        Assert.Empty(viewModel.AccountList.Accounts);

        saveCompletion.SetResult();
        await deleteTask;
    }

    [Fact]
    public async Task ConfirmAddOfflineAccountCreatesStandardUuid()
    {
        var viewModel = CreateViewModel(new FakeAccountStore(), new FakeStatusService());

        viewModel.Dialog.OpenAddAccountDialog();
        viewModel.Dialog.SelectedAccountTypeOption = viewModel.Dialog.AccountTypeOptions[0];
        await viewModel.Dialog.ConfirmAddAccountDialogAsync();
        viewModel.Dialog.NewOfflineAccountName = "LocalUser";

        await viewModel.Dialog.ConfirmAddAccountDialogAsync();

        var account = Assert.Single(viewModel.AccountList.Accounts);
        Assert.Equal("Standard-LocalUser", account.Uuid);
        Assert.Equal(OfflineUuidGenerationMode.Standard, account.OfflineUuidGenerationMode);
        Assert.Equal("Standard-LocalUser", viewModel.OfflineUuid.SelectedAccountUuidText);
    }

    [Fact]
    public async Task CompleteMicrosoftAccountLoginPersistsAvatarSource()
    {
        var accountStore = new FakeAccountStore();
        var microsoftAccountService = new FakeMicrosoftAccountService
        {
            LoginHandler = () => new LauncherAccount
            {
                Id = "microsoft-00000000000000000000000000000001",
                DisplayName = "PlayerOne",
                Uuid = "00000000000000000000000000000001",
                AvatarSource = "cached-avatar.png",
                IsOffline = false
            }
        };
        var viewModel = CreateViewModel(
            accountStore,
            new FakeStatusService(),
            microsoftAccountService);

        await viewModel.Dialog.CompleteMicrosoftAccountLoginAsync();

        var account = Assert.Single(viewModel.AccountList.Accounts);
        Assert.Equal("cached-avatar.png", account.AvatarSource);
        var savedAccount = Assert.Single(accountStore.LastSavedAccounts);
        Assert.Equal("cached-avatar.png", savedAccount.AvatarSource);
    }

    [Fact]
    public async Task RenameMicrosoftAccountShowsFailureReason()
    {
        var statusService = new FakeStatusService();
        var microsoftAccountService = new FakeMicrosoftAccountService
        {
            ChangeNameHandler = (_, _) => throw new MicrosoftAccountNameChangeException(
                MicrosoftAccountNameChangeFailureReason.NotAllowed,
                "HTTP 403 / NOT_ALLOWED")
        };
        var viewModel = CreateViewModel(
            new FakeAccountStore(),
            statusService,
            microsoftAccountService);
        var account = new LauncherAccount
        {
            Id = "microsoft-00000000000000000000000000000001",
            DisplayName = "OldName",
            Uuid = "00000000000000000000000000000001",
            IsOffline = false
        };
        viewModel.AccountList.Accounts.Add(account);
        viewModel.SelectAccount(account);
        viewModel.Dialog.OpenRenameAccountDialog();
        viewModel.Dialog.RenameAccountName = "NewName";

        await viewModel.Dialog.ConfirmRenameAccountDialogAsync();

        Assert.Equal("OldName", viewModel.SelectedAccount?.DisplayName);
        Assert.Equal(Strings.Status_AccountRenameFailedNotAllowed, viewModel.Dialog.RenameAccountMessage);
        Assert.Equal("返回代码：HTTP 403 / NOT_ALLOWED", viewModel.Dialog.RenameAccountErrorCodeMessage);
        Assert.Equal(Strings.Status_AccountRenameFailedNotAllowed, statusService.LastMessage);
    }

    [Fact]
    public async Task ApplyManualUuidRejectsInvalidUuid()
    {
        var statusService = new FakeStatusService();
        var viewModel = CreateViewModel(new FakeAccountStore(), statusService);
        var account = new LauncherAccount
        {
            Id = "offline-1",
            DisplayName = "LocalUser",
            Uuid = "Standard-LocalUser",
            OfflineUuidGenerationMode = OfflineUuidGenerationMode.Standard,
            IsOffline = true
        };
        viewModel.AccountList.Accounts.Add(account);
        viewModel.SelectAccount(account);
        viewModel.OfflineUuid.SelectedOfflineUuidOption = viewModel.OfflineUuid.OfflineUuidOptions
            .First(option => option.Mode == OfflineUuidGenerationMode.Manual);
        viewModel.OfflineUuid.ManualUuidText = "bad-uuid";

        await viewModel.OfflineUuid.ApplyManualUuidCommand.ExecuteAsync(null);

        Assert.True(viewModel.OfflineUuid.IsManualUuidInvalid);
        Assert.Equal("Standard-LocalUser", viewModel.SelectedAccount?.Uuid);
        Assert.Equal(OfflineUuidGenerationMode.Standard, viewModel.SelectedAccount?.OfflineUuidGenerationMode);
        Assert.Equal("UUID 格式不正确，请检查后重试。", statusService.LastMessage);
    }

    [Fact]
    public async Task ApplyManualUuidPersistsNormalizedUuid()
    {
        var accountStore = new FakeAccountStore();
        var viewModel = CreateViewModel(accountStore, new FakeStatusService());
        var account = new LauncherAccount
        {
            Id = "offline-1",
            DisplayName = "LocalUser",
            Uuid = "Standard-LocalUser",
            OfflineUuidGenerationMode = OfflineUuidGenerationMode.Standard,
            IsOffline = true
        };
        viewModel.AccountList.Accounts.Add(account);
        viewModel.SelectAccount(account);
        var saveCountBeforeApply = accountStore.SaveCount;
        viewModel.OfflineUuid.SelectedOfflineUuidOption = viewModel.OfflineUuid.OfflineUuidOptions
            .First(option => option.Mode == OfflineUuidGenerationMode.Manual);
        viewModel.OfflineUuid.ManualUuidText = "00000000000000000000000000000005";

        await viewModel.OfflineUuid.ApplyManualUuidCommand.ExecuteAsync(null);

        Assert.False(viewModel.OfflineUuid.IsManualUuidInvalid);
        Assert.Equal("00000000-0000-0000-0000-000000000005", viewModel.SelectedAccount?.Uuid);
        Assert.Equal(OfflineUuidGenerationMode.Manual, viewModel.SelectedAccount?.OfflineUuidGenerationMode);
        Assert.Equal("00000000-0000-0000-0000-000000000005", viewModel.OfflineUuid.SelectedAccountUuidText);
        Assert.Equal(saveCountBeforeApply + 1, accountStore.SaveCount);
    }

    [Fact]
    public async Task PickInvalidSkinFileOpensFormatErrorDialog()
    {
        var dialogService = new FakeAccountDialogService();
        var filePickerService = new FakeFilePickerService("bad-skin.png");
        var viewModel = CreateViewModel(
            new FakeAccountStore(),
            new FakeStatusService(),
            dialogService: dialogService,
            filePickerService: filePickerService,
            skinFileValidator: new FakeSkinFileValidator(false));
        var account = new LauncherAccount
        {
            Id = "microsoft-00000000000000000000000000000001",
            DisplayName = "Player",
            Uuid = "00000000000000000000000000000001",
            IsOffline = false
        };
        viewModel.AccountList.Accounts.Add(account);
        viewModel.SelectAccount(account);

        await viewModel.Appearance.PickAndChangeSelectedAccountSkinCommand.ExecuteAsync(null);

        Assert.True(dialogService.WasSkinFormatErrorShown);
        Assert.Null(dialogService.LastSkinModelFilePath);
    }

    [Fact]
    public async Task ApplySelectedSkinUploadsAndMarksActive()
    {
        var accountStore = new FakeAccountStore();
        var microsoftAccountService = new FakeMicrosoftAccountService
        {
            UploadSkinHandler = (account, _, skinModel) => new LauncherAccount
            {
                Id = account.Id,
                DisplayName = account.DisplayName,
                Uuid = account.Uuid,
                AvatarSource = account.AvatarSource,
                SkinSource = "uploaded-skin.png",
                SkinModel = skinModel,
                IsOffline = false,
                HasFreshProfile = true
            }
        };
        var floatingMessageService = new FakeFloatingMessageService();
        var viewModel = CreateViewModel(
            accountStore,
            new FakeStatusService(),
            microsoftAccountService,
            floatingMessageService: floatingMessageService);
        var skin = new LauncherSkinRecord
        {
            Id = "skin-classic",
            Source = "C:\\tmp\\skin.png",
            SkinModel = MinecraftSkinModel.Classic,
            ContentHash = "hash-classic",
            AddedAtUtc = DateTimeOffset.UnixEpoch
        };
        var account = new LauncherAccount
        {
            Id = "microsoft-00000000000000000000000000000001",
            DisplayName = "Player",
            Uuid = "00000000000000000000000000000001",
            IsOffline = false,
            SkinLibrary = [skin]
        };
        viewModel.AccountList.Accounts.Add(account);
        viewModel.SelectAccount(account);
        viewModel.Appearance.SelectedAccountSkin = skin;

        await viewModel.Appearance.ApplySelectedAccountSkinCommand.ExecuteAsync(null);

        Assert.Equal(1, microsoftAccountService.UploadSkinCount);
        Assert.Equal("C:\\tmp\\skin.png", microsoftAccountService.LastSkinFilePath);
        Assert.Equal("skin-classic", viewModel.SelectedAccount?.ActiveSkinId);
        Assert.Equal("C:\\tmp\\skin.png", viewModel.SelectedAccount?.SkinSource);
        Assert.Equal(MinecraftSkinModel.Classic, viewModel.SelectedAccount?.SkinModel);
        var savedAccount = Assert.Single(accountStore.LastSavedAccounts);
        Assert.Equal("skin-classic", savedAccount.ActiveSkinId);
        Assert.Equal(Strings.Status_SkinUpdated, floatingMessageService.LastMessage);
    }

    [Fact]
    public async Task SkinManagerDeleteCommandUsesTileSkin()
    {
        var accountStore = new FakeAccountStore();
        var skinLibraryService = new FakeAccountSkinLibraryService();
        var viewModel = CreateViewModel(
            accountStore,
            new FakeStatusService(),
            skinLibraryService: skinLibraryService);
        var activeSkin = CreateSkinRecord("skin-active", "hash-active", MinecraftSkinModel.Classic);
        var nextSkin = CreateSkinRecord("skin-next", "hash-next", MinecraftSkinModel.Slim);
        var account = new LauncherAccount
        {
            Id = "microsoft-00000000000000000000000000000001",
            DisplayName = "Player",
            Uuid = "00000000000000000000000000000001",
            ActiveSkinId = activeSkin.Id,
            SkinLibrary = [activeSkin, nextSkin],
            IsOffline = false
        };
        viewModel.AccountList.Accounts.Add(account);
        viewModel.SelectAccount(account);

        Assert.False(viewModel.Appearance.DeleteAccountSkinCommand.CanExecute(activeSkin));
        Assert.True(viewModel.Appearance.DeleteAccountSkinCommand.CanExecute(nextSkin));
        await viewModel.Appearance.DeleteAccountSkinCommand.ExecuteAsync(nextSkin);

        Assert.Equal(1, skinLibraryService.DeleteSkinCount);
        Assert.Same(nextSkin, skinLibraryService.LastDeletedSkin);
        var savedAccount = Assert.Single(accountStore.LastSavedAccounts);
        var savedSkin = Assert.Single(savedAccount.SkinLibrary);
        Assert.Equal(activeSkin.Id, savedSkin.Id);
    }

    [Fact]
    public async Task DeleteSelectedSkinRemovesOnlyNonActiveSkin()
    {
        var accountStore = new FakeAccountStore();
        var skinLibraryService = new FakeAccountSkinLibraryService();
        var viewModel = CreateViewModel(
            accountStore,
            new FakeStatusService(),
            skinLibraryService: skinLibraryService);
        var activeSkin = CreateSkinRecord("skin-active", "hash-active", MinecraftSkinModel.Classic);
        var nextSkin = CreateSkinRecord("skin-next", "hash-next", MinecraftSkinModel.Slim);
        var account = new LauncherAccount
        {
            Id = "microsoft-00000000000000000000000000000001",
            DisplayName = "Player",
            Uuid = "00000000000000000000000000000001",
            SkinSource = activeSkin.Source,
            SkinModel = activeSkin.SkinModel,
            ActiveSkinId = activeSkin.Id,
            SkinLibrary = [activeSkin, nextSkin],
            IsOffline = false
        };
        viewModel.AccountList.Accounts.Add(account);
        viewModel.SelectAccount(account);
        viewModel.Appearance.SelectedAccountSkin = nextSkin;

        Assert.True(viewModel.Appearance.DeleteSelectedAccountSkinCommand.CanExecute(null));
        await viewModel.Appearance.DeleteSelectedAccountSkinCommand.ExecuteAsync(null);

        Assert.Equal(1, skinLibraryService.DeleteSkinCount);
        Assert.Same(nextSkin, skinLibraryService.LastDeletedSkin);
        var savedAccount = Assert.Single(accountStore.LastSavedAccounts);
        var savedSkin = Assert.Single(savedAccount.SkinLibrary);
        Assert.Equal(activeSkin.Id, savedSkin.Id);
        Assert.Equal(activeSkin.Id, savedAccount.ActiveSkinId);
        Assert.Equal(activeSkin.Id, viewModel.Appearance.SelectedAccountSkin?.Id);
        Assert.False(viewModel.Appearance.DeleteSelectedAccountSkinCommand.CanExecute(null));
    }

    [Fact]
    public async Task ChangeSkinFailureShowsReturnedCode()
    {
        var microsoftAccountService = new FakeMicrosoftAccountService
        {
            UploadSkinHandler = (_, _, _) => throw new MicrosoftAccountSkinUpdateException("HTTP 400 / INVALID_SKIN")
        };
        var floatingMessageService = new FakeFloatingMessageService();
        var viewModel = CreateViewModel(
            new FakeAccountStore(),
            new FakeStatusService(),
            microsoftAccountService,
            floatingMessageService: floatingMessageService);
        var account = new LauncherAccount
        {
            Id = "microsoft-00000000000000000000000000000001",
            DisplayName = "Player",
            Uuid = "00000000000000000000000000000001",
            IsOffline = false
        };
        viewModel.AccountList.Accounts.Add(account);
        viewModel.SelectAccount(account);

        var skin = new LauncherSkinRecord
        {
            Id = "skin-classic",
            Source = "skin.png",
            SkinModel = MinecraftSkinModel.Classic,
            ContentHash = "hash-classic",
            AddedAtUtc = DateTimeOffset.UtcNow
        };
        var accountWithSkin = AccountMapper.WithSkinLibrary(account, [skin], null, null, null);
        viewModel.AccountList.ReplaceSelectedAccount(account, accountWithSkin);
        viewModel.SelectAccount(accountWithSkin);
        viewModel.Appearance.SelectedAccountSkin = skin;

        await viewModel.Appearance.ApplySelectedAccountSkinCommand.ExecuteAsync(null);

        Assert.Equal(Strings.Status_SkinUpdateFailed, viewModel.Appearance.AccountProfileMessage);
        Assert.Equal(Strings.Status_SkinUpdateFailed, floatingMessageService.LastMessage);
        Assert.Equal("返回代码：HTTP 400 / INVALID_SKIN", viewModel.Appearance.AccountProfileErrorCodeMessage);
        Assert.True(viewModel.Appearance.HasAccountProfileErrorCode);
    }

    [Fact]
    public async Task RefreshSelectedAccountInfoUpdatesProfileAndPersists()
    {
        var accountStore = new FakeAccountStore();
        var cachedCape = new AccountCapeOption
        {
            Id = "cape-1",
            DisplayName = "Cape",
            IsActive = true
        };
        var microsoftAccountService = new FakeMicrosoftAccountService
        {
            RefreshProfileHandler = account => new LauncherAccount
            {
                Id = account.Id,
                DisplayName = "NewName",
                Uuid = account.Uuid,
                AvatarSource = "new-avatar.png",
                SkinSource = "new-skin.png",
                SkinModel = MinecraftSkinModel.Classic,
                IsOffline = false,
                HasFreshProfile = true
            }
        };
        var viewModel = CreateViewModel(
            accountStore,
            new FakeStatusService(),
            microsoftAccountService);
        var account = new LauncherAccount
        {
            Id = "microsoft-00000000000000000000000000000001",
            DisplayName = "OldName",
            Uuid = "00000000000000000000000000000001",
            AvatarSource = "old-avatar.png",
            IsOffline = false,
            CachedCapeOptions = [cachedCape]
        };
        viewModel.AccountList.Accounts.Add(account);
        viewModel.SelectAccount(account);

        await viewModel.Appearance.RefreshSelectedAccountInfoCommand.ExecuteAsync(null);

        Assert.Equal("NewName", viewModel.SelectedAccount?.DisplayName);
        Assert.Equal("new-avatar.png", viewModel.SelectedAccount?.AvatarSource);
        Assert.Equal("new-skin.png", viewModel.SelectedAccount?.SkinSource);
        Assert.Equal(MinecraftSkinModel.Classic, viewModel.SelectedAccount?.SkinModel);
        Assert.Same(cachedCape, Assert.Single(viewModel.SelectedAccount!.CachedCapeOptions));
        Assert.Equal(Strings.Status_AccountProfileRefreshed, viewModel.Appearance.AccountProfileMessage);
        Assert.Equal(1, microsoftAccountService.RefreshProfileCount);
        var savedAccount = Assert.Single(accountStore.LastSavedAccounts);
        Assert.Equal("NewName", savedAccount.DisplayName);
        Assert.Equal("new-avatar.png", savedAccount.AvatarSource);
        Assert.Equal("new-skin.png", savedAccount.SkinSource);
        Assert.Equal(MinecraftSkinModel.Classic, savedAccount.SkinModel);
        Assert.True(viewModel.Appearance.CanEditSelectedMicrosoftAccount);
        Assert.True(viewModel.Appearance.RefreshSelectedAccountInfoCommand.CanExecute(null));
    }

    [Fact]
    public async Task RefreshSelectedAccountInfoFailureShowsReturnedCode()
    {
        var microsoftAccountService = new FakeMicrosoftAccountService
        {
            RefreshProfileHandler = _ => throw new MicrosoftAccountProfileRefreshException("HTTP 401 / UNAUTHORIZED")
        };
        var viewModel = CreateViewModel(
            new FakeAccountStore(),
            new FakeStatusService(),
            microsoftAccountService);
        var account = new LauncherAccount
        {
            Id = "microsoft-00000000000000000000000000000001",
            DisplayName = "Player",
            Uuid = "00000000000000000000000000000001",
            IsOffline = false
        };
        viewModel.AccountList.Accounts.Add(account);
        viewModel.SelectAccount(account);

        await viewModel.Appearance.RefreshSelectedAccountInfoCommand.ExecuteAsync(null);

        Assert.Equal(Strings.Status_AccountProfileRefreshFailed, viewModel.Appearance.AccountProfileMessage);
        Assert.Equal("返回代码：HTTP 401 / UNAUTHORIZED", viewModel.Appearance.AccountProfileErrorCodeMessage);
        Assert.True(viewModel.Appearance.HasAccountProfileErrorCode);
    }

    [Fact]
    public async Task ApplyCapeFailureShowsFloatingMessage()
    {
        var floatingMessageService = new FakeFloatingMessageService();
        var noneCape = new AccountCapeOption
        {
            DisplayName = string.Empty,
            IsNone = true
        };
        var activeCape = new AccountCapeOption
        {
            Id = "cape-1",
            DisplayName = "Cape",
            ImageUrl = "cape.png",
            IsActive = true
        };
        var microsoftAccountService = new FakeMicrosoftAccountService
        {
            SetActiveCapeHandler = (_, _) => throw new MicrosoftAccountProfileRefreshException("HTTP 500")
        };
        var viewModel = CreateViewModel(
            new FakeAccountStore(),
            new FakeStatusService(),
            microsoftAccountService,
            floatingMessageService: floatingMessageService);
        var account = new LauncherAccount
        {
            Id = "microsoft-00000000000000000000000000000001",
            DisplayName = "Player",
            Uuid = "00000000000000000000000000000001",
            IsOffline = false,
            CachedCapeOptions = [noneCape, activeCape]
        };
        viewModel.AccountList.Accounts.Add(account);
        viewModel.SelectAccount(account);
        viewModel.Appearance.SelectPreviousAccountCapeCommand.Execute(null);

        await viewModel.Appearance.ApplySelectedAccountCapeCommand.ExecuteAsync(null);

        Assert.Equal(Strings.Status_CapeChangeFailed, viewModel.Appearance.AccountProfileMessage);
        Assert.Equal(Strings.Status_CapeChangeFailed, floatingMessageService.LastMessage);
    }

    [Fact]
    public async Task RefreshSelectedAccountProfileFailureShowsReturnedCode()
    {
        var floatingMessageService = new FakeFloatingMessageService();
        var cachedCape = new AccountCapeOption
        {
            Id = "cached-cape",
            DisplayName = "Cached Cape",
            ImageUrl = "file:///cached-cape.png",
            IsActive = true
        };
        var microsoftAccountService = new FakeMicrosoftAccountService
        {
            GetCapesHandler = _ => throw new MicrosoftAccountProfileRefreshException("HTTP 503")
        };
        var viewModel = CreateViewModel(
            new FakeAccountStore(),
            new FakeStatusService(),
            microsoftAccountService,
            floatingMessageService: floatingMessageService);
        var account = new LauncherAccount
        {
            Id = "microsoft-00000000000000000000000000000001",
            DisplayName = "Player",
            Uuid = "00000000000000000000000000000001",
            IsOffline = false,
            CachedCapeOptions = [cachedCape]
        };
        viewModel.AccountList.Accounts.Add(account);
        viewModel.SelectAccount(account);
        var selectedCapeBeforeRefresh = viewModel.Appearance.SelectedAccountCapeOption;
        var capeOptionsBeforeRefresh = viewModel.Appearance.SelectedAccountCapeOptions.ToList();

        await viewModel.Appearance.RefreshSelectedAccountProfileCommand.ExecuteAsync(null);

        Assert.Equal(Strings.Status_LoadAccountProfileFailed, viewModel.Appearance.AccountProfileMessage);
        Assert.Equal(Strings.Status_LoadAccountProfileFailed, floatingMessageService.LastMessage);
        Assert.Equal("返回代码：HTTP 503", viewModel.Appearance.AccountProfileErrorCodeMessage);
        Assert.True(viewModel.Appearance.HasAccountProfileErrorCode);
        Assert.Equal(capeOptionsBeforeRefresh, viewModel.Appearance.SelectedAccountCapeOptions);
        Assert.Same(selectedCapeBeforeRefresh, viewModel.Appearance.SelectedAccountCapeOption);
    }

    [Fact]
    public async Task RefreshSelectedAccountProfileTooManyRequestsShowsFriendlyFloatingMessage()
    {
        var floatingMessageService = new FakeFloatingMessageService();
        var microsoftAccountService = new FakeMicrosoftAccountService
        {
            GetCapesHandler = _ => throw new MicrosoftAccountProfileRefreshException("HTTP 429 / too many request")
        };
        var viewModel = CreateViewModel(
            new FakeAccountStore(),
            new FakeStatusService(),
            microsoftAccountService,
            floatingMessageService: floatingMessageService);
        var account = new LauncherAccount
        {
            Id = "microsoft-00000000000000000000000000000001",
            DisplayName = "Player",
            Uuid = "00000000000000000000000000000001",
            IsOffline = false
        };
        viewModel.AccountList.Accounts.Add(account);
        viewModel.SelectAccount(account);

        await viewModel.Appearance.RefreshSelectedAccountProfileCommand.ExecuteAsync(null);

        Assert.Equal(Strings.Status_AccountProfileRefreshTooFrequent, viewModel.Appearance.AccountProfileMessage);
        Assert.Equal(Strings.Status_AccountProfileRefreshTooFrequent, floatingMessageService.LastMessage);
        Assert.Equal("返回代码：HTTP 429 / too many request", viewModel.Appearance.AccountProfileErrorCodeMessage);
    }

    private static LauncherSkinRecord CreateSkinRecord(
        string id,
        string contentHash,
        MinecraftSkinModel skinModel)
    {
        return new LauncherSkinRecord
        {
            Id = id,
            Source = $"{id}.png",
            SkinModel = skinModel,
            ContentHash = contentHash,
            AddedAtUtc = DateTimeOffset.UnixEpoch
        };
    }

    private static AccountPageViewModel CreateViewModel(
        FakeAccountStore accountStore,
        FakeStatusService statusService,
        FakeMicrosoftAccountService? microsoftAccountService = null,
        FakeAccountDialogService? dialogService = null,
        FakeFilePickerService? filePickerService = null,
        FakeSkinFileValidator? skinFileValidator = null,
        FakeAccountSkinLibraryService? skinLibraryService = null,
        FakeFloatingMessageService? floatingMessageService = null)
    {
        var accountList = new AccountListViewModel(accountStore);
        var microsoftService = microsoftAccountService ?? new FakeMicrosoftAccountService();
        var offlineUuidService = new FakeOfflineAccountUuidService();
        var accountDialogService = dialogService ?? new FakeAccountDialogService();
        var accountSkinModelDialog = new AccountSkinModelDialogViewModel();
        return new AccountPageViewModel(
            accountList,
            new AccountDialogViewModel(accountList, microsoftService, offlineUuidService, statusService),
            new AccountAppearanceViewModel(
                accountList,
                microsoftService,
                skinLibraryService ?? new FakeAccountSkinLibraryService(),
                accountSkinModelDialog,
                accountDialogService,
                filePickerService ?? new FakeFilePickerService(),
                skinFileValidator ?? new FakeSkinFileValidator(true),
                floatingMessageService: floatingMessageService),
            new AccountOfflineUuidViewModel(
                accountList,
                offlineUuidService,
                statusService,
                new FakeClipboardService()),
            accountDialogService);
    }

    private sealed class FakeAccountStore : IAccountStore
    {
        private readonly TaskCompletionSource? saveCompletion;
        private readonly IReadOnlyList<LauncherAccount> accountsToLoad;
        private readonly Func<Task<IReadOnlyList<LauncherAccount>>>? loadAccountsAsync;

        public FakeAccountStore(
            TaskCompletionSource? saveCompletion = null,
            IReadOnlyList<LauncherAccount>? accountsToLoad = null,
            Func<Task<IReadOnlyList<LauncherAccount>>>? loadAccountsAsync = null)
        {
            this.saveCompletion = saveCompletion;
            this.accountsToLoad = accountsToLoad ?? [];
            this.loadAccountsAsync = loadAccountsAsync;
        }

        public int SaveCount { get; private set; }

        public IReadOnlyList<LauncherAccount> LastSavedAccounts { get; private set; } = [];

        public async Task<AccountStoreSnapshot> LoadAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<LauncherAccount> accounts;
            if (loadAccountsAsync is not null)
                accounts = await loadAccountsAsync();
            else
                accounts = accountsToLoad;

            return new AccountStoreSnapshot(accounts, accounts.SingleOrDefault()?.Id);
        }

        public Task SaveOrderAsync(
            string? selectedAccountId,
            IEnumerable<LauncherAccount> accounts,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            LastSavedAccounts = accounts.ToList();
            return saveCompletion?.Task ?? Task.CompletedTask;
        }
    }

    private sealed class FakeMicrosoftAccountService : IMicrosoftAccountService
    {
        public Func<LauncherAccount>? LoginHandler { get; init; }

        public Func<LauncherAccount, string, LauncherAccount>? ChangeNameHandler { get; init; }

        public Func<LauncherAccount, string, MinecraftSkinModel, LauncherAccount>? UploadSkinHandler { get; init; }

        public Func<LauncherAccount, LauncherAccount>? RefreshProfileHandler { get; init; }

        public Func<LauncherAccount, Task<LauncherAccount>>? RefreshProfileAsyncHandler { get; init; }

        public Func<LauncherAccount, IReadOnlyList<AccountCapeOption>>? GetCapesHandler { get; init; }

        public Func<LauncherAccount, string?, Task>? SetActiveCapeHandler { get; init; }

        public int UploadSkinCount { get; private set; }

        public int RefreshProfileCount { get; private set; }

        public int GetCapesCount { get; private set; }

        public int SetActiveCapeCount { get; private set; }

        public string? LastSkinFilePath { get; private set; }

        public MinecraftSkinModel? LastSkinModel { get; private set; }

        public string? LastCapeId { get; private set; }

        public Task<IReadOnlyList<LauncherAccount>> GetSavedAccountsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LauncherAccount>>([]);
        }

        public Task<LauncherAccount> LoginInteractivelyAsync(CancellationToken cancellationToken = default)
        {
            return LoginHandler is null
                ? throw new NotSupportedException()
                : Task.FromResult(LoginHandler());
        }

        public Task DeleteAccountAsync(LauncherAccount account, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<AccountCapeOption>> GetCapesAsync(LauncherAccount account, CancellationToken cancellationToken = default)
        {
            GetCapesCount++;
            return GetCapesHandler is null
                ? throw new NotSupportedException()
                : Task.FromResult(GetCapesHandler(account));
        }

        public Task<LauncherAccount> RefreshAccountProfileAsync(
            LauncherAccount account,
            CancellationToken cancellationToken = default)
        {
            RefreshProfileCount++;
            if (RefreshProfileAsyncHandler is not null)
                return RefreshProfileAsyncHandler(account);

            return RefreshProfileHandler is null
                ? throw new NotSupportedException()
                : Task.FromResult(RefreshProfileHandler(account));
        }

        public Task<LauncherAccount> UploadSkinAsync(
            LauncherAccount account,
            string skinFilePath,
            MinecraftSkinModel skinModel,
            CancellationToken cancellationToken = default)
        {
            UploadSkinCount++;
            LastSkinFilePath = skinFilePath;
            LastSkinModel = skinModel;
            return UploadSkinHandler is null
                ? throw new NotSupportedException()
                : Task.FromResult(UploadSkinHandler(account, skinFilePath, skinModel));
        }

        public Task SetActiveCapeAsync(LauncherAccount account, string? capeId, CancellationToken cancellationToken = default)
        {
            SetActiveCapeCount++;
            LastCapeId = capeId;
            return SetActiveCapeHandler is null
                ? Task.CompletedTask
                : SetActiveCapeHandler(account, capeId);
        }

        public Task<LauncherAccount> ChangeNameAsync(LauncherAccount account, string newName, CancellationToken cancellationToken = default)
        {
            return ChangeNameHandler is null
                ? throw new NotSupportedException()
                : Task.FromResult(ChangeNameHandler(account, newName));
        }
    }

    private sealed class FakeStatusService : IStatusService
    {
        public event Action<string>? MessageReported;

        public string? LastMessage { get; private set; }

        public void Report(string message)
        {
            LastMessage = message;
            MessageReported?.Invoke(message);
        }
    }

    private sealed class FakeFloatingMessageService : IFloatingMessageService
    {
        public event Action<string>? MessageRequested;

        public string? LastMessage { get; private set; }

        public void Show(string message)
        {
            LastMessage = message;
            MessageRequested?.Invoke(message);
        }
    }

    private sealed class FakeAccountDialogService : IAccountDialogService
    {
        public void Attach(
            AccountPageViewModel accountPage,
            DialogHost addAccountHost,
            DialogHost deleteAccountHost,
            DialogHost renameAccountHost,
            DialogHost skinModelDialogHost,
            DialogHost skinManagerDialogHost)
        {
        }

        public string? LastSkinModelFilePath { get; private set; }

        public MinecraftSkinModel? LastExistingSkinModel { get; private set; }

        public bool WasSkinManagerShown { get; private set; }

        public bool WasSkinManagerCanceled { get; private set; }

        public bool WasSkinFormatErrorShown { get; private set; }

        public void ShowAddAccountDialog()
        {
        }

        public void ShowDeleteAccountDialog(LauncherAccount account)
        {
        }

        public void ShowRenameAccountDialog()
        {
        }

        public void ShowSkinModelDialog(string skinFilePath)
        {
            LastSkinModelFilePath = skinFilePath;
        }

        public void ShowSkinModelDialog(MinecraftSkinModel skinModel)
        {
            LastExistingSkinModel = skinModel;
        }

        public void ShowSkinFormatErrorDialog()
        {
            WasSkinFormatErrorShown = true;
        }

        public void ShowSkinManagerDialog()
        {
            WasSkinManagerShown = true;
        }

        public void CancelAddAccountDialog()
        {
        }

        public void BackAddAccountDialog()
        {
        }

        public Task ConfirmAddAccountDialogAsync()
        {
            throw new NotSupportedException();
        }

        public void CancelDeleteAccountDialog()
        {
        }

        public Task ConfirmDeleteAccountDialogAsync()
        {
            throw new NotSupportedException();
        }

        public void CancelRenameAccountDialog()
        {
        }

        public Task ConfirmRenameAccountDialogAsync()
        {
            throw new NotSupportedException();
        }

        public void CancelSkinModelDialog()
        {
        }

        public Task ConfirmSkinModelDialogAsync()
        {
            throw new NotSupportedException();
        }

        public void CancelSkinManagerDialog()
        {
            WasSkinManagerCanceled = true;
        }

        public void Prewarm()
        {
        }
    }

    private sealed class FakeClipboardService : IClipboardService
    {
        public void CopyText(string text)
        {
        }
    }

    private sealed class FakeFilePickerService : IFilePickerService
    {
        private readonly string? skinFilePath;

        public FakeFilePickerService(string? skinFilePath = null)
        {
            this.skinFilePath = skinFilePath;
        }

        public string? PickMinecraftSkin()
        {
            return skinFilePath;
        }

        public string? PickJavaExecutable()
        {
            return null;
        }

        public string? PickModFile()
        {
            return null;
        }

        public string? PickSaveArchive()
        {
            return null;
        }

        public string? PickResourcePackArchive()
        {
            return null;
        }

        public string? PickShaderPackArchive()
        {
            return null;
        }

        public string? PickLocalImportFile()
        {
            return null;
        }

        public string? PickFolder(string title, string? initialDirectory = null)
        {
            return null;
        }

    }

    private sealed class FakeSkinFileValidator(bool isValid) : IMinecraftSkinFileValidator
    {
        public Task<MinecraftSkinFileValidationResult> ValidateAsync(
            string skinFilePath,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new MinecraftSkinFileValidationResult(isValid, 64, isValid ? 64 : 128));
        }
    }
}



