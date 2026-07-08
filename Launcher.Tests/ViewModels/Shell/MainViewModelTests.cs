using System.Windows;
using Launcher.App.Controls;
using Launcher.App.Models;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Accounts;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;

namespace Launcher.Tests.ViewModels.Shell;

public sealed class MainViewModelTests
{
    [Fact]
    public async Task PrimeAsyncUsesCachedSelectedAccountForHomePage()
    {
        var account = new LauncherAccount
        {
            Id = "microsoft-00000000000000000000000000000001",
            DisplayName = "CachedName",
            Uuid = "00000000000000000000000000000001",
            AvatarSource = "cached-avatar.png",
            IsOffline = false
        };
        var viewModel = CreateViewModel(new FakeGameInstanceService(), selectedAccount: account);

        await viewModel.PrimeAsync();

        Assert.Equal("CachedName", viewModel.AccountPage.SelectedAccount?.DisplayName);
        Assert.Equal("CachedName", viewModel.HomePage.HomeAccountDisplayName);
        Assert.Equal("cached-avatar.png", viewModel.HomePage.HomeAvatarUrl);
    }

    [Fact]
    public async Task PrimeAsyncPrimesHomeLaunchInstancesFromStoredInstances()
    {
        var first = CreateInstance("Vanilla World", "1.21.4");
        var second = CreateInstance("Fabric Pack", "1.20.1");
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.Add(first);
        instanceService.CreatedInstances.Add(second);
        var settings = new LauncherSettings
        {
            DefaultInstanceId = second.Id
        };
        var viewModel = CreateViewModel(instanceService, settings);

        await viewModel.PrimeAsync();

        Assert.Equal(1, instanceService.GetStoredInstancesCallCount);
        Assert.Equal(0, instanceService.GetInstancesCallCount);
        Assert.Equal(2, viewModel.GameManagement.Instances.Count);
        Assert.Equal(2, viewModel.HomePage.LaunchInstances.Count);
        Assert.Equal(second.Id, viewModel.GameManagement.SelectedInstance?.Id);
        Assert.Equal(second.Id, viewModel.HomePage.SelectedInstance?.Id);
        Assert.Same(
            viewModel.HomePage.LaunchInstances.Single(item => item.Instance.Id == second.Id),
            viewModel.HomePage.SelectedLaunchInstanceItem);
    }

    [Fact]
    public async Task HomeGameSettingsButtonNavigatesImmediatelyWhileGameSettingsRefreshIsPending()
    {
        var instanceService = new FakeGameInstanceService();
        var instance = CreateInstance("Vanilla World", "1.21.4");
        instanceService.CreatedInstances.Add(instance);
        var viewModel = CreateViewModel(instanceService);

        await viewModel.InitializeAsync();
        var refreshRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        instanceService.WaitBeforeGetInstances = refreshRelease.Task;

        var commandTask = viewModel.HomePage.OpenSelectedInstanceSettingsCommand.ExecuteAsync(null);
        await commandTask.WaitAsync(TimeSpan.FromMilliseconds(250));

        Assert.Equal("GameSettings", viewModel.CurrentPage);
        Assert.True(viewModel.GameSettingsPage.IsDetailsStep);
        Assert.Equal(instance.Id, viewModel.GameSettingsPage.SelectedInstance?.Instance.Id);

        await TestAsync.WaitForAsync(() => instanceService.GetInstancesCallCount >= 2);
        Assert.True(instanceService.GetInstancesCallCount >= 2);
        refreshRelease.SetResult();
        await TestAsync.WaitForAsync(() => !viewModel.GameSettingsPage.IsLoadingInstances);
        Assert.False(viewModel.GameSettingsPage.IsLoadingInstances);
    }

