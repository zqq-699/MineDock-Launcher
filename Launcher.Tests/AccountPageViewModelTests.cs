using System.Windows;
using Launcher.App.Controls;
using Launcher.App.Services;
using Launcher.App.ViewModels;
using Launcher.Application.Accounts;
using Launcher.Domain.Models;

namespace Launcher.Tests;

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

        viewModel.OpenAddAccountDialog();
        viewModel.SelectedAccountTypeOption = viewModel.AccountTypeOptions[0];
        await viewModel.ConfirmAddAccountDialogAsync();
        viewModel.NewOfflineAccountName = invalidName;

        await viewModel.ConfirmAddAccountDialogAsync();

        Assert.True(viewModel.IsNewOfflineAccountNameInvalid);
        Assert.Empty(viewModel.Accounts);
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
        viewModel.Accounts.Add(account);
        viewModel.SelectAccount(account);
        viewModel.OpenRenameAccountDialog();
        viewModel.RenameAccountName = invalidName;
        var saveCountBeforeRename = accountStore.SaveCount;

        await viewModel.ConfirmRenameAccountDialogAsync();

        Assert.True(viewModel.IsRenameAccountNameInvalid);
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
        viewModel.Accounts.Add(account);
        viewModel.OpenDeleteAccountDialog(account);

        var deleteTask = viewModel.ConfirmDeleteAccountDialogAsync();

        Assert.False(viewModel.IsDeleteAccountDialogOpen);
        Assert.Null(viewModel.AccountPendingDelete);
        Assert.Empty(viewModel.Accounts);

        saveCompletion.SetResult();
        await deleteTask;
    }

    [Fact]
    public async Task ConfirmAddOfflineAccountCreatesStandardUuid()
    {
        var viewModel = CreateViewModel(new FakeAccountStore(), new FakeStatusService());

        viewModel.OpenAddAccountDialog();
        viewModel.SelectedAccountTypeOption = viewModel.AccountTypeOptions[0];
        await viewModel.ConfirmAddAccountDialogAsync();
        viewModel.NewOfflineAccountName = "LocalUser";

        await viewModel.ConfirmAddAccountDialogAsync();

        var account = Assert.Single(viewModel.Accounts);
        Assert.Equal("Standard-LocalUser", account.Uuid);
        Assert.Equal(OfflineUuidGenerationMode.Standard, account.OfflineUuidGenerationMode);
        Assert.Equal("Standard-LocalUser", viewModel.OfflineUuid.SelectedAccountUuidText);
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
        viewModel.Accounts.Add(account);
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
        viewModel.Accounts.Add(account);
        viewModel.SelectAccount(account);
        viewModel.OpenRenameAccountDialog();
        viewModel.RenameAccountName = "RenamedUser";

        await viewModel.ConfirmRenameAccountDialogAsync();

        Assert.Equal("RenamedUser", viewModel.SelectedAccount?.DisplayName);
        Assert.Equal("fixed-random", viewModel.SelectedAccount?.Uuid);
        Assert.Equal(OfflineUuidGenerationMode.Random, viewModel.SelectedAccount?.OfflineUuidGenerationMode);
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
        viewModel.Accounts.Add(account);
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
        viewModel.Accounts.Add(account);
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
        viewModel.Accounts.Add(account);
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
        viewModel.Accounts.Add(account);
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

    private static AccountPageViewModel CreateViewModel(
        FakeAccountStore accountStore,
        FakeStatusService statusService)
    {
        var accountList = new AccountListViewModel(accountStore);
        var microsoftAccountService = new FakeMicrosoftAccountService();
        var offlineUuidService = new FakeOfflineAccountUuidService();
        return new AccountPageViewModel(
            accountList,
            new AccountDialogViewModel(accountList, microsoftAccountService, offlineUuidService, statusService),
            new AccountAppearanceViewModel(accountList, microsoftAccountService),
            new AccountOfflineUuidViewModel(accountList, offlineUuidService, statusService),
            statusService,
            new FakeAccountDialogService(),
            new FakeClipboardService(),
            new FakeFilePickerService());
    }

    private sealed class FakeAccountStore : IAccountStore
    {
        private readonly TaskCompletionSource? saveCompletion;

        public FakeAccountStore(TaskCompletionSource? saveCompletion = null)
        {
            this.saveCompletion = saveCompletion;
        }

        public int SaveCount { get; private set; }

        public Task<IReadOnlyList<LauncherAccount>> LoadAsync(LauncherSettings settings)
        {
            return Task.FromResult<IReadOnlyList<LauncherAccount>>([]);
        }

        public Task SaveOrderAsync(LauncherSettings settings, IEnumerable<LauncherAccount> accounts)
        {
            SaveCount++;
            return saveCompletion?.Task ?? Task.CompletedTask;
        }
    }

    private sealed class FakeMicrosoftAccountService : IMicrosoftAccountService
    {
        public Task<IReadOnlyList<LauncherAccount>> GetSavedAccountsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LauncherAccount>>([]);
        }

        public Task<LauncherAccount> LoginInteractivelyAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task DeleteAccountAsync(LauncherAccount account, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<AccountCapeOption>> GetCapesAsync(LauncherAccount account, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<LauncherAccount> UploadSkinAsync(LauncherAccount account, string skinFilePath, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task SetActiveCapeAsync(LauncherAccount account, string? capeId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<LauncherAccount> ChangeNameAsync(LauncherAccount account, string newName, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
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
            Window owner,
            FrameworkElement contentLayer,
            DialogHost addAccountHost,
            DialogHost deleteAccountHost,
            DialogHost renameAccountHost)
        {
        }

        public void ShowAddAccountDialog()
        {
        }

        public void ShowDeleteAccountDialog(LauncherAccount account)
        {
        }

        public void ShowRenameAccountDialog()
        {
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
        public string? PickMinecraftSkin()
        {
            return null;
        }
    }
}
