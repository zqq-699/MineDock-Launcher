using System.Windows;
using Launcher.App.Controls;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Accounts;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests.ViewModels.Shell;

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

    [Fact]
    public async Task GameSettingsLaunchRequestNavigatesHomeAndSelectsInstance()
    {
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.Add(CreateInstance("Vanilla World", "1.21.4"));
        instanceService.CreatedInstances.Add(CreateInstance("Fabric Pack", "1.20.1"));
        var viewModel = CreateViewModel(instanceService);

        await viewModel.InitializeAsync();
        await viewModel.GameSettingsPage.EnsureInstancesLoadedAsync();
        var requestedItem = viewModel.GameSettingsPage.VisibleInstances.Single(instance => instance.Name == "Fabric Pack");

        viewModel.GameSettingsPage.SelectInstanceAndGoHomeCommand.Execute(requestedItem);

        await TestAsync.WaitForAsync(() =>
            string.Equals(viewModel.CurrentPage, "Home", StringComparison.OrdinalIgnoreCase)
            && viewModel.HomePage.SelectedInstance?.Id == requestedItem.Instance.Id);

        Assert.Equal(requestedItem.Instance.Id, instanceService.LastDefaultInstanceId);
    }

    [Fact]
    public async Task GameSettingsDeleteRequestRefreshesHomeInstances()
    {
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.Add(CreateInstance("Vanilla World", "1.21.4"));
        instanceService.CreatedInstances.Add(CreateInstance("Fabric Pack", "1.20.1"));
        var viewModel = CreateViewModel(instanceService);

        await viewModel.InitializeAsync();
        await viewModel.GameSettingsPage.EnsureInstancesLoadedAsync();
        Assert.Equal(2, viewModel.HomePage.LaunchInstances.Count);

        var deletedItem = viewModel.GameSettingsPage.VisibleInstances.Single(instance => instance.Name == "Fabric Pack");
        viewModel.GameSettingsPage.OpenDeleteInstanceDialogCommand.Execute(deletedItem);
        await viewModel.GameSettingsPage.ConfirmDeleteInstanceDialogCommand.ExecuteAsync(null);

        await TestAsync.WaitForAsync(() =>
            viewModel.HomePage.LaunchInstances.Count == 1
            && viewModel.HomePage.LaunchInstances.Single().Name == "Vanilla World");

        Assert.Equal(deletedItem.Instance.Id, instanceService.LastDeletedInstanceId);
    }

    [Fact]
    public async Task GameSettingsStateSyncRefreshesInstancesWithoutReplayingEntranceAnimation()
    {
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.Add(CreateInstance("Vanilla World", "1.21.4"));
        var viewModel = CreateViewModel(instanceService);

        await viewModel.InitializeAsync();
        viewModel.SelectNavigationItem(viewModel.NavigationItems.Single(item => item.Page == "GameSettings"));

        await TestAsync.WaitForAsync(() =>
            viewModel.GameSettingsPage.ListEntranceAnimationToken == 1
            && viewModel.GameSettingsPage.VisibleInstances.Count == 1);

        instanceService.CreatedInstances.Add(CreateInstance("Fabric Pack", "1.20.1"));
        await viewModel.SyncCurrentStateAsync();

        Assert.Equal(1, viewModel.GameSettingsPage.ListEntranceAnimationToken);
        Assert.Equal(["Vanilla World", "Fabric Pack"], viewModel.GameSettingsPage.VisibleInstances.Select(instance => instance.Name));
    }

    [Fact]
    public async Task GlobalLaunchDefaultsRefreshUseGlobalGameSettingsTogglesImmediately()
    {
        var settings = new LauncherSettings
        {
            DefaultCheckFilesBeforeLaunch = false,
            DefaultAutoRepairMissingFiles = false,
            DefaultMinimizeLauncherAfterLaunch = false,
            DefaultLaunchFullScreen = false,
            DefaultWaitForPreLaunchCommand = true,
            DefaultMemorySettingsMode = MemorySettingsMode.Auto,
            DefaultMemoryMb = 4096,
            DefaultGameArguments = string.Empty
        };
        var instanceService = new FakeGameInstanceService();
        var instance = CreateInstance("Vanilla World", "1.21.4");
        instance.LaunchSettingsMode = LaunchSettingsMode.UseGlobal;
        instanceService.CreatedInstances.Add(instance);
        var viewModel = CreateViewModel(instanceService, settings);

        await viewModel.InitializeAsync();
        await viewModel.GameSettingsPage.EnsureInstancesLoadedAsync();
        viewModel.GameSettingsPage.SelectInstanceCommand.Execute(viewModel.GameSettingsPage.VisibleInstances.Single());

        Assert.False(viewModel.GameSettingsPage.Details.LaunchCheckFilesBeforeLaunchEnabled);
        Assert.False(viewModel.GameSettingsPage.Details.LaunchAutoRepairMissingFilesEnabled);
        Assert.False(viewModel.GameSettingsPage.Details.LaunchMinimizeLauncherAfterLaunchEnabled);
        Assert.False(viewModel.GameSettingsPage.Details.LaunchFullScreenEnabled);
        Assert.True(viewModel.GameSettingsPage.Details.LaunchWaitForPreLaunchCommand);
        Assert.Equal(MemorySettingsMode.Auto, viewModel.GameSettingsPage.Details.SelectedMemoryModeOption?.Mode);
        Assert.Equal(4096, viewModel.GameSettingsPage.Details.MemoryMb);
        Assert.False(viewModel.GameSettingsPage.Details.IsMemorySliderEnabled);
        Assert.False(viewModel.GameSettingsPage.Details.IsMemorySliderVisible);
        Assert.Equal(string.Empty, viewModel.GameSettingsPage.Details.LaunchGameArguments);

        viewModel.SettingsPage.DefaultCheckFilesBeforeLaunch = true;
        viewModel.SettingsPage.DefaultMinimizeLauncherAfterLaunch = true;
        viewModel.SettingsPage.DefaultLaunchFullScreen = true;
        viewModel.SettingsPage.DefaultWaitForPreLaunchCommand = false;
        viewModel.SettingsPage.SelectedMemoryModeOption = viewModel.SettingsPage.MemoryModeOptions
            .Single(option => option.Mode == MemorySettingsMode.Manual);
        viewModel.SettingsPage.DefaultMemoryMb = 8192;
        viewModel.SettingsPage.DefaultGameArguments = "--demo";

        Assert.True(viewModel.GameSettingsPage.Details.LaunchCheckFilesBeforeLaunchEnabled);
        Assert.True(viewModel.GameSettingsPage.Details.LaunchAutoRepairMissingFilesEnabled);
        Assert.True(viewModel.GameSettingsPage.Details.LaunchMinimizeLauncherAfterLaunchEnabled);
        Assert.True(viewModel.GameSettingsPage.Details.LaunchFullScreenEnabled);
        Assert.False(viewModel.GameSettingsPage.Details.LaunchWaitForPreLaunchCommand);
        Assert.Equal(MemorySettingsMode.Manual, viewModel.GameSettingsPage.Details.SelectedMemoryModeOption?.Mode);
        Assert.Equal(8192, viewModel.GameSettingsPage.Details.MemoryMb);
        Assert.False(viewModel.GameSettingsPage.Details.IsMemorySliderEnabled);
        Assert.True(viewModel.GameSettingsPage.Details.IsMemorySliderVisible);
        Assert.Equal("--demo", viewModel.GameSettingsPage.Details.LaunchGameArguments);
    }

    [Fact]
    public void FloatingMessageShowsOnlyWhenExplicitlyRequested()
    {
        var statusService = new FakeStatusService();
        var floatingMessageService = new FakeFloatingMessageService();
        var viewModel = CreateViewModel(
            new FakeGameInstanceService(),
            statusService: statusService,
            floatingMessageService: floatingMessageService);

        statusService.Report(Strings.Status_LaunchCanceled);

        Assert.False(viewModel.IsFloatingMessageOpen);
        Assert.Equal(string.Empty, viewModel.FloatingMessage);

        floatingMessageService.Show(Strings.Status_LaunchCanceled);

        Assert.True(viewModel.IsFloatingMessageOpen);
        Assert.Equal(Strings.Status_LaunchCanceled, viewModel.FloatingMessage);
    }

    [Fact]
    public async Task AutomaticJavaRequirementFailureOpensDialog()
    {
        var launchService = new FakeLaunchService
        {
            ExceptionToThrow = new JavaRuntimeSelectionException(
                "missing java",
                JavaRuntimeSelectionFailureReason.AutomaticRuntimeNotFound,
                21)
        };
        var account = CreateOfflineAccount();
        var viewModel = CreateViewModel(
            new FakeGameInstanceService(),
            launchService: launchService,
            selectedAccount: account);
        viewModel.HomePage.SetSelectedInstance(CreateInstance("Vanilla World", "1.20.5"));

        await viewModel.HomePage.LaunchCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsJavaRequirementDialogOpen);
        Assert.Contains("Java 21", viewModel.JavaRequirementDialogMessage);

        viewModel.CloseJavaRequirementDialogCommand.Execute(null);

        Assert.False(viewModel.IsJavaRequirementDialogOpen);
    }

    [Fact]
    public async Task JavaRequirementDialogCanNavigateToGlobalJavaSettings()
    {
        var launchService = new FakeLaunchService
        {
            ExceptionToThrow = new JavaRuntimeSelectionException(
                "missing java",
                JavaRuntimeSelectionFailureReason.AutomaticRuntimeNotFound,
                17)
        };
        var account = CreateOfflineAccount();
        var viewModel = CreateViewModel(
            new FakeGameInstanceService(),
            launchService: launchService,
            selectedAccount: account);
        viewModel.HomePage.SetSelectedInstance(CreateInstance("Vanilla World", "1.20.1"));

        await viewModel.HomePage.LaunchCommand.ExecuteAsync(null);
        viewModel.OpenJavaSettingsFromRequirementDialogCommand.Execute(null);

        Assert.False(viewModel.IsJavaRequirementDialogOpen);
        Assert.Equal("Settings", viewModel.CurrentPage);
        Assert.True(viewModel.SettingsPage.IsJavaMemorySection);
    }

    [Fact]
    public async Task LaunchFailureOpensLaunchStatusDialog()
    {
        var windowService = new FakeWindowService();
        var report = new LaunchFailureReport(
            LaunchFailureKind.StartupAbnormalExit,
            "Vanilla World",
            "1.20.1",
            1,
            @"C:\logs\launch-diagnostics.log",
            @"C:\logs",
            new LaunchFailureAnalysis(
                LaunchFailureCategory.JavaVersionMismatch,
                "java_version_mismatch",
                "java_version_mismatch",
                "select_required_java",
                RequiredJavaMajorVersion: 21,
                CurrentJavaMajorVersion: 8,
                ModName: "Fabric API"));
        var launchService = new FakeLaunchService
        {
            ExceptionToThrow = new LaunchProcessExitedException(report)
        };
        var viewModel = CreateViewModel(
            new FakeGameInstanceService(),
            launchService: launchService,
            selectedAccount: CreateOfflineAccount(),
            windowService: windowService);
        viewModel.HomePage.SetSelectedInstance(CreateInstance("Vanilla World", "1.20.1"));

        await viewModel.HomePage.LaunchCommand.ExecuteAsync(null);

        Assert.True(viewModel.LaunchStatusDialog.IsOpen);
        Assert.Equal(1, windowService.RestoreAndActivateCallCount);
        Assert.Equal(Strings.Dialog_LaunchStatusFailedTitle, viewModel.LaunchStatusDialog.Title);
        Assert.Contains("Vanilla World", viewModel.LaunchStatusDialog.Message);
        Assert.Contains("Fabric API", viewModel.LaunchStatusDialog.Message);
        Assert.Contains("Java 21", viewModel.LaunchStatusDialog.Message);
        Assert.Contains("Java 8", viewModel.LaunchStatusDialog.Message);
        Assert.True(viewModel.LaunchStatusDialog.HasAnalysis);
        Assert.Equal(Strings.Dialog_LaunchAnalysisJavaVersionTitle, viewModel.LaunchStatusDialog.AnalysisReasonTitle);
        Assert.Contains("Fabric API", viewModel.LaunchStatusDialog.AnalysisReasonDetail);
        Assert.Contains("Java 21", viewModel.LaunchStatusDialog.AnalysisReasonDetail);
        Assert.Contains("Java 8", viewModel.LaunchStatusDialog.AnalysisReasonDetail);
        Assert.Contains("Java 21", viewModel.LaunchStatusDialog.AnalysisRecommendation);
    }

    [Fact]
    public async Task RuntimeLaunchFailureOpensDialogOnlyOnce()
    {
        var report = new LaunchFailureReport(
            LaunchFailureKind.RuntimeAbnormalExit,
            "Vanilla World",
            "1.20.1",
            2,
            @"C:\logs\launch-diagnostics.log",
            @"C:\logs");
        var launchService = new FakeLaunchService
        {
            SessionToReturn = new GameLaunchSession(
                "instance",
                "Vanilla World",
                Task.FromResult(new LaunchExitResult(report)))
        };
        var viewModel = CreateViewModel(
            new FakeGameInstanceService(),
            launchService: launchService,
            selectedAccount: CreateOfflineAccount());
        viewModel.HomePage.SetSelectedInstance(CreateInstance("Vanilla World", "1.20.1"));

        await viewModel.HomePage.LaunchCommand.ExecuteAsync(null);
        await TestAsync.WaitForAsync(() => viewModel.LaunchStatusDialog.IsOpen);
        viewModel.LaunchStatusDialog.CloseCommand.Execute(null);

        Assert.False(viewModel.LaunchStatusDialog.IsOpen);
        Assert.False(launchService.SessionToReturn.TryMarkExitHandled());
    }

    [Fact]
    public async Task NormalRuntimeExitDoesNotOpenLaunchStatusDialog()
    {
        var launchService = new FakeLaunchService
        {
            SessionToReturn = new GameLaunchSession(
                "instance",
                "Vanilla World",
                Task.FromResult(LaunchExitResult.Success))
        };
        var viewModel = CreateViewModel(
            new FakeGameInstanceService(),
            launchService: launchService,
            selectedAccount: CreateOfflineAccount());
        viewModel.HomePage.SetSelectedInstance(CreateInstance("Vanilla World", "1.20.1"));

        await viewModel.HomePage.LaunchCommand.ExecuteAsync(null);
        await Task.Delay(50);

        Assert.False(viewModel.LaunchStatusDialog.IsOpen);
    }

    [Fact]
    public void LaunchStatusDialogOpenLogDirectoryUsesFolderService()
    {
        var folderService = new FakeInstanceFolderService();
        var statusService = new FakeStatusService();
        var dialog = new LaunchStatusDialogViewModel(folderService, statusService);

        dialog.Show(new LaunchFailureReport(
            LaunchFailureKind.StartupFailed,
            "Vanilla World",
            "1.20.1",
            null,
            @"C:\logs\launch-diagnostics.log",
            @"C:\logs"));
        dialog.OpenLogDirectoryCommand.Execute(null);

        Assert.Equal(@"C:\logs", folderService.LastOpenedPath);
    }

    [Fact]
    public void LaunchStatusDialogShowsMissingClasspathEntryPath()
    {
        const string missingPath = @"C:\Minecraft\versions\example\example.jar";
        var dialog = new LaunchStatusDialogViewModel(new FakeInstanceFolderService(), new FakeStatusService());

        dialog.Show(new LaunchFailureReport(
            LaunchFailureKind.StartupAbnormalExit,
            "Fabric World",
            "1.21.9-fabric-0.19.3",
            1,
            @"C:\logs\launch-diagnostics.log",
            @"C:\logs",
            new LaunchFailureAnalysis(
                LaunchFailureCategory.MissingGameFiles,
                "missing_game_files",
                "missing_classpath_entry",
                "repair_or_reinstall_instance",
                MissingPath: missingPath)));

        Assert.True(dialog.HasAnalysis);
        Assert.Contains(missingPath, dialog.Message);
        Assert.Contains(missingPath, dialog.AnalysisReasonDetail);
        Assert.Equal(Strings.Dialog_LaunchAnalysisMissingFilesTitle, dialog.AnalysisReasonTitle);
    }

    [Fact]
    public void LaunchStatusDialogShowsMissingClientJarPath()
    {
        const string missingPath = @"C:\Minecraft\versions\example\example.jar";
        var dialog = new LaunchStatusDialogViewModel(new FakeInstanceFolderService(), new FakeStatusService());

        dialog.Show(new LaunchFailureReport(
            LaunchFailureKind.StartupFailed,
            "Fabric World",
            "1.21.9-fabric-0.19.3",
            null,
            @"C:\logs\launch-diagnostics.log",
            @"C:\logs",
            new LaunchFailureAnalysis(
                LaunchFailureCategory.MissingGameFiles,
                "missing_game_files",
                "missing_client_jar",
                "repair_or_reinstall_instance",
                MissingPath: missingPath)));

        Assert.True(dialog.HasAnalysis);
        Assert.Contains(missingPath, dialog.Message);
        Assert.Contains(missingPath, dialog.AnalysisReasonDetail);
        Assert.Contains("客户端 jar", dialog.AnalysisReasonDetail);
    }

    [Fact]
    public async Task InstanceJavaSettingsChangesSynchronizeHomeSelectedInstance()
    {
        var instanceService = new FakeGameInstanceService();
        var instance = CreateInstance("Vanilla World", "1.20.5");
        instance.JavaSettingsMode = LaunchSettingsMode.UseGlobal;
        instance.JavaSelectionMode = JavaSelectionMode.Manual;
        instanceService.CreatedInstances.Add(instance);
        var viewModel = CreateViewModel(
            instanceService,
            settings: new LauncherSettings
            {
                JavaSelectionMode = JavaSelectionMode.Manual,
                SelectedJavaExecutablePath = @"C:\Global\jdk-21\bin\java.exe"
            });

        await viewModel.InitializeAsync();
        await viewModel.GameSettingsPage.EnsureInstancesLoadedAsync();
        viewModel.GameSettingsPage.SelectInstanceCommand.Execute(viewModel.GameSettingsPage.VisibleInstances.Single());
        viewModel.HomePage.SetSelectedInstance(instance);

        viewModel.GameSettingsPage.Details.SelectedInstanceJavaSettingsModeOption = viewModel.GameSettingsPage.Details.LaunchSettingsModeOptions
            .Single(option => option.Mode == LaunchSettingsMode.PerInstance);
        viewModel.GameSettingsPage.Details.SelectedInstanceJavaSelectionOption = viewModel.GameSettingsPage.Details.InstanceJavaSelectionOptions
            .Single(option => option.Id == "auto");

        await TestAsync.WaitForAsync(() =>
            viewModel.HomePage.SelectedInstance?.JavaSettingsMode == LaunchSettingsMode.PerInstance
            && viewModel.HomePage.SelectedInstance.JavaSelectionMode == JavaSelectionMode.Auto);
    }

    private static MainViewModel CreateViewModel(
        FakeGameInstanceService instanceService,
        LauncherSettings? settings = null,
        FakeStatusService? statusService = null,
        FakeFloatingMessageService? floatingMessageService = null,
        FakeLaunchService? launchService = null,
        LauncherAccount? selectedAccount = null,
        FakeWindowService? windowService = null)
    {
        var settingsService = new TestSettingsService(settings ?? new LauncherSettings());
        statusService ??= new FakeStatusService();
        floatingMessageService ??= new FakeFloatingMessageService();
        var gameVersionService = new FakeGameVersionService([]);
        var downloadTasksPage = new DownloadTasksPageViewModel();
        var accountPage = CreateAccountPage(statusService, selectedAccount);
        var settingsPage = new SettingsPageViewModel(
            settingsService,
            statusService,
            new FakeSystemMemoryService(),
            new FakeJavaRuntimeDiscoveryService(),
            new FakeFilePickerService(),
            floatingMessageService);
        var gameManagement = new GameManagementViewModel(
            new InstanceManagementViewModel(settingsService, instanceService, statusService),
            new LoaderSelectionViewModel(gameVersionService, [], statusService),
            new LocalModsViewModel(new FakeModService(), statusService),
            new ModrinthSearchViewModel(new FakeModrinthService(), statusService),
            statusService);

        return new MainViewModel(
            settingsService,
            accountPage,
            new DownloadPageViewModel(gameVersionService, instanceService, downloadTasksPage, []),
            downloadTasksPage,
            new GameSettingsPageViewModel(
                instanceService,
                gameVersionService,
                statusService,
                new FakeInstanceFolderService(),
                new FakeSystemMemoryService(),
                new FakeModService(),
                new FakeJavaRuntimeDiscoveryService(),
                new FakeFilePickerService(),
                floatingMessageService),
            settingsPage,
            gameManagement,
            windowService ?? new FakeWindowService(),
            statusService,
            floatingMessageService,
            ImmediateUiDispatcher.Instance,
            new HomePageViewModelFactory(
                launchService ?? new FakeLaunchService(),
                gameVersionService,
                statusService,
                floatingMessageService,
                new FakeWindowService(),
                ImmediateUiDispatcher.Instance),
            new LaunchStatusDialogViewModel(new FakeInstanceFolderService(), statusService));
    }

    private static AccountPageViewModel CreateAccountPage(
        FakeStatusService statusService,
        LauncherAccount? selectedAccount = null)
    {
        var accountList = new AccountListViewModel(new FakeAccountStore());
        if (selectedAccount is not null)
        {
            accountList.Accounts.Add(selectedAccount);
            accountList.SelectAccount(selectedAccount);
        }

        var microsoftAccountService = new FakeMicrosoftAccountService();
        var offlineUuidService = new FakeOfflineAccountUuidService();
        var accountDialogService = new FakeAccountDialogService();
        var accountSkinModelDialog = new AccountSkinModelDialogViewModel();
        return new AccountPageViewModel(
            accountList,
            new AccountDialogViewModel(accountList, microsoftAccountService, offlineUuidService, statusService),
            new AccountAppearanceViewModel(
                accountList,
                microsoftAccountService,
                new FakeAccountSkinLibraryService(),
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

    private static LauncherAccount CreateOfflineAccount()
    {
        return new LauncherAccount
        {
            Id = "offline-1",
            DisplayName = "LocalUser",
            Uuid = "00000000-0000-0000-0000-000000000001",
            IsOffline = true
        };
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
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            InstanceDirectory = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"))
        };
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

        public void Show(string message)
        {
            MessageRequested?.Invoke(message);
        }
    }

    private sealed class FakeInstanceFolderService : IInstanceFolderService
    {
        public string? LastOpenedPath { get; private set; }

        public bool TryOpen(string folderPath)
        {
            LastOpenedPath = folderPath;
            return true;
        }
    }

    private sealed class FakeWindowService : IWindowService
    {
        public int RestoreAndActivateCallCount { get; private set; }

        public void Attach(Window window)
        {
        }

        public void Minimize()
        {
        }

        public void RestoreAndActivate()
        {
            RestoreAndActivateCallCount++;
        }

        public void Close()
        {
        }
    }

    private sealed class FakeLaunchService : ILaunchService
    {
        public Exception? ExceptionToThrow { get; init; }
        public GameLaunchSession? SessionToReturn { get; init; }

        public Task<GameLaunchSession> LaunchAsync(
            GameInstance instance,
            LauncherAccount account,
            LauncherSettings settings,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow is not null)
                throw ExceptionToThrow;

            return Task.FromResult(SessionToReturn ?? new GameLaunchSession(
                instance.Id,
                instance.Name,
                Task.FromResult(LaunchExitResult.Success)));
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

        public Task<string> InstallFabricApiAsync(
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

        public void ShowSkinModelDialog(MinecraftSkinModel skinModel)
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

        public string? PickJavaExecutable()
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

    private sealed class FakeJavaRuntimeDiscoveryService : IJavaRuntimeDiscoveryService
    {
        public Task<IReadOnlyList<JavaRuntimeInfo>> DiscoverAsync(
            string? minecraftDirectory,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<JavaRuntimeInfo>>([]);
        }

        public Task<JavaRuntimeInfo> DiscoverExecutableAsync(
            string executablePath,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeSystemMemoryService : ISystemMemoryService
    {
        public SystemMemorySnapshot GetSnapshot()
        {
            return new SystemMemorySnapshot(
                TotalMemoryBytes: 16L * 1024L * 1024L * 1024L,
                AvailableMemoryBytes: 8L * 1024L * 1024L * 1024L);
        }
    }
}