    [Fact]
    public async Task GameSettingsOnlineModInstallNavigatesToResourcesModPageWithInstanceFilters()
    {
        var instance = CreateInstance("Fabric Pack", "1.18.2");
        instance.Loader = LoaderKind.Fabric;
        instance.LoaderVersion = "latest";
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.Add(instance);
        var resourcesPage = new ResourcesPageViewModel(
            gameVersionService: new FakeGameVersionService(
            [
                new MinecraftVersionInfo("1.18.2", "release", false)
            ]));
        var viewModel = CreateViewModel(instanceService, resourcesPage: resourcesPage);
        await viewModel.InitializeAsync();
        await viewModel.GameSettingsPage.EnsureInstancesLoadedAsync();
        viewModel.GameSettingsPage.SelectInstanceCommand.Execute(viewModel.GameSettingsPage.VisibleInstances.Single());

        viewModel.GameSettingsPage.Details.ModManagement.InstallOnlineModCommand.Execute(null);

        await TestAsync.WaitForAsync(() => viewModel.CurrentPage == "Resources"
            && resourcesPage.ModPage.SelectedLoaderOption?.Id == "fabric");
        Assert.True(viewModel.NavigationItems.Single(item => item.Page == "Resources").IsSelected);
        Assert.True(resourcesPage.IsModsSection);
        Assert.Equal("1.18", resourcesPage.ModPage.SelectedVersionOption?.Id);
    }

