using System.Windows;
using Launcher.App.Controls;
using Launcher.App.Resources;
using Launcher.App.Services;
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
    public void SelectingOfflineUuidModeUpdatesSelectedAccount()
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
        var saveCountBeforeModeChange = accountStore.SaveCount;

        viewModel.OfflineUuid.SelectedOfflineUuidOption = viewModel.OfflineUuid.OfflineUuidOptions
            .First(option => option.Mode == OfflineUuidGenerationMode.Random);

        Assert.Equal("Random-LocalUser", viewModel.SelectedAccount?.Uuid);
        Assert.Equal(OfflineUuidGenerationMode.Random, viewModel.SelectedAccount?.OfflineUuidGenerationMode);
        Assert.Equal("Random-LocalUser", viewModel.OfflineUuid.SelectedAccountUuidText);
        Assert.Equal(saveCountBeforeModeChange + 1, accountStore.SaveCount);
    }

    [Fact]
    public async Task RenameOfflineAccountKeepsRandomUuid()
    {
        var viewModel = CreateViewModel(new FakeAccountStore(), new FakeStatusService());
        var account = new LauncherAccount
        {
            Id = "offline-1",
            DisplayName = "LocalUser",
            Uuid = "fixed-random",
            OfflineUuidGenerationMode = OfflineUuidGenerationMode.Random,
            IsOffline = true
        };
        viewModel.AccountList.Accounts.Add(account);
        viewModel.SelectAccount(account);
        viewModel.Dialog.OpenRenameAccountDialog();
        viewModel.Dialog.RenameAccountName = "RenamedUser";

        await viewModel.Dialog.ConfirmRenameAccountDialogAsync();

        Assert.Equal("RenamedUser", viewModel.SelectedAccount?.DisplayName);
        Assert.Equal("fixed-random", viewModel.SelectedAccount?.Uuid);
        Assert.Equal(OfflineUuidGenerationMode.Random, viewModel.SelectedAccount?.OfflineUuidGenerationMode);
    }

    [Fact]
    public async Task RenameMicrosoftAccountKeepsAvatarWhenResponseHasNoAvatar()
    {
        var accountStore = new FakeAccountStore();
        var microsoftAccountService = new FakeMicrosoftAccountService
        {
            ChangeNameHandler = (account, newName) => new LauncherAccount
            {
                Id = account.Id,
                DisplayName = newName,
                Uuid = account.Uuid,
                IsOffline = false
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
            AvatarSource = "cached-avatar.png",
            SkinSource = "cached-skin.png",
            SkinModel = MinecraftSkinModel.Slim,
            IsOffline = false
        };
        viewModel.AccountList.Accounts.Add(account);
        viewModel.SelectAccount(account);
        viewModel.Dialog.OpenRenameAccountDialog();
        viewModel.Dialog.RenameAccountName = "NewName";

        await viewModel.Dialog.ConfirmRenameAccountDialogAsync();

        Assert.Equal("NewName", viewModel.SelectedAccount?.DisplayName);
        Assert.Equal("cached-avatar.png", viewModel.SelectedAccount?.AvatarSource);
        Assert.Equal("cached-skin.png", viewModel.SelectedAccount?.SkinSource);
        Assert.Equal(MinecraftSkinModel.Slim, viewModel.SelectedAccount?.SkinModel);
        var savedAccount = Assert.Single(accountStore.LastSavedAccounts);
        Assert.Equal("NewName", savedAccount.DisplayName);
        Assert.Equal("cached-skin.png", savedAccount.SkinSource);
        Assert.Equal(MinecraftSkinModel.Slim, savedAccount.SkinModel);
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
    public void SelectingManualUuidModeShowsEditorWithoutChangingAccount()
    {
        var viewModel = CreateViewModel(new FakeAccountStore(), new FakeStatusService());
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

        Assert.True(viewModel.OfflineUuid.HasManualUuidEditor);
        Assert.True(viewModel.OfflineUuid.CanApplyManualUuid);
        Assert.Equal("Standard-LocalUser", viewModel.OfflineUuid.ManualUuidText);
        Assert.Equal(OfflineUuidGenerationMode.Standard, viewModel.SelectedAccount?.OfflineUuidGenerationMode);
    }

    [Fact]
    public void ManualUuidEditorNotifiesApplyStateChanges()
    {
        var viewModel = CreateViewModel(new FakeAccountStore(), new FakeStatusService());
        var account = new LauncherAccount
        {
            Id = "offline-1",
            DisplayName = "LocalUser",
            Uuid = "Standard-LocalUser",
            OfflineUuidGenerationMode = OfflineUuidGenerationMode.Standard,
            IsOffline = true
        };
        var changedProperties = new List<string?>();
        viewModel.OfflineUuid.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);
        viewModel.AccountList.Accounts.Add(account);
        viewModel.SelectAccount(account);

        viewModel.OfflineUuid.SelectedOfflineUuidOption = viewModel.OfflineUuid.OfflineUuidOptions
            .First(option => option.Mode == OfflineUuidGenerationMode.Manual);
        viewModel.OfflineUuid.ManualUuidText = string.Empty;

        Assert.Contains(nameof(AccountOfflineUuidViewModel.CanApplyManualUuid), changedProperties);
        Assert.False(viewModel.OfflineUuid.CanApplyManualUuid);
    }

    [Fact]
    public void OfflineUuidModeOptionDisplaysTitle()
    {
        var viewModel = CreateViewModel(new FakeAccountStore(), new FakeStatusService());

        var option = viewModel.OfflineUuid.OfflineUuidOptions
            .First(option => option.Mode == OfflineUuidGenerationMode.Manual);

        Assert.Equal(option.Title, option.ToString());
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
    public async Task PickSkinFileOpensSkinModelDialogWithoutUploading()
    {
        var dialogService = new FakeAccountDialogService();
        var filePickerService = new FakeFilePickerService("skin.png");
        var microsoftAccountService = new FakeMicrosoftAccountService
        {
            UploadSkinHandler = (account, _, _) => account
        };
        var viewModel = CreateViewModel(
            new FakeAccountStore(),
            new FakeStatusService(),
            microsoftAccountService,
            dialogService,
            filePickerService);
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

        Assert.Equal("skin.png", dialogService.LastSkinModelFilePath);
        Assert.False(dialogService.WasSkinFormatErrorShown);
        Assert.False(viewModel.Appearance.SkinModelDialog.IsSkinModelDialogOpen);
        Assert.Equal(0, microsoftAccountService.UploadSkinCount);
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
    public async Task ConfirmSkinModelDialogAddsAndSelectsSlimModelWithoutUploading()
    {
        var microsoftAccountService = new FakeMicrosoftAccountService
        {
            UploadSkinHandler = (account, _, skinModel) => new LauncherAccount
            {
                Id = account.Id,
                DisplayName = account.DisplayName,
                Uuid = account.Uuid,
                SkinSource = "uploaded-skin.png",
                SkinModel = skinModel,
                IsOffline = false,
                HasFreshProfile = true
            }
        };
        var skinLibraryService = new FakeAccountSkinLibraryService
        {
            ImportSkinHandler = (_, _, skinModel) => new LauncherSkinRecord
            {
                Id = "skin-slim",
                Source = "cached-skin.png",
                SkinModel = skinModel,
                ContentHash = "hash-slim",
                AddedAtUtc = DateTimeOffset.UtcNow
            }
        };
        var accountStore = new FakeAccountStore();
        var viewModel = CreateViewModel(
            accountStore,
            new FakeStatusService(),
            microsoftAccountService,
            skinLibraryService: skinLibraryService);
        var account = new LauncherAccount
        {
            Id = "microsoft-00000000000000000000000000000001",
            DisplayName = "Player",
            Uuid = "00000000000000000000000000000001",
            IsOffline = false
        };
        viewModel.AccountList.Accounts.Add(account);
        viewModel.SelectAccount(account);
        viewModel.Appearance.SkinModelDialog.Open("skin.png");
        Assert.True(viewModel.Appearance.SkinModelDialog.IsSkinModelDialogOpen);
        Assert.Null(viewModel.Appearance.SkinModelDialog.SelectedSkinModelOption);
        Assert.False(viewModel.Appearance.SkinModelDialog.CanConfirmSkinModelDialog);
        viewModel.Appearance.SkinModelDialog.SelectedSkinModelOption = viewModel.Appearance.SkinModelDialog.SkinModelOptions
            .First(option => option.Model == MinecraftSkinModel.Slim);
        Assert.True(viewModel.Appearance.SkinModelDialog.CanConfirmSkinModelDialog);

        await viewModel.Appearance.ConfirmSkinModelDialogAsync();

        Assert.False(viewModel.Appearance.SkinModelDialog.IsSkinModelDialogOpen);
        Assert.Equal(0, microsoftAccountService.UploadSkinCount);
        Assert.Equal(1, skinLibraryService.ImportSkinCount);
        Assert.Equal("skin.png", skinLibraryService.LastSkinFilePath);
        Assert.Equal(MinecraftSkinModel.Slim, skinLibraryService.LastSkinModel);
        Assert.Equal("cached-skin.png", viewModel.Appearance.SelectedAccountSkin?.Source);
        Assert.Equal(MinecraftSkinModel.Slim, viewModel.Appearance.SelectedAccountSkin?.SkinModel);
        Assert.Null(viewModel.SelectedAccount?.SkinSource);
        Assert.Null(viewModel.SelectedAccount?.SkinModel);
        var savedAccount = Assert.Single(accountStore.LastSavedAccounts);
        var savedSkin = Assert.Single(savedAccount.SkinLibrary);
        Assert.Equal("skin-slim", savedSkin.Id);
        Assert.Null(savedAccount.ActiveSkinId);
        Assert.True(viewModel.Appearance.HasSelectedAccountSkinPreview);
        Assert.True(viewModel.Appearance.CanApplySelectedAccountSkin);
    }

    [Fact]
    public async Task CancelSkinModelDialogClearsPendingSkinUpload()
    {
        var microsoftAccountService = new FakeMicrosoftAccountService
        {
            UploadSkinHandler = (account, _, _) => account
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
        viewModel.Appearance.SkinModelDialog.Open("skin.png");

        viewModel.Appearance.SkinModelDialog.Cancel();
        await viewModel.Appearance.ConfirmSkinModelDialogAsync();

        Assert.False(viewModel.Appearance.SkinModelDialog.IsSkinModelDialogOpen);
        Assert.Equal(0, microsoftAccountService.UploadSkinCount);
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
        var viewModel = CreateViewModel(
            accountStore,
            new FakeStatusService(),
            microsoftAccountService);
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
    }

    [Fact]
    public void SkinManagerCanOpenForMicrosoftAccountsOnly()
    {
        var dialogService = new FakeAccountDialogService();
        var viewModel = CreateViewModel(
            new FakeAccountStore(),
            new FakeStatusService(),
            dialogService: dialogService);
        var offline = new LauncherAccount
        {
            Id = "offline-1",
            DisplayName = "Local",
            IsOffline = true
        };
        var microsoft = new LauncherAccount
        {
            Id = "microsoft-00000000000000000000000000000001",
            DisplayName = "Player",
            Uuid = "00000000000000000000000000000001",
            IsOffline = false
        };
        viewModel.AccountList.Accounts.Add(offline);
        viewModel.AccountList.Accounts.Add(microsoft);

        viewModel.SelectAccount(offline);

        Assert.False(viewModel.Appearance.CanManageSelectedAccountSkins);
        Assert.False(viewModel.Appearance.RequestOpenSkinManagerDialogCommand.CanExecute(null));

        viewModel.SelectAccount(microsoft);
        viewModel.Appearance.RequestOpenSkinManagerDialogCommand.Execute(null);

        Assert.True(viewModel.Appearance.CanManageSelectedAccountSkins);
        Assert.True(dialogService.WasSkinManagerShown);
    }

    [Fact]
    public void SkinManagerCanSelectSkinFromTile()
    {
        var viewModel = CreateViewModel(new FakeAccountStore(), new FakeStatusService());
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

        viewModel.Appearance.SelectAccountSkinCommand.Execute(nextSkin);

        Assert.Same(nextSkin, viewModel.Appearance.SelectedAccountSkin);
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
    public async Task ChangeSelectedSkinModelUpdatesLocalRecordWithoutUploading()
    {
        var accountStore = new FakeAccountStore();
        var dialogService = new FakeAccountDialogService();
        var microsoftAccountService = new FakeMicrosoftAccountService
        {
            UploadSkinHandler = (account, _, _) => account
        };
        var viewModel = CreateViewModel(
            accountStore,
            new FakeStatusService(),
            microsoftAccountService,
            dialogService: dialogService);
        var skin = CreateSkinRecord("skin-active", "hash-active", MinecraftSkinModel.Classic);
        var account = new LauncherAccount
        {
            Id = "microsoft-00000000000000000000000000000001",
            DisplayName = "Player",
            Uuid = "00000000000000000000000000000001",
            SkinSource = skin.Source,
            SkinModel = skin.SkinModel,
            ActiveSkinId = skin.Id,
            SkinLibrary = [skin],
            IsOffline = false
        };
        viewModel.AccountList.Accounts.Add(account);
        viewModel.SelectAccount(account);

        viewModel.Appearance.ChangeSelectedAccountSkinModelCommand.Execute(null);
        viewModel.Appearance.SkinModelDialog.OpenForExistingSkin(MinecraftSkinModel.Classic);
        viewModel.Appearance.SkinModelDialog.SelectedSkinModelOption = viewModel.Appearance.SkinModelDialog.SkinModelOptions
            .First(option => option.Model == MinecraftSkinModel.Slim);
        await viewModel.Appearance.ConfirmSkinModelDialogAsync();

        Assert.Equal(MinecraftSkinModel.Classic, dialogService.LastExistingSkinModel);
        Assert.Equal(0, microsoftAccountService.UploadSkinCount);
        Assert.Equal(MinecraftSkinModel.Slim, viewModel.Appearance.SelectedAccountSkin?.SkinModel);
        Assert.True(viewModel.Appearance.CanApplySelectedAccountSkin);
        var savedAccount = Assert.Single(accountStore.LastSavedAccounts);
        var savedSkin = Assert.Single(savedAccount.SkinLibrary);
        Assert.Equal(MinecraftSkinModel.Slim, savedSkin.SkinModel);
        Assert.Equal("skin-active", savedAccount.ActiveSkinId);
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
    public async Task ActiveSkinCannotBeDeleted()
    {
        var accountStore = new FakeAccountStore();
        var skinLibraryService = new FakeAccountSkinLibraryService();
        var viewModel = CreateViewModel(
            accountStore,
            new FakeStatusService(),
            skinLibraryService: skinLibraryService);
        var activeSkin = CreateSkinRecord("skin-active", "hash-active", MinecraftSkinModel.Classic);
        var account = new LauncherAccount
        {
            Id = "microsoft-00000000000000000000000000000001",
            DisplayName = "Player",
            Uuid = "00000000000000000000000000000001",
            SkinSource = activeSkin.Source,
            SkinModel = activeSkin.SkinModel,
            ActiveSkinId = activeSkin.Id,
            SkinLibrary = [activeSkin],
            IsOffline = false
        };
        viewModel.AccountList.Accounts.Add(account);
        viewModel.SelectAccount(account);
        var saveCountBeforeDelete = accountStore.SaveCount;

        Assert.False(viewModel.Appearance.CanDeleteSelectedAccountSkin);
        Assert.False(viewModel.Appearance.DeleteSelectedAccountSkinCommand.CanExecute(null));
        await viewModel.Appearance.DeleteSelectedAccountSkinCommand.ExecuteAsync(null);

        Assert.Equal(0, skinLibraryService.DeleteSkinCount);
        Assert.Equal(saveCountBeforeDelete, accountStore.SaveCount);
    }

    [Fact]
    public void SingleAppliedSkinHasNoAdjacentSelectionAndCannotApply()
    {
        var viewModel = CreateViewModel(new FakeAccountStore(), new FakeStatusService());
        var skin = new LauncherSkinRecord
        {
            Id = "skin-active",
            Source = "cached-skin.png",
            SkinModel = MinecraftSkinModel.Classic,
            ContentHash = "hash-active",
            AddedAtUtc = DateTimeOffset.UnixEpoch
        };
        var account = new LauncherAccount
        {
            Id = "microsoft-00000000000000000000000000000001",
            DisplayName = "Player",
            Uuid = "00000000000000000000000000000001",
            SkinSource = skin.Source,
            SkinModel = skin.SkinModel,
            ActiveSkinId = skin.Id,
            SkinLibrary = [skin],
            IsOffline = false
        };
        viewModel.AccountList.Accounts.Add(account);
        viewModel.SelectAccount(account);

        Assert.Same(skin, viewModel.Appearance.SelectedAccountSkin);
        Assert.Null(viewModel.Appearance.PreviousAccountSkin);
        Assert.Null(viewModel.Appearance.NextAccountSkin);
        Assert.False(viewModel.Appearance.SelectNextAccountSkinCommand.CanExecute(null));
        Assert.False(viewModel.Appearance.CanApplySelectedAccountSkin);

        viewModel.Appearance.SelectNextAccountSkinCommand.Execute(null);

        Assert.Same(skin, viewModel.Appearance.SelectedAccountSkin);
        Assert.False(viewModel.Appearance.CanApplySelectedAccountSkin);
    }

    [Fact]
    public void TwoSkinsExposeOnlyNextSelection()
    {
        var viewModel = CreateViewModel(new FakeAccountStore(), new FakeStatusService());
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

        Assert.Null(viewModel.Appearance.PreviousAccountSkin);
        Assert.Same(nextSkin, viewModel.Appearance.NextAccountSkin);
        Assert.False(viewModel.Appearance.SelectPreviousAccountSkinCommand.CanExecute(null));
        Assert.True(viewModel.Appearance.SelectNextAccountSkinCommand.CanExecute(null));

        viewModel.Appearance.SelectNextAccountSkinCommand.Execute(null);

        Assert.Same(nextSkin, viewModel.Appearance.SelectedAccountSkin);
        Assert.Same(activeSkin, viewModel.Appearance.PreviousAccountSkin);
        Assert.Null(viewModel.Appearance.NextAccountSkin);
        Assert.True(viewModel.Appearance.SelectPreviousAccountSkinCommand.CanExecute(null));
        Assert.False(viewModel.Appearance.SelectNextAccountSkinCommand.CanExecute(null));
    }

    [Fact]
    public void ThreeSkinsExposeLinearPreviousAndNextSelection()
    {
        var viewModel = CreateViewModel(new FakeAccountStore(), new FakeStatusService());
        var firstSkin = CreateSkinRecord("skin-first", "hash-first", MinecraftSkinModel.Classic);
        var activeSkin = CreateSkinRecord("skin-active", "hash-active", MinecraftSkinModel.Slim);
        var nextSkin = CreateSkinRecord("skin-next", "hash-next", MinecraftSkinModel.Classic);
        var account = new LauncherAccount
        {
            Id = "microsoft-00000000000000000000000000000001",
            DisplayName = "Player",
            Uuid = "00000000000000000000000000000001",
            SkinSource = activeSkin.Source,
            SkinModel = activeSkin.SkinModel,
            ActiveSkinId = activeSkin.Id,
            SkinLibrary = [firstSkin, activeSkin, nextSkin],
            IsOffline = false
        };
        viewModel.AccountList.Accounts.Add(account);
        viewModel.SelectAccount(account);

        Assert.Same(firstSkin, viewModel.Appearance.PreviousAccountSkin);
        Assert.Same(nextSkin, viewModel.Appearance.NextAccountSkin);

        viewModel.Appearance.SelectNextAccountSkinCommand.Execute(null);

        Assert.Same(nextSkin, viewModel.Appearance.SelectedAccountSkin);
        Assert.Same(activeSkin, viewModel.Appearance.PreviousAccountSkin);
        Assert.Null(viewModel.Appearance.NextAccountSkin);
        Assert.False(viewModel.Appearance.SelectNextAccountSkinCommand.CanExecute(null));
    }

    [Fact]
    public void DuplicateAppliedSkinRecordsBehaveAsSingleSkin()
    {
        var viewModel = CreateViewModel(new FakeAccountStore(), new FakeStatusService());
        var activeSkin = new LauncherSkinRecord
        {
            Id = "skin-active",
            Source = "cached-skin.png",
            SkinModel = MinecraftSkinModel.Classic,
            ContentHash = "hash-active",
            AddedAtUtc = DateTimeOffset.UnixEpoch
        };
        var duplicateSkin = new LauncherSkinRecord
        {
            Id = "skin-duplicate",
            Source = "cached-skin.png",
            SkinModel = MinecraftSkinModel.Classic,
            ContentHash = "hash-active",
            AddedAtUtc = DateTimeOffset.UnixEpoch.AddSeconds(1)
        };
        var account = new LauncherAccount
        {
            Id = "microsoft-00000000000000000000000000000001",
            DisplayName = "Player",
            Uuid = "00000000000000000000000000000001",
            SkinSource = activeSkin.Source,
            SkinModel = activeSkin.SkinModel,
            ActiveSkinId = activeSkin.Id,
            SkinLibrary = [activeSkin, duplicateSkin],
            IsOffline = false
        };
        viewModel.AccountList.Accounts.Add(account);
        viewModel.SelectAccount(account);

        Assert.Single(viewModel.Appearance.SelectedAccountSkins);
        Assert.Null(viewModel.Appearance.NextAccountSkin);
        Assert.False(viewModel.Appearance.SelectNextAccountSkinCommand.CanExecute(null));
        Assert.False(viewModel.Appearance.CanApplySelectedAccountSkin);
    }

    [Fact]
    public void MissingLocalSkinRecordsAreNotSelectable()
    {
        var skinLibraryService = new FakeAccountSkinLibraryService
        {
            GetAvailableSkinsHandler = account => account.SkinLibrary
                .Where(skin => !string.Equals(skin.Id, "skin-missing", StringComparison.Ordinal))
                .ToList()
        };
        var viewModel = CreateViewModel(
            new FakeAccountStore(),
            new FakeStatusService(),
            skinLibraryService: skinLibraryService);
        var activeSkin = new LauncherSkinRecord
        {
            Id = "skin-active",
            Source = "cached-skin.png",
            SkinModel = MinecraftSkinModel.Classic,
            ContentHash = "hash-active",
            AddedAtUtc = DateTimeOffset.UnixEpoch
        };
        var missingSkin = new LauncherSkinRecord
        {
            Id = "skin-missing",
            Source = "deleted-skin.png",
            SkinModel = MinecraftSkinModel.Slim,
            ContentHash = "hash-missing",
            AddedAtUtc = DateTimeOffset.UnixEpoch.AddSeconds(1)
        };
        var account = new LauncherAccount
        {
            Id = "microsoft-00000000000000000000000000000001",
            DisplayName = "Player",
            Uuid = "00000000000000000000000000000001",
            SkinSource = activeSkin.Source,
            SkinModel = activeSkin.SkinModel,
            ActiveSkinId = activeSkin.Id,
            SkinLibrary = [activeSkin, missingSkin],
            IsOffline = false
        };
        viewModel.AccountList.Accounts.Add(account);
        viewModel.SelectAccount(account);

        var skin = Assert.Single(viewModel.Appearance.SelectedAccountSkins);
        Assert.Same(activeSkin, skin);
        Assert.Null(viewModel.Appearance.PreviousAccountSkin);
        Assert.Null(viewModel.Appearance.NextAccountSkin);
        Assert.False(viewModel.Appearance.SelectPreviousAccountSkinCommand.CanExecute(null));
        Assert.False(viewModel.Appearance.SelectNextAccountSkinCommand.CanExecute(null));
        Assert.False(viewModel.Appearance.CanApplySelectedAccountSkin);
    }

    [Fact]
    public async Task ChangeSkinFailureShowsReturnedCode()
    {
        var microsoftAccountService = new FakeMicrosoftAccountService
        {
            UploadSkinHandler = (_, _, _) => throw new MicrosoftAccountSkinUpdateException("HTTP 400 / INVALID_SKIN")
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
    public async Task InitializeShowsCachedAccountBeforeSilentRefreshCompletes()
    {
        var cachedAccount = new LauncherAccount
        {
            Id = "microsoft-00000000000000000000000000000001",
            DisplayName = "CachedName",
            Uuid = "00000000000000000000000000000001",
            AvatarSource = "cached-avatar.png",
            IsOffline = false
        };
        var refreshCompletion = new TaskCompletionSource<LauncherAccount>(TaskCreationOptions.RunContinuationsAsynchronously);
        var accountStore = new FakeAccountStore(accountsToLoad: [cachedAccount]);
        var microsoftAccountService = new FakeMicrosoftAccountService
        {
            RefreshProfileAsyncHandler = _ => refreshCompletion.Task
        };
        var viewModel = CreateViewModel(
            accountStore,
            new FakeStatusService(),
            microsoftAccountService);
        var settings = new LauncherSettings
        {
            SelectedAccountId = cachedAccount.Id
        };

        await viewModel.InitializeAsync(settings);

        Assert.Same(cachedAccount, viewModel.SelectedAccount);
        Assert.Equal("CachedName", viewModel.SelectedAccount?.DisplayName);
        Assert.Equal("cached-avatar.png", viewModel.SelectedAccount?.AvatarSource);
        Assert.Equal(1, microsoftAccountService.RefreshProfileCount);
        Assert.Equal(0, accountStore.SaveCount);

        refreshCompletion.SetResult(new LauncherAccount
        {
            Id = cachedAccount.Id,
            DisplayName = "LiveName",
            Uuid = cachedAccount.Uuid,
            AvatarSource = "live-avatar.png",
            IsOffline = false,
            HasFreshProfile = true
        });

        await TestAsync.WaitForAsync(() =>
            viewModel.SelectedAccount?.DisplayName == "LiveName"
            && accountStore.SaveCount == 1);

        Assert.Equal("live-avatar.png", viewModel.SelectedAccount?.AvatarSource);
        Assert.Equal(1, accountStore.SaveCount);
        var savedAccount = Assert.Single(accountStore.LastSavedAccounts);
        Assert.Equal("LiveName", savedAccount.DisplayName);
        Assert.Equal("live-avatar.png", savedAccount.AvatarSource);
    }

    [Fact]
    public async Task InitializeKeepsPrimedSelectedAccountWhileLoadIsPending()
    {
        var cachedAccount = new LauncherAccount
        {
            Id = "microsoft-00000000000000000000000000000001",
            DisplayName = "CachedName",
            Uuid = "00000000000000000000000000000001",
            AvatarSource = "cached-avatar.png",
            IsOffline = false
        };
        var settings = new LauncherSettings
        {
            SelectedAccountId = cachedAccount.Id,
            Accounts =
            [
                new LauncherAccountRecord
                {
                    Id = cachedAccount.Id,
                    DisplayName = cachedAccount.DisplayName,
                    Uuid = cachedAccount.Uuid,
                    AvatarSource = cachedAccount.AvatarSource,
                    IsOffline = false
                }
            ]
        };
        var loadCompletion = new TaskCompletionSource<IReadOnlyList<LauncherAccount>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = CreateViewModel(
            new FakeAccountStore(loadAccountsAsync: () => loadCompletion.Task),
            new FakeStatusService(),
            new FakeMicrosoftAccountService());
        viewModel.PrimeFromSettings(settings);

        var initializeTask = viewModel.InitializeAsync(settings);

        Assert.Equal("CachedName", viewModel.SelectedAccount?.DisplayName);
        Assert.Equal("cached-avatar.png", viewModel.SelectedAccount?.AvatarSource);

        loadCompletion.SetResult([cachedAccount]);
        await initializeTask;

        Assert.Equal("CachedName", viewModel.SelectedAccount?.DisplayName);
        Assert.Equal("cached-avatar.png", viewModel.SelectedAccount?.AvatarSource);
    }

    [Fact]
    public async Task RefreshSelectedAccountProfileCanRunAgainAfterCachingCapes()
    {
        var firstCape = new AccountCapeOption
        {
            Id = "cape-1",
            DisplayName = "Cape",
            IsActive = true
        };
        var microsoftAccountService = new FakeMicrosoftAccountService
        {
            GetCapesHandler = _ => [firstCape]
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

        await viewModel.Appearance.RefreshSelectedAccountProfileCommand.ExecuteAsync(null);

        Assert.False(viewModel.Appearance.IsAccountProfileBusy);
        Assert.True(viewModel.Appearance.CanEditSelectedMicrosoftAccount);
        Assert.True(viewModel.Appearance.RefreshSelectedAccountProfileCommand.CanExecute(null));

        await viewModel.Appearance.RefreshSelectedAccountProfileCommand.ExecuteAsync(null);

        Assert.Equal(2, microsoftAccountService.GetCapesCount);
        Assert.False(viewModel.Appearance.IsAccountProfileBusy);
        Assert.True(viewModel.Appearance.CanEditSelectedMicrosoftAccount);
    }

    [Fact]
    public async Task RefreshSelectedAccountProfileFailureShowsReturnedCode()
    {
        var microsoftAccountService = new FakeMicrosoftAccountService
        {
            GetCapesHandler = _ => throw new MicrosoftAccountProfileRefreshException("HTTP 503")
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

        await viewModel.Appearance.RefreshSelectedAccountProfileCommand.ExecuteAsync(null);

        Assert.Equal(Strings.Status_LoadAccountProfileFailed, viewModel.Appearance.AccountProfileMessage);
        Assert.Equal("返回代码：HTTP 503", viewModel.Appearance.AccountProfileErrorCodeMessage);
        Assert.True(viewModel.Appearance.HasAccountProfileErrorCode);
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
        FakeAccountSkinLibraryService? skinLibraryService = null)
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
                skinFileValidator ?? new FakeSkinFileValidator(true)),
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

        public Task<IReadOnlyList<LauncherAccount>> LoadAsync(LauncherSettings settings)
        {
            if (loadAccountsAsync is not null)
                return loadAccountsAsync();

            return Task.FromResult(accountsToLoad);
        }

        public Task SaveOrderAsync(LauncherSettings settings, IEnumerable<LauncherAccount> accounts)
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

        public int UploadSkinCount { get; private set; }

        public int RefreshProfileCount { get; private set; }

        public int GetCapesCount { get; private set; }

        public string? LastSkinFilePath { get; private set; }

        public MinecraftSkinModel? LastSkinModel { get; private set; }

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
            throw new NotSupportedException();
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

        public void QueueOpenDialogBlurRefresh()
        {
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



