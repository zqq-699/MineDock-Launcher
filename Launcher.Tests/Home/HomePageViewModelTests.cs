using System.Windows;
using Launcher.App.Controls;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Accounts;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests.Home;

public sealed class HomePageViewModelTests
{
    [Fact]
    public void HomePageBuildsLaunchInstanceMenuItems()
    {
        var viewModel = CreateViewModel();
        var first = CreateInstance("first", "First World", "1.20.1", LoaderKind.Vanilla);
        var second = CreateInstance("second", "Fabric Pack", "1.21.1", LoaderKind.Fabric);

        viewModel.SetLaunchInstances([first, second]);
        viewModel.SetSelectedInstance(second);

        Assert.True(viewModel.HasLaunchInstances);
        Assert.False(viewModel.HasNoLaunchInstances);
        Assert.Equal(["First World", "Fabric Pack"], viewModel.LaunchInstances.Select(instance => instance.Name));
        Assert.False(viewModel.LaunchInstances[0].IsSelected);
        Assert.True(viewModel.LaunchInstances[1].IsSelected);
        Assert.True(viewModel.HasSelectedLaunchInstance);
        Assert.Same(viewModel.LaunchInstances[1], viewModel.SelectedLaunchInstanceItem);
    }

    [Fact]
    public async Task HomePageSelectLaunchInstanceCommandPersistsAndSelectsInstance()
    {
        GameInstance? requestedInstance = null;
        var statusService = new FakeStatusService();
        var viewModel = CreateViewModel(
            statusService,
            instance =>
            {
                requestedInstance = instance;
                return Task.FromResult(true);
            });
        var first = CreateInstance("first", "First World", "1.20.1", LoaderKind.Vanilla);
        var second = CreateInstance("second", "Fabric Pack", "1.21.1", LoaderKind.Fabric);
        viewModel.SetLaunchInstances([first, second]);

        await viewModel.SelectLaunchInstanceCommand.ExecuteAsync(viewModel.LaunchInstances[1]);

        Assert.Same(second, requestedInstance);
        Assert.Same(second, viewModel.SelectedInstance);
        Assert.False(viewModel.LaunchInstances[0].IsSelected);
        Assert.True(viewModel.LaunchInstances[1].IsSelected);
        Assert.True(viewModel.HasSelectedLaunchInstance);
        Assert.Same(viewModel.LaunchInstances[1], viewModel.SelectedLaunchInstanceItem);
        Assert.Equal(
            string.Format(Strings.Status_LaunchInstanceSelectedFormat, "Fabric Pack"),
            statusService.LastMessage);
    }

    [Fact]
    public void HomePageCollapsedLaunchInstanceSelectionFollowsSelectedInstance()
    {
        var viewModel = CreateViewModel();
        var first = CreateInstance("first", "First World", "1.20.1", LoaderKind.Vanilla);
        var second = CreateInstance("second", "Fabric Pack", "1.21.1", LoaderKind.Fabric);
        viewModel.SetLaunchInstances([first, second]);

        viewModel.SetSelectedInstance(first);
        Assert.Same(viewModel.LaunchInstances[0], viewModel.SelectedLaunchInstanceItem);

        viewModel.SetSelectedInstance(second);
        Assert.Same(viewModel.LaunchInstances[1], viewModel.SelectedLaunchInstanceItem);
    }

    [Fact]
    public async Task HomePageSelectLaunchInstanceCommandShowsFriendlyFailure()
    {
        var statusService = new FakeStatusService();
        var viewModel = CreateViewModel(statusService, _ => Task.FromResult(false));
        var instance = CreateInstance("first", "First World", "1.20.1", LoaderKind.Vanilla);
        viewModel.SetLaunchInstances([instance]);

        await viewModel.SelectLaunchInstanceCommand.ExecuteAsync(viewModel.LaunchInstances[0]);

        Assert.Null(viewModel.SelectedInstance);
        Assert.False(viewModel.LaunchInstances[0].IsSelected);
        Assert.False(viewModel.HasSelectedLaunchInstance);
        Assert.Null(viewModel.SelectedLaunchInstanceItem);
        Assert.Equal(Strings.Status_LaunchInstanceSelectionFailed, statusService.LastMessage);
    }