    [Fact]
    public async Task ChangingMinecraftDirectoryRefreshesHomeAndGameSettingsInstances()
    {
        var originalDirectory = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"), ".minecraft");
        var newDirectory = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"), "custom-minecraft");
        var settings = new LauncherSettings
        {
            MinecraftDirectory = originalDirectory
        };
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.Add(CreateInstance("Vanilla World", "1.21.4"));
        var filePickerService = new FakeFilePickerService
        {
            FolderPath = newDirectory
        };
        var viewModel = CreateViewModel(instanceService, settings: settings, filePickerService: filePickerService);

        await viewModel.InitializeAsync();
        await viewModel.GameSettingsPage.EnsureInstancesLoadedAsync();
        Assert.Equal(["Vanilla World"], viewModel.HomePage.LaunchInstances.Select(instance => instance.Name));
        Assert.Equal(["Vanilla World"], viewModel.GameSettingsPage.VisibleInstances.Select(instance => instance.Name));

        instanceService.CreatedInstances.Clear();
        instanceService.CreatedInstances.Add(CreateInstance("Fabric Pack", "1.20.1"));

        await viewModel.SettingsPage.ChangeMinecraftDirectoryCommand.ExecuteAsync(null);

        await TestAsync.WaitForAsync(() =>
            instanceService.GetInstancesCallCount >= 4
            && viewModel.HomePage.LaunchInstances.Select(instance => instance.Name).SequenceEqual(["Fabric Pack"])
            && viewModel.GameSettingsPage.VisibleInstances.Select(instance => instance.Name).SequenceEqual(["Fabric Pack"]));

        Assert.Equal(Path.GetFullPath(newDirectory), viewModel.Settings.MinecraftDirectory);
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
    public async Task GameSettingsRenameUpdatesHomeAndGameManagementWithoutFullRefresh()
    {
        var instanceService = new FakeGameInstanceService();
        var defaultInstance = CreateInstance("Vanilla World", "1.21.4");
        var editedInstance = CreateInstance("Fabric Pack", "1.20.1");
        instanceService.CreatedInstances.Add(defaultInstance);
        instanceService.CreatedInstances.Add(editedInstance);
        var viewModel = CreateViewModel(instanceService);

        await viewModel.InitializeAsync();
        await viewModel.GameSettingsPage.EnsureInstancesLoadedAsync();
        var getInstancesCallCount = instanceService.GetInstancesCallCount;
        var editedItem = viewModel.GameSettingsPage.VisibleInstances.Single(instance => instance.Instance.Id == editedInstance.Id);
        viewModel.GameSettingsPage.SelectInstanceCommand.Execute(editedItem);
        viewModel.GameSettingsPage.Details.RequestEditInstanceCommand.Execute(null);
        viewModel.GameSettingsPage.EditDialog.InstanceName = "Renamed Fabric";

        await viewModel.GameSettingsPage.ConfirmEditInstanceDialogCommand.ExecuteAsync(null);

        Assert.Equal(getInstancesCallCount, instanceService.GetInstancesCallCount);
        Assert.Equal(defaultInstance.Id, viewModel.GameManagement.SelectedInstance?.Id);
        Assert.Contains(viewModel.GameManagement.Instances, instance => instance.Id == editedInstance.Id && instance.Name == "Renamed Fabric");
        Assert.Contains(viewModel.HomePage.LaunchInstances, item => item.Instance.Id == editedInstance.Id && item.Name == "Renamed Fabric");
        Assert.Equal(defaultInstance.Id, viewModel.HomePage.SelectedInstance?.Id);
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
        Assert.Equal(Strings.Dialog_JavaRequirementNotMetTitle, viewModel.JavaRequirementDialogTitle);
        Assert.Contains("Java 21", viewModel.JavaRequirementDialogMessage);

        viewModel.CloseJavaRequirementDialogCommand.Execute(null);

        Assert.False(viewModel.IsJavaRequirementDialogOpen);
    }

    [Fact]
    public async Task AutomaticJavaMissingFailureOpensDialog()
    {
        var launchService = new FakeLaunchService
        {
            ExceptionToThrow = new JavaRuntimeSelectionException(
                "missing java",
                JavaRuntimeSelectionFailureReason.AutomaticRuntimeMissing,
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
        Assert.Equal(Strings.Dialog_JavaRuntimeMissingTitle, viewModel.JavaRequirementDialogTitle);
        Assert.Contains("Java 21", viewModel.JavaRequirementDialogMessage);
        Assert.Contains("本机", viewModel.JavaRequirementDialogMessage);
    }

    [Theory]
    [InlineData(JavaRuntimeSelectionFailureReason.AutomaticRuntimeNotFound)]
    [InlineData(JavaRuntimeSelectionFailureReason.AutomaticRuntimeMissing)]
    public async Task WrappedAutomaticJavaFailureOpensOnlyJavaDialog(JavaRuntimeSelectionFailureReason reason)
    {
        var report = new LaunchFailureReport(
            LaunchFailureKind.StartupFailed,
            "Vanilla World",
            "1.20.5",
            null,
            @"C:\logs\launch-diagnostics.log",
            @"C:\logs");
        var launchService = new FakeLaunchService
        {
            ExceptionToThrow = new LaunchFailedException(
                report,
                new JavaRuntimeSelectionException("missing java", reason, 21))
        };
        var viewModel = CreateViewModel(
            new FakeGameInstanceService(),
            launchService: launchService,
            selectedAccount: CreateOfflineAccount());
        viewModel.HomePage.SetSelectedInstance(CreateInstance("Vanilla World", "1.20.5"));

        await viewModel.HomePage.LaunchCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsJavaRequirementDialogOpen);
        Assert.False(viewModel.LaunchStatusDialog.IsOpen);
        Assert.Contains("Java 21", viewModel.JavaRequirementDialogMessage);
    }

    [Fact]
    public async Task ManualJavaVersionMismatchOpensForceLaunchDialog()
    {
        var launchService = new FakeLaunchService
        {
            ExceptionToThrow = new JavaRuntimeSelectionException(
                "manual java too low",
                JavaRuntimeSelectionFailureReason.ManualRuntimeVersionTooLow,
                21,
                8)
        };
        var viewModel = CreateViewModel(
            new FakeGameInstanceService(),
            launchService: launchService,
            selectedAccount: CreateOfflineAccount());
        viewModel.HomePage.SetSelectedInstance(CreateInstance("Vanilla World", "1.20.5"));

        await viewModel.HomePage.LaunchCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsJavaRequirementDialogOpen);
        Assert.True(viewModel.IsJavaRequirementForceLaunchAvailable);
        Assert.Equal(Strings.Dialog_JavaManualVersionTooLowTitle, viewModel.JavaRequirementDialogTitle);
        Assert.Contains("Java 21", viewModel.JavaRequirementDialogMessage);
        Assert.Contains("Java 8", viewModel.JavaRequirementDialogMessage);
        Assert.False(viewModel.LaunchStatusDialog.IsOpen);
    }

    [Fact]
    public async Task ManualJavaVersionMismatchForceLaunchRetriesWithIgnoredRequirement()
    {
        var launchService = new FakeLaunchService
        {
            ExceptionToThrow = new JavaRuntimeSelectionException(
                "manual java too low",
                JavaRuntimeSelectionFailureReason.ManualRuntimeVersionTooLow,
                21,
                8)
        };
        var viewModel = CreateViewModel(
            new FakeGameInstanceService(),
            launchService: launchService,
            selectedAccount: CreateOfflineAccount());
        viewModel.HomePage.SetSelectedInstance(CreateInstance("Vanilla World", "1.20.5"));

        await viewModel.HomePage.LaunchCommand.ExecuteAsync(null);
        launchService.ExceptionToThrow = null;
        await viewModel.ForceLaunchFromJavaRequirementDialogCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsJavaRequirementDialogOpen);
        Assert.False(viewModel.IsJavaRequirementForceLaunchAvailable);
        Assert.Equal(2, launchService.LaunchCallCount);
        Assert.True(launchService.LastOptions?.IgnoreJavaVersionRequirement);
    }

