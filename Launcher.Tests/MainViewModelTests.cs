using System.Windows;
using Launcher.App.Controls;
using Launcher.App.Services;
using Launcher.App.ViewModels;
using Launcher.Application.Accounts;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public async Task PrimeAsyncUsesCachedSelectedAccountForHomePage()
    {
        var settings = new LauncherSettings
        {
            SelectedAccountId = "microsoft-00000000000000000000000000000001",
            Accounts =
            [
                new LauncherAccountRecord
                {
                    Id = "microsoft-00000000000000000000000000000001",
                    DisplayName = "CachedName",
                    Uuid = "00000000000000000000000000000001",
                    AvatarSource = "cached-avatar.png",
                    IsOffline = false
                }
            ]
        };
        var viewModel = CreateViewModel(new FakeGameInstanceService(), settings);

        await viewModel.PrimeAsync();

        Assert.Equal("CachedName", viewModel.AccountPage.SelectedAccount?.DisplayName);
        Assert.Equal("CachedName", viewModel.HomePage.HomeAccountDisplayName);
        Assert.Equal("cached-avatar.png", viewModel.HomePage.HomeAvatarUrl);
    }

    [Fact]
    public async Task HomePageRefreshesLaunchGamesWhenHomeNavigationIsRepeated()
    {
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.Add(CreateInstance("Vanilla World", "1.21.4"));
        var viewModel = CreateViewModel(instanceService);

        await viewModel.InitializeAsync();
        Assert.Single(viewModel.HomePage.LaunchInstances);

        instanceService.CreatedInstances.Clear();
        viewModel.SelectNavigationItem(viewModel.NavigationItems.Single(item => item.Page == "Home"));

        await TestAsync.WaitForAsync(() =>
            instanceService.GetInstancesCallCount >= 2
            && viewModel.HomePage.LaunchInstances.Count == 0);
        Assert.True(viewModel.HomePage.HasNoLaunchInstances);
        Assert.Null(viewModel.HomePage.SelectedInstance);
    }

    private static MainViewModel CreateViewModel(
        FakeGameInstanceService instanceService,
        LauncherSettings? settings = null)
    {
        var settingsService = new TestSettingsService(settings ?? new LauncherSettings());
        var statusService = new FakeStatusService();
        var gameVersionService = new FakeGameVersionService([]);
        var downloadTasksPage = new DownloadTasksPageViewModel();
        var accountPage = CreateAccountPage(statusService);
        var gameManagement = new GameManagementViewModel(
            new InstanceManagementViewModel(settingsService, instanceService, statusService),
            new LoaderSelectionViewModel(gameVersionService, [], statusService),
            new LocalModsViewModel(new FakeModService(), statusService),
            new ModrinthSearchViewModel(new FakeModrinthService(), statusService),
            statusService);

        return new MainViewModel(
            settingsService,
            accountPage,
            new DownloadPageViewModel(gameVersionService, instanceService, downloadTasksPage),
            downloadTasksPage,
            new GameSettingsPageViewModel(instanceService, gameVersionService),
            gameManagement,
            new FakeLaunchService(),
            gameVersionService,
            new FakeWindowService(),
            statusService);
    }

    private static AccountPageViewModel CreateAccountPage(FakeStatusService statusService)
    {
        var accountList = new AccountListViewModel(new FakeAccountStore());
        var microsoftAccountService = new FakeMicrosoftAccountService();
        var offlineUuidService = new FakeOfflineAccountUuidService();
        return new AccountPageViewModel(
            accountList,
            new AccountDialogViewModel(accountList, microsoftAccountService, offlineUuidService, statusService),
            new AccountAppearanceViewModel(accountList, microsoftAccountService),
            new AccountOfflineUuidViewModel(accountList, offlineUuidService, statusService),
            new AccountSkinModelDialogViewModel(),
            statusService,
            new FakeAccountDialogService(),
            new FakeClipboardService(),
            new FakeFilePickerService(),
            new FakeSkinFileValidator());
    }

    private static GameInstance CreateInstance(string name, string minecraftVersion)
    {
        return new GameInstance
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            MinecraftVersion = minecraftVersion,
            VersionName = minecraftVersion,
            Loader = LoaderKind.Vanilla,
            InstanceDirectory = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"))
        };
    }

    private sealed class FakeStatusService : IStatusService
    {
        public event Action<string>? MessageReported;

        public void Report(string message)
        {
            MessageReported?.Invoke(message);
        }
    }

    private sealed class FakeWindowService : IWindowService
    {
        public void Attach(Window window)
        {
        }

        public void Minimize()
        {
        }

        public void Close()
        {
        }
    }

    private sealed class FakeLaunchService : ILaunchService
    {
        public Task LaunchAsync(
            GameInstance instance,
            LauncherAccount account,
            LauncherSettings settings,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeModService : IModService
    {
        public Task<IReadOnlyList<LocalMod>> GetModsAsync(GameInstance instance, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LocalMod>>([]);
        }

        public Task<LocalMod> ImportAsync(GameInstance instance, string sourceJarPath, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task SetEnabledAsync(LocalMod mod, bool enabled, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task DeleteAsync(LocalMod mod, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeModrinthService : IModrinthService
    {
        public Task<IReadOnlyList<ModrinthProject>> SearchModsAsync(
            string query,
            string minecraftVersion,
            LoaderKind loader,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<ModrinthProject>>([]);
        }

        public Task<string> InstallLatestCompatibleAsync(
            ModrinthProject project,
            GameInstance instance,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
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
            Window owner,
            FrameworkElement contentLayer,
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