    [Fact]
    public void HomePageShowsEmptyLaunchInstanceState()
    {
        var viewModel = CreateViewModel();

        viewModel.SetLaunchInstances([]);

        Assert.Empty(viewModel.LaunchInstances);
        Assert.False(viewModel.HasLaunchInstances);
        Assert.True(viewModel.HasNoLaunchInstances);
        Assert.False(viewModel.HasSelectedLaunchInstance);
        Assert.Null(viewModel.SelectedLaunchInstanceItem);
    }

    [Fact]
    public async Task HomePageUsesMinecraftVersionTypeForLaunchGameIcons()
    {
        var viewModel = CreateViewModel(
            versions:
            [
                new MinecraftVersionInfo("1.21.4", "Release", false),
                new MinecraftVersionInfo("snapshot-profile", "Snapshot", false),
                new MinecraftVersionInfo("b1.7.3", "old_beta", false),
                new MinecraftVersionInfo("a1.2.6", "old_alpha", false)
            ]);
        await viewModel.EnsureVersionTypesLoadedAsync();
        var release = CreateInstance("release", "Release World", "1.21.4", LoaderKind.Vanilla);
        var snapshot = CreateInstance("snapshot", "Snapshot World", "snapshot-profile", LoaderKind.Vanilla);
        var beta = CreateInstance("beta", "Beta World", "b1.7.3", LoaderKind.Vanilla);
        var alpha = CreateInstance("alpha", "Alpha World", "a1.2.6", LoaderKind.Vanilla);

        viewModel.SetLaunchInstances([release, snapshot, beta, alpha]);

        Assert.Equal("/Assets/Icons/block/grass_block.png", viewModel.LaunchInstances[0].IconSource);
        Assert.Equal("/Assets/Icons/block/dirt_block.png", viewModel.LaunchInstances[1].IconSource);
        Assert.Equal("/Assets/Icons/block/craftingtable_block.png", viewModel.LaunchInstances[2].IconSource);
        Assert.Equal("/Assets/Icons/block/stone_block.png", viewModel.LaunchInstances[3].IconSource);
    }

    [Fact]
    public void HomePageUsesCustomLaunchGameIconWhenSet()
    {
        var viewModel = CreateViewModel();
        var game = CreateInstance("custom", "Custom World", "1.21.4", LoaderKind.Vanilla);
        game.IconSource = "/custom/game-icon.png";

        viewModel.SetLaunchInstances([game]);

        Assert.Equal("/custom/game-icon.png", viewModel.LaunchInstances[0].IconSource);
    }

    [Fact]
    public async Task HomePageLaunchPassesSelectedAccountToLaunchService()
    {
        var launchService = new FakeLaunchService();
        var account = new LauncherAccount
        {
            Id = "offline-1",
            DisplayName = "LocalUser",
            Uuid = "00000000-0000-0000-0000-000000000001",
            IsOffline = true
        };
        var viewModel = CreateViewModel(launchService: launchService, selectedAccount: account);
        var instance = CreateInstance("first", "First World", "1.20.1", LoaderKind.Vanilla);
        viewModel.SetSelectedInstance(instance);

        await viewModel.LaunchCommand.ExecuteAsync(null);

        Assert.Same(instance, launchService.LastInstance);
        Assert.Same(account, launchService.LastAccount);
    }

    [Fact]
    public async Task HomePageLaunchShowsFriendlyAccountFailure()
    {
        var statusService = new FakeStatusService();
        var launchService = new FakeLaunchService
        {
            ExceptionToThrow = new LaunchAccountSessionException()
        };
        var account = new LauncherAccount
        {
            Id = "microsoft-1",
            DisplayName = "LiveUser",
            Uuid = "00000000000000000000000000000001",
            IsOffline = false
        };
        var viewModel = CreateViewModel(statusService, launchService: launchService, selectedAccount: account);
        viewModel.SetSelectedInstance(CreateInstance("first", "First World", "1.20.1", LoaderKind.Vanilla));

        await viewModel.LaunchCommand.ExecuteAsync(null);

        Assert.Equal(Strings.Status_LaunchAccountUnavailable, statusService.LastMessage);
    }