    [Fact]
    public async Task ManualJavaVersionMismatchCancelDoesNotRetryLaunch()
    {
        var launchService = new FakeLaunchService
        {
            ExceptionToThrow = new JavaRuntimeSelectionException(
                "manual java too low",
                JavaRuntimeSelectionFailureReason.ManualRuntimeVersionTooLow,
                21,
                8)
        };
        var viewModel = CreateViewModel(
            new FakeGameInstanceService(),
            launchService: launchService,
            selectedAccount: CreateOfflineAccount());
        viewModel.HomePage.SetSelectedInstance(CreateInstance("Vanilla World", "1.20.5"));

        await viewModel.HomePage.LaunchCommand.ExecuteAsync(null);
        viewModel.CloseJavaRequirementDialogCommand.Execute(null);

        Assert.False(viewModel.IsJavaRequirementDialogOpen);
        Assert.False(viewModel.IsJavaRequirementForceLaunchAvailable);
        Assert.Equal(1, launchService.LaunchCallCount);
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

    private static MainViewModel CreateViewModel(
        FakeGameInstanceService instanceService,
        LauncherSettings? settings = null,
        FakeStatusService? statusService = null,
        FakeFloatingMessageService? floatingMessageService = null,
        FakeLaunchService? launchService = null,
        LauncherAccount? selectedAccount = null,
        FakeWindowService? windowService = null,
        FakeFilePickerService? filePickerService = null,
        ResourcesPageViewModel? resourcesPage = null,
        TestSettingsService? settingsService = null)
    {
        settingsService ??= new TestSettingsService(settings ?? new LauncherSettings());
        statusService ??= new FakeStatusService();
        floatingMessageService ??= new FakeFloatingMessageService();
        filePickerService ??= new FakeFilePickerService();
        var gameVersionService = new FakeGameVersionService([]);
        var downloadTasksPage = new DownloadTasksPageViewModel();
        var accountPage = CreateAccountPage(statusService, selectedAccount);
        var settingsPage = new SettingsPageViewModel(
            settingsService,
            statusService,
            new FakeSystemMemoryService(),
            new FakeJavaRuntimeDiscoveryService(),
            filePickerService,
            new FakeInstanceFolderService(),
            floatingMessageService,
            new FakeThemeService());
        var gameManagement = new GameManagementViewModel(
            new InstanceManagementViewModel(settingsService, instanceService, statusService),
            new LoaderSelectionViewModel(gameVersionService, [], statusService),
            new LocalModsViewModel(new FakeModService(), statusService),
            new ModrinthSearchViewModel(new FakeModrinthService(), statusService),
            statusService);

        return new MainViewModel(
            new DownloadSpeedLimitState(),
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
                new FakeInstanceBackupService(),
                new LocalModsViewModel(new FakeModService(), statusService),
                new LocalSavesViewModel(new FakeSaveService(), statusService),
                new LocalResourcePacksViewModel(new FakeResourcePackService(), statusService),
                new LocalShaderPacksViewModel(new FakeShaderPackService(), statusService),
                new FakeJavaRuntimeDiscoveryService(),
                filePickerService,
                floatingMessageService),
            resourcesPage ?? new ResourcesPageViewModel(),
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

    private sealed class PendingResourceCatalogService(Task<ResourceCatalogSearchResult> resultTask) : IResourceCatalogService
    {
        public int CallCount { get; private set; }

        public Task<ResourceCatalogSearchResult> SearchModsAsync(
            ResourceCatalogSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return resultTask;
        }

        public Task<ResourceProjectVersionsResult> GetProjectVersionsAsync(
            ResourceProjectVersionsRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ResourceProjectVersionsResult());
        }

        public Task<string> InstallProjectVersionAsync(
            ResourceProjectVersion version,
            GameInstance instance,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult("installed.jar");
        }

        public Task<string> DownloadProjectVersionAsync(
            ResourceProjectVersion version,
            string targetDirectory,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult("downloaded.jar");
        }

        public Task<bool> ProjectVersionDownloadExistsAsync(
            ResourceProjectVersion version,
            string targetDirectory,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public Task<bool> ProjectVersionInstallExistsAsync(
            ResourceProjectVersion version,
            GameInstance instance,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }
    }

    private sealed class QueueingUiDispatcher : IUiDispatcher
    {
        private readonly object gate = new();
        private readonly Queue<Action> actions = new();

        public bool HasAccess => true;

        public int PendingCount
        {
            get
            {
                lock (gate)
                    return actions.Count;
            }
        }

        public void Post(Action action)
        {
            lock (gate)
                actions.Enqueue(action);
        }

        public void Invoke(Action action)
        {
            action();
        }

        public void RunNext()
        {
            Action action;
            lock (gate)
                action = actions.Dequeue();
            action.Invoke();
        }
    }

    private static AccountPageViewModel CreateAccountPage(
        FakeStatusService statusService,
        LauncherAccount? selectedAccount = null)
    {
        var accountList = new AccountListViewModel(new FakeAccountStore(selectedAccount));
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
        public string? LastRevealedFilePath { get; private set; }

        public bool DirectoryExists(string folderPath)
        {
            return !string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath);
        }

        public string EnsureDirectoryExists(string folderPath)
        {
            var normalizedFolderPath = Path.GetFullPath(folderPath);
            Directory.CreateDirectory(normalizedFolderPath);
            return normalizedFolderPath;
        }

        public bool TryOpen(string folderPath)
        {
            LastOpenedPath = folderPath;
            return true;
        }

        public bool TryRevealFile(string filePath)
        {
            LastRevealedFilePath = filePath;
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
        public Exception? ExceptionToThrow { get; set; }
        public GameLaunchSession? SessionToReturn { get; init; }
        public LaunchRequestOptions? LastOptions { get; private set; }
        public int LaunchCallCount { get; private set; }

        public Task<GameLaunchSession> LaunchAsync(
            GameInstance instance,
            LauncherAccount account,
            LauncherSettings settings,
            IProgress<LauncherProgress>? progress,
            LaunchRequestOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LaunchCallCount++;
            LastOptions = options;
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

        public Task<LocalMod> ImportAsync(
            GameInstance instance,
            string sourceJarPath,
            bool overwriteExisting = false,
            CancellationToken cancellationToken = default)
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

    private sealed class FakeSaveService : ILocalSaveService
    {
        public Task<IReadOnlyList<LocalSave>> GetSavesAsync(GameInstance instance, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LocalSave>>([]);
        }

        public Task<LocalSaveImportResult> ImportFromArchiveAsync(
            GameInstance instance,
            string archivePath,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(LocalSaveImportResult.Failure(LocalSaveImportFailureReason.UnsupportedArchive));
        }

        public Task DeleteAsync(LocalSave save, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task DeleteAsync(IEnumerable<LocalSave> saves, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeInstanceBackupService : IInstanceBackupService
    {
        public Task<string> EnsureBackupDirectoryAsync(string backupDirectory, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Path.GetFullPath(backupDirectory));
        }

        public Task<int> CountBackupEntriesAsync(string backupDirectory, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }

        public Task<IReadOnlyList<InstanceBackupRecord>> GetBackupsAsync(
            string backupDirectory,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<InstanceBackupRecord>>([]);
        }

        public Task<InstanceBackupRecord> CreateBackupAsync(
            GameInstance instance,
            string backupDirectory,
            string backupName,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new InstanceBackupRecord
            {
                Name = backupName,
                FileName = $"{backupName}.zip",
                FullPath = Path.Combine(backupDirectory, $"{backupName}.zip"),
                SizeBytes = 1024 * 1024,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        public Task DeleteBackupAsync(
            string backupDirectory,
            string backupFullPath,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task RestoreBackupAsync(
            GameInstance instance,
            string backupDirectory,
            string backupFullPath,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeResourcePackService : ILocalResourcePackService
    {
        public Task<IReadOnlyList<LocalResourcePack>> GetResourcePacksAsync(
            GameInstance instance,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LocalResourcePack>>([]);
        }

        public Task<LocalResourcePackImportResult> ImportAsync(
            GameInstance instance,
            string archivePath,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(LocalResourcePackImportResult.Failure(LocalResourcePackImportFailureReason.UnsupportedArchive));
        }

        public Task DeleteAsync(LocalResourcePack resourcePack, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task DeleteAsync(IEnumerable<LocalResourcePack> resourcePacks, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeShaderPackService : ILocalShaderPackService
    {
        public Task<IReadOnlyList<LocalShaderPack>> GetShaderPacksAsync(
            GameInstance instance,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LocalShaderPack>>([]);
        }

        public Task<LocalShaderPackImportResult> ImportAsync(
            GameInstance instance,
            string archivePath,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(LocalShaderPackImportResult.Failure(LocalShaderPackImportFailureReason.UnsupportedArchive));
        }

        public Task DeleteAsync(LocalShaderPack shaderPack, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task DeleteAsync(IEnumerable<LocalShaderPack> shaderPacks, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
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

        public Task<IReadOnlyList<ModrinthVersionInfo>> GetFabricApiVersionsAsync(
            string minecraftVersion,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<ModrinthVersionInfo>>([]);
        }

        public Task<IReadOnlyList<ModrinthVersionInfo>> GetQuiltStandardLibraryVersionsAsync(
            string minecraftVersion,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<ModrinthVersionInfo>>([]);
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

        public Task<string> InstallFabricApiAsync(
            GameInstance instance,
            string versionId,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<string> InstallQuiltStandardLibraryAsync(
            GameInstance instance,
            string versionId,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeAccountStore : IAccountStore
    {
        private readonly LauncherAccount? selectedAccount;

        public FakeAccountStore(LauncherAccount? selectedAccount = null)
        {
            this.selectedAccount = selectedAccount;
        }

        public Task<AccountStoreSnapshot> LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(selectedAccount is null
                ? new AccountStoreSnapshot([], null)
                : new AccountStoreSnapshot([selectedAccount], selectedAccount.Id));
        }

        public Task SaveOrderAsync(
            string? selectedAccountId,
            IEnumerable<LauncherAccount> accounts,
            CancellationToken cancellationToken = default)
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
            DialogHost skinModelDialogHost,
            DialogHost skinManagerDialogHost)
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

        public void ShowSkinManagerDialog()
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

        public void CancelSkinManagerDialog()
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
        public string? FolderPath { get; init; }

        public string? PickMinecraftSkin()
        {
            return null;
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
            return FolderPath;
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


