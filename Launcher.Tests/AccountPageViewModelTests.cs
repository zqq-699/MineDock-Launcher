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

    private static AccountPageViewModel CreateViewModel(
        FakeAccountStore accountStore,
        FakeStatusService statusService)
    {
        return new AccountPageViewModel(
            accountStore,
            new FakeMicrosoftAccountService(),
            statusService,
            new FakeAccountDialogService(),
            new FakeClipboardService(),
            new FakeFilePickerService());
    }

    private sealed class FakeAccountStore : IAccountStore
    {
        public int SaveCount { get; private set; }

        public Task<IReadOnlyList<LauncherAccount>> LoadAsync(LauncherSettings settings)
        {
            return Task.FromResult<IReadOnlyList<LauncherAccount>>([]);
        }

        public Task SaveOrderAsync(LauncherSettings settings, IEnumerable<LauncherAccount> accounts)
        {
            SaveCount++;
            return Task.CompletedTask;
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