    private static HomePageViewModel CreateViewModel(
        FakeStatusService? statusService = null,
        Func<GameInstance, Task<bool>>? selectLaunchInstance = null,
        IReadOnlyList<MinecraftVersionInfo>? versions = null,
        FakeLaunchService? launchService = null,
        LauncherAccount? selectedAccount = null)
    {
        statusService ??= new FakeStatusService();
        return new HomePageViewModel(
            launchService ?? new FakeLaunchService(),
            new FakeGameVersionService(versions ?? []),
            CreateAccountPage(statusService, selectedAccount),
            statusService,
            _ => { },
            _ => { },
            selectLaunchInstance ?? (_ => Task.FromResult(true)));
    }

    private static AccountPageViewModel CreateAccountPage(
        FakeStatusService statusService,
        LauncherAccount? selectedAccount = null)
    {
        var accountList = new AccountListViewModel(new FakeAccountStore());
        var microsoftAccountService = new FakeMicrosoftAccountService();
        var offlineUuidService = new FakeOfflineAccountUuidService();
        if (selectedAccount is not null)
        {
            accountList.Accounts.Add(selectedAccount);
            accountList.SelectAccount(selectedAccount);
        }

        var accountDialogService = new FakeAccountDialogService();
        var accountSkinModelDialog = new AccountSkinModelDialogViewModel();
        return new AccountPageViewModel(
            accountList,
            new AccountDialogViewModel(accountList, microsoftAccountService, offlineUuidService, statusService),
            new AccountAppearanceViewModel(
                accountList,
                microsoftAccountService,
                accountSkinModelDialog,
                accountDialogService,
                new FakeFilePickerService(),
                new FakeSkinFileValidator()),
            new AccountOfflineUuidViewModel(
                accountList,
                offlineUuidService,
                statusService,
                new FakeClipboardService()),
            accountDialogService);
    }

    private static GameInstance CreateInstance(string id, string name, string minecraftVersion, LoaderKind loader)
    {
        return new GameInstance
        {
            Id = id,
            Name = name,
            MinecraftVersion = minecraftVersion,
            VersionName = minecraftVersion,
            Loader = loader
        };
    }

    private sealed class FakeLaunchService : ILaunchService
    {
        public GameInstance? LastInstance { get; private set; }
        public LauncherAccount? LastAccount { get; private set; }
        public Exception? ExceptionToThrow { get; init; }

        public Task LaunchAsync(
            GameInstance instance,
            LauncherAccount account,
            LauncherSettings settings,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default)
        {
            LastInstance = instance;
            LastAccount = account;
            if (ExceptionToThrow is not null)
                throw ExceptionToThrow;

            return Task.CompletedTask;
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

    private sealed class FakeAccountStore : IAccountStore
    {
        public Task<IReadOnlyList<LauncherAccount>> LoadAsync(LauncherSettings settings)
        {
            return Task.FromResult<IReadOnlyList<LauncherAccount>>([]);
        }

        public Task SaveOrderAsync(LauncherSettings settings, IEnumerable<LauncherAccount> accounts)
        {
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

        public Task<LauncherAccount> RefreshAccountProfileAsync(
            LauncherAccount account,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<LauncherAccount> UploadSkinAsync(
            LauncherAccount account,
            string skinFilePath,
            MinecraftSkinModel skinModel,
            CancellationToken cancellationToken = default)
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

    private sealed class FakeAccountDialogService : IAccountDialogService
    {
        public void Attach(
            AccountPageViewModel accountPage,
            DialogHost addAccountHost,
            DialogHost deleteAccountHost,
            DialogHost renameAccountHost,
            DialogHost skinModelDialogHost)
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

        public void ShowSkinModelDialog(string skinFilePath)
        {
        }

        public void ShowSkinFormatErrorDialog()
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

        public void CancelSkinModelDialog()
        {
        }

        public Task ConfirmSkinModelDialogAsync()
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

    private sealed class FakeSkinFileValidator : IMinecraftSkinFileValidator
    {
        public Task<MinecraftSkinFileValidationResult> ValidateAsync(
            string skinFilePath,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new MinecraftSkinFileValidationResult(true, 64, 64));
        }
    }
}


