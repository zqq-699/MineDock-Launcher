using System.Net;
using System.Text.Json;
using Launcher.App.ViewModels;
using Launcher.Domain.Models;
using Launcher.Application.Services;
using Launcher.Infrastructure.FileSystem;
using Launcher.Infrastructure.Minecraft;
using Launcher.Infrastructure.Modrinth;
using Launcher.Infrastructure.Persistence;

namespace Launcher.Tests;

public sealed class LauncherCoreTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SettingsServiceWritesAndLoadsDefaults()
    {
        var service = new JsonSettingsService(tempRoot);

        var settings = await service.LoadAsync();
        settings.OfflineUsername = "Steve";
        settings.DefaultMemoryMb = 6144;
        await service.SaveAsync(settings);

        var loaded = await service.LoadAsync();

        Assert.Equal("Steve", loaded.OfflineUsername);
        Assert.Equal(6144, loaded.DefaultMemoryMb);
        Assert.Equal(tempRoot, loaded.DataDirectory);
        Assert.Equal(Path.GetFullPath(LauncherDefaults.DefaultMinecraftDirectory), loaded.MinecraftDirectory);
    }

    [Fact]
    public async Task SettingsServiceBackfillsMinecraftDirectory()
    {
        Directory.CreateDirectory(tempRoot);
        await File.WriteAllTextAsync(Path.Combine(tempRoot, "settings.json"), "{}");
        var service = new JsonSettingsService(tempRoot);

        var loaded = await service.LoadAsync();

        Assert.Equal(Path.GetFullPath(LauncherDefaults.DefaultMinecraftDirectory), loaded.MinecraftDirectory);
    }

    [Fact]
    public async Task SettingsServiceUsesCurrentExecutableMinecraftDirectory()
    {
        Directory.CreateDirectory(tempRoot);
        var staleMinecraftDirectory = Path.Combine(tempRoot, "old-debug", ".minecraft");
        await File.WriteAllTextAsync(
            Path.Combine(tempRoot, "settings.json"),
            $$"""
            {
              "MinecraftDirectory": "{{staleMinecraftDirectory.Replace("\\", "\\\\")}}"
            }
            """);
        var service = new JsonSettingsService(tempRoot);

        var loaded = await service.LoadAsync();

        Assert.Equal(Path.GetFullPath(LauncherDefaults.DefaultMinecraftDirectory), loaded.MinecraftDirectory);
    }

    [Fact]
    public async Task SettingsServicePersistsOfflineAccounts()
    {
        var service = new JsonSettingsService(tempRoot);
        var settings = await service.LoadAsync();
        settings.Accounts =
        [
            new LauncherAccountRecord
            {
                Id = "offline-alex",
                DisplayName = "Alex",
                IsOffline = true
            }
        ];

        await service.SaveAsync(settings);
        var loaded = await service.LoadAsync();

        var account = Assert.Single(loaded.Accounts);
        Assert.True(loaded.AccountsInitialized);
        Assert.Equal("offline-alex", account.Id);
        Assert.Equal("Alex", account.DisplayName);
        Assert.True(account.IsOffline);
    }

    [Fact]
    public async Task SettingsServiceKeepsInitializedEmptyAccountList()
    {
        var service = new JsonSettingsService(tempRoot);
        var settings = await service.LoadAsync();
        settings.AccountsInitialized = true;
        settings.Accounts.Clear();

        await service.SaveAsync(settings);
        var loaded = await service.LoadAsync();

        Assert.True(loaded.AccountsInitialized);
        Assert.Empty(loaded.Accounts);
    }

    [Fact]
    public async Task SettingsServicePreservesMixedAccountOrder()
    {
        var service = new JsonSettingsService(tempRoot);
        var settings = await service.LoadAsync();
        settings.AccountsInitialized = true;
        settings.Accounts =
        [
            new LauncherAccountRecord
            {
                Id = "offline-first",
                DisplayName = "First",
                IsOffline = true
            },
            new LauncherAccountRecord
            {
                Id = "microsoft-alex",
                DisplayName = "Alex",
                Uuid = "alexuuid",
                IsOffline = false
            },
            new LauncherAccountRecord
            {
                Id = "offline-last",
                DisplayName = "Last",
                IsOffline = true
            }
        ];

        await service.SaveAsync(settings);
        var loaded = await service.LoadAsync();

        Assert.Equal(
            ["offline-first", "microsoft-alex", "offline-last"],
            loaded.Accounts.Select(account => account.Id));
    }

    [Fact]
    public async Task SettingsServicePersistsCachedAccountCapes()
    {
        var service = new JsonSettingsService(tempRoot);
        var settings = await service.LoadAsync();
        settings.AccountsInitialized = true;
        settings.Accounts =
        [
            new LauncherAccountRecord
            {
                Id = "microsoft-alex",
                DisplayName = "Alex",
                Uuid = "alexuuid",
                IsOffline = false,
                Capes =
                [
                    new LauncherCapeRecord
                    {
                        Id = "cape-one",
                        DisplayName = "Cape One",
                        IsActive = true
                    }
                ]
            }
        ];

        await service.SaveAsync(settings);
        var loaded = await service.LoadAsync();

        var account = Assert.Single(loaded.Accounts);
        var cape = Assert.Single(account.Capes);
        Assert.Equal("cape-one", cape.Id);
        Assert.Equal("Cape One", cape.DisplayName);
        Assert.True(cape.IsActive);
    }

    [Fact]
    public async Task InstanceServiceCreatesIsolatedDirectoriesWithProvider()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = tempRoot,
            MinecraftDirectory = Path.Combine(tempRoot, ".minecraft"),
            DefaultMemoryMb = 3072
        };
        var settingsService = new TestSettingsService(settings);

        var repository = new JsonGameInstanceRepository(settingsService);
        var provider = new FakeLoaderProvider();
        var service = new GameInstanceService(settingsService, repository, [provider]);
        var instance = await service.CreateInstanceAsync("1.20.1", LoaderKind.Vanilla, null, "Test Instance", null);

        Assert.Equal("Test Instance", instance.VersionName);
        Assert.Equal("Test Instance", provider.LastIsolatedVersionName);
        Assert.Equal(settings.MinecraftDirectory, provider.LastGameDirectory);
        Assert.Equal(Path.Combine(settings.MinecraftDirectory, "versions", "Test Instance"), instance.InstanceDirectory);
        Assert.True(Directory.Exists(Path.Combine(instance.InstanceDirectory, "mods")));
        Assert.True(Directory.Exists(Path.Combine(instance.InstanceDirectory, "config")));
        Assert.True(Directory.Exists(Path.Combine(instance.InstanceDirectory, "shaderpacks")));
        Assert.Equal(3072, instance.MemoryMb);
    }

    [Fact]
    public async Task InstanceServiceQueuesConcurrentCreatesAndPersistsBothInstances()
    {
        var settingsService = new TestSettingsService(new LauncherSettings
        {
            DataDirectory = tempRoot,
            MinecraftDirectory = Path.Combine(tempRoot, ".minecraft")
        });

        var repository = new JsonGameInstanceRepository(settingsService);
        var allowInstall = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeLoaderProvider { WaitBeforeInstall = allowInstall.Task };
        var service = new GameInstanceService(settingsService, repository, [provider]);

        var firstCreate = service.CreateInstanceAsync("1.20.1", LoaderKind.Vanilla, null, "First", null);
        await WaitForAsync(() => provider.InstallCallCount == 1);

        var secondCreate = service.CreateInstanceAsync("1.20.2", LoaderKind.Vanilla, null, "Second", null);
        await Task.Delay(80);

        Assert.Equal(1, provider.InstallCallCount);

        allowInstall.SetResult(true);
        await Task.WhenAll(firstCreate, secondCreate);

        Assert.Equal(2, provider.InstallCallCount);
        var storedInstances = await repository.GetAllAsync();
        Assert.Equal(2, storedInstances.Count);
        Assert.Contains(storedInstances, instance => instance.Name == "First");
        Assert.Contains(storedInstances, instance => instance.Name == "Second");
    }

    [Fact]
    public async Task VanillaVersionIsolatorCopiesJsonAndJarToCustomVersion()
    {
        var minecraftDirectory = Path.Combine(tempRoot, ".minecraft");
        var sourceDirectory = Path.Combine(minecraftDirectory, "versions", "1.20.1");
        Directory.CreateDirectory(sourceDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(sourceDirectory, "1.20.1.json"),
            """
            {
              "id": "1.20.1",
              "type": "release",
              "mainClass": "net.minecraft.client.main.Main"
            }
            """);
        await File.WriteAllTextAsync(Path.Combine(sourceDirectory, "1.20.1.jar"), "fake jar");

        var versionName = await VanillaVersionIsolator.CreateIsolatedVersionAsync(
            "1.20.1",
            "My Vanilla",
            minecraftDirectory);

        var destinationDirectory = Path.Combine(minecraftDirectory, "versions", "My Vanilla");
        Assert.Equal("My Vanilla", versionName);
        Assert.True(File.Exists(Path.Combine(destinationDirectory, "My Vanilla.jar")));

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(destinationDirectory, "My Vanilla.json")));
        Assert.Equal("My Vanilla", json.RootElement.GetProperty("id").GetString());
        Assert.Equal("My Vanilla", json.RootElement.GetProperty("jar").GetString());
        Assert.Equal("release", json.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task InstanceServiceSyncsRecordsWithInstalledVersionFolders()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = tempRoot,
            MinecraftDirectory = Path.Combine(tempRoot, ".minecraft"),
            DefaultInstanceId = "missing"
        };
        var settingsService = new TestSettingsService(settings);

        var versionDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "1.21.5");
        Directory.CreateDirectory(versionDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(versionDirectory, "1.21.5.json"),
            """
            {
              "id": "1.21.5",
              "jar": "1.21.5"
            }
            """);
        await File.WriteAllTextAsync(Path.Combine(versionDirectory, "1.21.5.jar"), "fake jar");

        var repository = new JsonGameInstanceRepository(settingsService);
        await repository.SaveAllAsync(
        [
            new GameInstance
            {
                Id = "valid",
                Name = "Old Display Name",
                MinecraftVersion = "1.21.5",
                VersionName = "1.21.5",
                InstanceDirectory = Path.Combine(tempRoot, "instances", "old")
            },
            new GameInstance
            {
                Id = "duplicate",
                Name = "Duplicate",
                MinecraftVersion = "1.21.5",
                VersionName = "1.21.5",
                InstanceDirectory = Path.Combine(tempRoot, "instances", "duplicate")
            },
            new GameInstance
            {
                Id = "missing",
                Name = "Missing",
                MinecraftVersion = "1.20.1",
                VersionName = "1.20.1",
                InstanceDirectory = Path.Combine(tempRoot, "instances", "missing")
            }
        ]);

        var service = new GameInstanceService(settingsService, repository, [new FakeLoaderProvider()]);

        var instances = await service.GetInstancesAsync();

        var instance = Assert.Single(instances);
        Assert.Equal("valid", instance.Id);
        Assert.Equal("1.21.5", instance.Name);
        Assert.Equal(Path.Combine(settings.MinecraftDirectory, "versions", "1.21.5"), instance.InstanceDirectory);
        Assert.True(Directory.Exists(Path.Combine(instance.InstanceDirectory, "mods")));

        var storedInstances = await repository.GetAllAsync();
        Assert.Single(storedInstances);

        var syncedSettings = await settingsService.LoadAsync();
        Assert.Equal("valid", syncedSettings.DefaultInstanceId);
    }

    [Fact]
    public async Task InstanceServiceRemovesRecordWhenClientJarFailsVersionMetadataValidation()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = tempRoot,
            MinecraftDirectory = Path.Combine(tempRoot, ".minecraft"),
            DefaultInstanceId = "broken"
        };
        var settingsService = new TestSettingsService(settings);

        var versionDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "broken");
        Directory.CreateDirectory(versionDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(versionDirectory, "broken.json"),
            """
            {
              "id": "broken",
              "downloads": {
                "client": {
                  "sha1": "0000000000000000000000000000000000000000",
                  "size": 1024
                }
              }
            }
            """);
        await File.WriteAllTextAsync(Path.Combine(versionDirectory, "broken.jar"), "too small");

        var repository = new JsonGameInstanceRepository(settingsService);
        await repository.SaveAllAsync(
        [
            new GameInstance
            {
                Id = "broken",
                Name = "broken",
                MinecraftVersion = "broken",
                VersionName = "broken",
                InstanceDirectory = versionDirectory
            }
        ]);

        var service = new GameInstanceService(settingsService, repository, [new FakeLoaderProvider()]);

        var instances = await service.GetInstancesAsync();

        Assert.Empty(instances);
        Assert.Empty(await repository.GetAllAsync());
        Assert.Empty((await settingsService.LoadAsync()).DefaultInstanceId ?? string.Empty);
    }

    [Fact]
    public async Task ModServiceImportsDisablesAndEnablesJar()
    {
        var instanceDirectory = Path.Combine(tempRoot, "instances", "modded");
        Directory.CreateDirectory(instanceDirectory);
        var sourceJar = Path.Combine(tempRoot, "example.jar");
        Directory.CreateDirectory(tempRoot);
        await File.WriteAllTextAsync(sourceJar, "fake jar");

        var instance = new GameInstance { InstanceDirectory = instanceDirectory };
        var service = new ModService();

        var imported = await service.ImportAsync(instance, sourceJar);
        await service.SetEnabledAsync(imported, false);
        var disabled = (await service.GetModsAsync(instance)).Single();
        await service.SetEnabledAsync(disabled, true);
        var enabled = (await service.GetModsAsync(instance)).Single();

        Assert.True(enabled.IsEnabled);
        Assert.Equal("example.jar", enabled.FileName);
    }

    [Fact]
    public async Task ModrinthSearchAddsMinecraftVersionAndLoaderFacets()
    {
        var handler = new CaptureHandler("""
            {"hits":[{"project_id":"p1","slug":"sodium","title":"Sodium","description":"Fast","icon_url":null,"downloads":42}]}
            """);
        var service = new ModrinthService(new HttpClient(handler));

        var results = await service.SearchModsAsync("sodium", "1.20.1", LoaderKind.Fabric);

        Assert.Single(results);
        Assert.Contains("query=sodium", handler.LastRequest!.Query);
        Assert.Contains("versions%3A1.20.1", handler.LastRequest.Query);
        Assert.Contains("categories%3Afabric", handler.LastRequest.Query);
    }

    [Fact]
    public async Task DownloadPageShowsOnlyReleaseVersionsForReleaseCategory()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.21.4", "Release", false),
            new MinecraftVersionInfo("24w45a", "Snapshot", false),
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);
        var viewModel = CreateDownloadPageViewModel(service);

        await viewModel.EnsureVersionsLoadedAsync();

        Assert.True(viewModel.HasVisibleVersions);
        Assert.Equal(["1.21.4", "1.20.1"], viewModel.VisibleVersions.Select(version => version.Name));
        Assert.False(viewModel.HasVersionEmptyMessage);
    }

    [Fact]
    public async Task DownloadPageDoesNotRescanInstancesAfterVersionsAreLoaded()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.21.4", "Release", false)
        ]);
        var instanceService = new FakeGameInstanceService();
        var viewModel = CreateDownloadPageViewModel(service, instanceService);

        await viewModel.EnsureVersionsLoadedAsync();
        await viewModel.EnsureVersionsLoadedAsync();

        Assert.Equal(1, instanceService.GetInstancesCallCount);
    }

    [Fact]
    public async Task DownloadPageRefreshesInstanceNamesWhenEnteringInstanceOptions()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.Add(new GameInstance { Name = "1.20.1", VersionName = "1.20.1" });
        var viewModel = CreateDownloadPageViewModel(service, instanceService);

        await viewModel.EnsureVersionsLoadedAsync();
        instanceService.CreatedInstances.Clear();
        viewModel.SelectMinecraftVersionCommand.Execute(viewModel.VisibleVersions.Single());
        await viewModel.GoToInstanceOptionsCommand.ExecuteAsync(null);

        Assert.Equal(2, instanceService.GetInstancesCallCount);
        Assert.False(viewModel.HasInstanceNameDuplicateMessage);
        Assert.Empty(viewModel.InstanceNameDuplicateMessage);
        Assert.True(viewModel.InstallCommand.CanExecute(null));
    }

    [Fact]
    public async Task DownloadPageShowsPlaceholderForUnimplementedCategory()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.21.4", "Release", false)
        ]);
        var viewModel = CreateDownloadPageViewModel(service);

        await viewModel.EnsureVersionsLoadedAsync();
        viewModel.SelectVersionCategoryCommand.Execute(viewModel.VersionCategories.Single(category => category.Id == "old_beta"));

        Assert.Empty(viewModel.VisibleVersions);
        Assert.True(viewModel.HasVersionEmptyMessage);
        Assert.Contains("\u7a0d\u540e\u5b9e\u73b0", viewModel.VersionEmptyMessage);
    }

    [Fact]
    public async Task DownloadPageShowsOnlySnapshotVersionsForSnapshotCategory()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.21.4", "Release", false),
            new MinecraftVersionInfo("24w45a", "Snapshot", false, new DateTimeOffset(2024, 10, 30, 0, 0, 0, TimeSpan.Zero)),
            new MinecraftVersionInfo("24w44a", "Snapshot", false, new DateTimeOffset(2024, 11, 06, 0, 0, 0, TimeSpan.Zero))
        ]);
        var viewModel = CreateDownloadPageViewModel(service);

        await viewModel.EnsureVersionsLoadedAsync();
        viewModel.SelectVersionCategoryCommand.Execute(viewModel.VersionCategories.Single(category => category.Id == "snapshot"));

        Assert.True(viewModel.HasVisibleVersions);
        Assert.Equal(["24w44a", "24w45a"], viewModel.VisibleVersions.Select(version => version.Name));
        Assert.All(viewModel.VisibleVersions, version =>
        {
            Assert.True(version.IsSnapshot);
            Assert.Equal("\u5feb\u7167\u7248", version.TypeLabel);
            Assert.Equal("/Assets/Icons/block/dirt_block.png", version.IconSource);
        });
    }

    [Fact]
    public async Task DownloadPageExposesAllFilteredSnapshotVersions()
    {
        var snapshots = Enumerable
            .Range(0, 130)
            .Select(index => new MinecraftVersionInfo(
                $"24w{index:00}a",
                "Snapshot",
                false,
                new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(index)))
            .ToList();
        var viewModel = CreateDownloadPageViewModel(new FakeGameVersionService(snapshots));

        await viewModel.EnsureVersionsLoadedAsync();
        viewModel.SelectVersionCategoryCommand.Execute(viewModel.VersionCategories.Single(category => category.Id == "snapshot"));

        Assert.Equal(130, viewModel.VisibleVersions.Count);
        Assert.Equal("24w129a", viewModel.VisibleVersions.First().Name);
        Assert.Equal("24w00a", viewModel.VisibleVersions.Last().Name);
    }

    [Fact]
    public async Task DownloadPageSelectsVersionItem()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.21.4", "Release", false),
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);
        var viewModel = CreateDownloadPageViewModel(service);

        await viewModel.EnsureVersionsLoadedAsync();
        var version = viewModel.VisibleVersions.Last();
        viewModel.SelectMinecraftVersionCommand.Execute(version);

        Assert.Same(version, viewModel.SelectedMinecraftVersion);
        Assert.True(version.IsSelected);
        Assert.False(viewModel.VisibleVersions.First().IsSelected);
    }

    [Fact]
    public async Task DownloadPageSearchFiltersReleaseVersions()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.21.4", "Release", false),
            new MinecraftVersionInfo("1.20.6", "Release", false),
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);
        var viewModel = CreateDownloadPageViewModel(service);

        await viewModel.EnsureVersionsLoadedAsync();
        viewModel.VersionSearchQuery = "1.20";

        Assert.Equal(["1.20.6", "1.20.1"], viewModel.VisibleVersions.Select(version => version.Name));
    }

    [Fact]
    public async Task DownloadPageReselectsCurrentCategoryToRefreshContent()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.21.4", "Release", false),
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);
        var viewModel = CreateDownloadPageViewModel(service);

        await viewModel.EnsureVersionsLoadedAsync();
        viewModel.SelectMinecraftVersionCommand.Execute(viewModel.VisibleVersions.Last());
        await viewModel.GoToInstanceOptionsCommand.ExecuteAsync(null);
        var previousRefreshToken = viewModel.ContentRefreshToken;
        var previousEntranceAnimationToken = viewModel.ListEntranceAnimationToken;

        viewModel.SelectVersionCategoryCommand.Execute(viewModel.SelectedVersionCategory);

        Assert.Equal(DownloadPageStep.VersionList, viewModel.CurrentStep);
        Assert.True(viewModel.ContentRefreshToken > previousRefreshToken);
        Assert.Equal(previousEntranceAnimationToken, viewModel.ListEntranceAnimationToken);
        Assert.True(viewModel.HasVisibleVersions);
        Assert.True(viewModel.GoToInstanceOptionsCommand.CanExecute(null));
        Assert.False(viewModel.InstallCommand.CanExecute(null));
    }

    [Fact]
    public async Task DownloadPageRequestsListEntranceAnimationOnlyForInitialLoadAndCategorySwitch()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.21.4", "Release", false),
            new MinecraftVersionInfo("24w45a", "Snapshot", false)
        ]);
        var allowCreate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var instanceService = new FakeGameInstanceService { WaitBeforeCreate = allowCreate.Task };
        var viewModel = CreateDownloadPageViewModel(service, instanceService);

        Assert.Equal(0, viewModel.ListEntranceAnimationToken);

        await viewModel.EnsureVersionsLoadedAsync();

        Assert.Equal(1, viewModel.ListEntranceAnimationToken);

        viewModel.SelectVersionCategoryCommand.Execute(viewModel.VersionCategories.Single(category => category.Id == "snapshot"));

        Assert.Equal(2, viewModel.ListEntranceAnimationToken);

        viewModel.SelectMinecraftVersionCommand.Execute(viewModel.VisibleVersions.Single());
        await viewModel.GoToInstanceOptionsCommand.ExecuteAsync(null);
        var installTask = viewModel.InstallCommand.ExecuteAsync(null);
        await instanceService.CreateStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(DownloadPageStep.VersionList, viewModel.CurrentStep);
        Assert.Equal(2, viewModel.ListEntranceAnimationToken);

        allowCreate.SetResult(true);
        await installTask;

        Assert.Equal(2, viewModel.ListEntranceAnimationToken);
    }

    [Fact]
    public async Task DownloadPageCannotEnterInstanceOptionsWithoutSelectedVersion()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);
        var viewModel = CreateDownloadPageViewModel(service);

        await viewModel.EnsureVersionsLoadedAsync();

        Assert.Equal(DownloadPageStep.VersionList, viewModel.CurrentStep);
        Assert.True(viewModel.IsVersionListStep);
        Assert.False(viewModel.GoToInstanceOptionsCommand.CanExecute(null));
    }

    [Fact]
    public async Task DownloadPageEntersInstanceOptionsWithSelectedReleaseDefaults()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);
        var viewModel = CreateDownloadPageViewModel(service);

        await viewModel.EnsureVersionsLoadedAsync();
        var version = viewModel.VisibleVersions.Single();
        viewModel.SelectMinecraftVersionCommand.Execute(version);
        viewModel.GoToInstanceOptionsCommand.Execute(null);

        Assert.Equal(DownloadPageStep.InstanceOptions, viewModel.CurrentStep);
        Assert.True(viewModel.IsInstanceOptionsStep);
        Assert.Equal("1.20.1", viewModel.InstanceName);
        Assert.Equal("1.20.1", viewModel.PageTitle);
        Assert.Equal("/Assets/Icons/block/grass_block.png", viewModel.PageTitleIconSource);
        Assert.Equal([LoaderKind.Vanilla, LoaderKind.Fabric, LoaderKind.Forge], viewModel.LoaderOptions.Select(option => option.Kind));
        Assert.Equal(LoaderKind.Vanilla, viewModel.SelectedLoaderOption?.Kind);
        Assert.True(viewModel.LoaderOptions.Single(option => option.Kind == LoaderKind.Vanilla).IsSelected);
        Assert.Equal("/Assets/Icons/block/grass_block.png", viewModel.LoaderOptions.Single(option => option.Kind == LoaderKind.Vanilla).IconSource);
    }

    [Fact]
    public async Task DownloadPageShowsDuplicateMessageForExistingInstanceName()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.Add(new GameInstance { Name = "Existing Display Name", VersionName = "1.20.1" });
        var viewModel = CreateDownloadPageViewModel(service, instanceService);

        await viewModel.EnsureVersionsLoadedAsync();
        viewModel.SelectMinecraftVersionCommand.Execute(viewModel.VisibleVersions.Single());
        viewModel.GoToInstanceOptionsCommand.Execute(null);

        Assert.True(viewModel.HasInstanceNameDuplicateMessage);
        Assert.Equal("\u5df2\u5b58\u5728\u540c\u540d\u7248\u672c", viewModel.InstanceNameDuplicateMessage);
        Assert.False(viewModel.InstallCommand.CanExecute(null));

        viewModel.InstanceName = "1.20.1 Copy";

        Assert.False(viewModel.HasInstanceNameDuplicateMessage);
        Assert.Empty(viewModel.InstanceNameDuplicateMessage);
        Assert.True(viewModel.InstallCommand.CanExecute(null));
    }

    [Fact]
    public async Task DownloadPageUsesSnapshotIconForInstanceOptionsTitle()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.1", "Release", false),
            new MinecraftVersionInfo("24w44a", "Snapshot", false)
        ]);
        var viewModel = CreateDownloadPageViewModel(service);

        await viewModel.EnsureVersionsLoadedAsync();
        viewModel.SelectVersionCategoryCommand.Execute(viewModel.VersionCategories.Single(category => category.Id == "snapshot"));
        var version = viewModel.VisibleVersions.Single();
        viewModel.SelectMinecraftVersionCommand.Execute(version);
        viewModel.GoToInstanceOptionsCommand.Execute(null);

        Assert.Equal("24w44a", viewModel.PageTitle);
        Assert.Equal("/Assets/Icons/block/dirt_block.png", viewModel.PageTitleIconSource);
    }

    [Fact]
    public async Task DownloadPageSelectsLoaderOption()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);
        var viewModel = CreateDownloadPageViewModel(service);

        await viewModel.EnsureVersionsLoadedAsync();
        viewModel.SelectMinecraftVersionCommand.Execute(viewModel.VisibleVersions.Single());
        viewModel.GoToInstanceOptionsCommand.Execute(null);
        var fabric = viewModel.LoaderOptions.Single(option => option.Kind == LoaderKind.Fabric);
        viewModel.SelectLoaderOptionCommand.Execute(fabric);

        Assert.Same(fabric, viewModel.SelectedLoaderOption);
        Assert.True(fabric.IsSelected);
        Assert.False(viewModel.LoaderOptions.Single(option => option.Kind == LoaderKind.Vanilla).IsSelected);
    }

    [Fact]
    public async Task DownloadPageBackToVersionListKeepsSelectedVersion()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);
        var viewModel = CreateDownloadPageViewModel(service);

        await viewModel.EnsureVersionsLoadedAsync();
        var version = viewModel.VisibleVersions.Single();
        viewModel.SelectMinecraftVersionCommand.Execute(version);
        viewModel.GoToInstanceOptionsCommand.Execute(null);
        viewModel.BackToVersionListCommand.Execute(null);

        Assert.Equal(DownloadPageStep.VersionList, viewModel.CurrentStep);
        Assert.Same(version, viewModel.SelectedMinecraftVersion);
        Assert.True(version.IsSelected);
        Assert.Equal("\u6b63\u5f0f\u7248", viewModel.PageTitle);
        Assert.Null(viewModel.PageTitleIconSource);
    }

    [Fact]
    public async Task DownloadPageInstallCommandRequiresSelectedVanillaVersion()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);
        var viewModel = CreateDownloadPageViewModel(service);

        await viewModel.EnsureVersionsLoadedAsync();

        Assert.False(viewModel.InstallCommand.CanExecute(null));

        viewModel.SelectMinecraftVersionCommand.Execute(viewModel.VisibleVersions.Single());
        viewModel.GoToInstanceOptionsCommand.Execute(null);

        Assert.True(viewModel.InstallCommand.CanExecute(null));

        viewModel.IsInstalling = true;
        Assert.True(viewModel.InstallCommand.CanExecute(null));

        viewModel.IsInstalling = false;
        var fabric = viewModel.LoaderOptions.Single(option => option.Kind == LoaderKind.Fabric);
        viewModel.SelectLoaderOptionCommand.Execute(fabric);

        Assert.False(viewModel.InstallCommand.CanExecute(null));
        Assert.True(viewModel.HasInstallStatus);
    }

    [Fact]
    public async Task DownloadPageInstallCreatesVanillaInstance()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);
        var instanceService = new FakeGameInstanceService();
        var tasksPage = new DownloadTasksPageViewModel();
        var viewModel = CreateDownloadPageViewModel(service, instanceService, tasksPage);
        GameInstance? installedInstance = null;
        viewModel.InstanceInstalled += (_, instance) => installedInstance = instance;

        await viewModel.EnsureVersionsLoadedAsync();
        viewModel.SelectMinecraftVersionCommand.Execute(viewModel.VisibleVersions.Single());
        viewModel.GoToInstanceOptionsCommand.Execute(null);
        viewModel.InstanceName = "My Vanilla";
        await viewModel.InstallCommand.ExecuteAsync(null);

        Assert.Equal("1.20.1", instanceService.LastMinecraftVersion);
        Assert.Equal(LoaderKind.Vanilla, instanceService.LastLoader);
        Assert.Null(instanceService.LastLoaderVersion);
        Assert.Equal("My Vanilla", instanceService.LastName);
        Assert.False(viewModel.IsInstalling);
        Assert.False(viewModel.HasInstallError);
        Assert.Equal(100, viewModel.InstallProgressPercent);
        Assert.Same(instanceService.CreatedInstances.Single(), installedInstance);
        Assert.Contains("\u5df2\u5b89\u88c5", viewModel.InstallStatusMessage);

        var task = Assert.Single(tasksPage.Tasks);
        Assert.True(tasksPage.HasTasks);
        Assert.Equal(DownloadTaskState.Completed, task.State);
        Assert.Equal(100, task.ProgressPercent);
        Assert.Contains("1.20.1", task.Title);
        Assert.Equal(DownloadPageStep.VersionList, viewModel.CurrentStep);
    }

    [Fact]
    public async Task DownloadPageInstallReturnsToVersionListWhenTaskStarts()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);
        var allowCreate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var instanceService = new FakeGameInstanceService { WaitBeforeCreate = allowCreate.Task };
        var viewModel = CreateDownloadPageViewModel(service, instanceService);

        await viewModel.EnsureVersionsLoadedAsync();
        viewModel.SelectMinecraftVersionCommand.Execute(viewModel.VisibleVersions.Single());
        viewModel.GoToInstanceOptionsCommand.Execute(null);

        var installTask = viewModel.InstallCommand.ExecuteAsync(null);
        await instanceService.CreateStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(DownloadPageStep.VersionList, viewModel.CurrentStep);
        Assert.True(viewModel.HasVisibleVersions);
        Assert.True(viewModel.GoToInstanceOptionsCommand.CanExecute(null));
        Assert.False(viewModel.InstallCommand.CanExecute(null));

        allowCreate.SetResult(true);
        await installTask;
    }

    [Fact]
    public async Task DownloadPageAllowsConcurrentInstalls()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.1", "Release", false),
            new MinecraftVersionInfo("1.20.2", "Release", false)
        ]);
        var allowCreate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var instanceService = new FakeGameInstanceService { WaitBeforeCreate = allowCreate.Task };
        var tasksPage = new DownloadTasksPageViewModel();
        var viewModel = CreateDownloadPageViewModel(service, instanceService, tasksPage);

        await viewModel.EnsureVersionsLoadedAsync();

        viewModel.SelectMinecraftVersionCommand.Execute(viewModel.VisibleVersions[0]);
        viewModel.GoToInstanceOptionsCommand.Execute(null);
        viewModel.InstanceName = "First Install";
        var firstInstall = viewModel.InstallCommand.ExecuteAsync(null);
        await WaitForAsync(() => instanceService.CreateCallCount == 1);

        viewModel.SelectMinecraftVersionCommand.Execute(viewModel.VisibleVersions[1]);
        viewModel.GoToInstanceOptionsCommand.Execute(null);
        viewModel.InstanceName = "Second Install";

        Assert.True(viewModel.InstallCommand.CanExecute(null));

        var secondInstall = viewModel.InstallCommand.ExecuteAsync(null);
        await WaitForAsync(() => instanceService.CreateCallCount == 2);

        Assert.True(viewModel.IsInstalling);
        Assert.Equal(2, tasksPage.Tasks.Count);
        Assert.All(tasksPage.Tasks, task => Assert.Equal(DownloadTaskState.Running, task.State));

        allowCreate.SetResult(true);
        await Task.WhenAll(firstInstall, secondInstall);

        Assert.False(viewModel.IsInstalling);
        Assert.Equal(2, instanceService.CreatedInstances.Count);
        Assert.Contains(instanceService.CreatedInstances, instance => instance.Name == "First Install");
        Assert.Contains(instanceService.CreatedInstances, instance => instance.Name == "Second Install");
        Assert.All(tasksPage.Tasks, task => Assert.Equal(DownloadTaskState.Completed, task.State));
    }

    [Fact]
    public async Task DownloadPageInstallShowsErrorAndPropagatesFailure()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);
        var expected = new InvalidOperationException("network down");
        var instanceService = new FakeGameInstanceService { CreateException = expected };
        var viewModel = CreateDownloadPageViewModel(service, instanceService);

        await viewModel.EnsureVersionsLoadedAsync();
        viewModel.SelectMinecraftVersionCommand.Execute(viewModel.VisibleVersions.Single());
        viewModel.GoToInstanceOptionsCommand.Execute(null);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => viewModel.InstallCommand.ExecuteAsync(null));

        Assert.Same(expected, actual);
        Assert.False(viewModel.IsInstalling);
        Assert.True(viewModel.HasInstallError);
        Assert.Contains("network down", viewModel.InstallError);
    }

    [Fact]
    public void DownloadTasksPageStartsEmpty()
    {
        var viewModel = new DownloadTasksPageViewModel();

        Assert.False(viewModel.HasTasks);
        Assert.Empty(viewModel.Tasks);
    }

    [Fact]
    public void DownloadTasksPageRaisesTaskStartedWhenTaskBegins()
    {
        var viewModel = new DownloadTasksPageViewModel();
        DownloadTaskItem? startedTask = null;

        viewModel.TaskStarted += (_, task) => startedTask = task;

        var task = viewModel.BeginTask("Vanilla 1.21.5", "1.21.5");

        Assert.Same(task, startedTask);
    }

    [Fact]
    public async Task DownloadTasksPageRemovesCompletedTasksAfterRetention()
    {
        var viewModel = new DownloadTasksPageViewModel(TimeSpan.FromMilliseconds(10));
        var task = viewModel.BeginTask("原版 1.21.5", "1.21.5");

        task.Complete("安装完成");

        await WaitForAsync(() => viewModel.Tasks.Count == 0);
        Assert.False(viewModel.HasTasks);
    }

    [Fact]
    public async Task DownloadTasksPageKeepsFailedTasks()
    {
        var viewModel = new DownloadTasksPageViewModel(TimeSpan.FromMilliseconds(10));
        var task = viewModel.BeginTask("原版 1.21.5", "1.21.5");

        task.Fail("安装失败");
        await Task.Delay(50);

        Assert.Single(viewModel.Tasks);
        Assert.True(viewModel.HasTasks);
    }

    [Fact]
    public void DownloadTaskShowsAndClearsDownloadSpeed()
    {
        var task = new DownloadTaskItem("\u539f\u7248 1.21.5", "1.21.5");

        task.Report(new LauncherProgress("Bytes", "\u6b63\u5728\u4e0b\u8f7d\u6e38\u620f\u6587\u4ef6", 42, "1.2 MB/s"));

        Assert.True(task.HasDownloadSpeedText);
        Assert.Equal("1.2 MB/s", task.DownloadSpeedText);

        task.Complete("\u5b89\u88c5\u5b8c\u6210");

        Assert.False(task.HasDownloadSpeedText);
        Assert.Empty(task.DownloadSpeedText);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, recursive: true);
    }

    private static DownloadPageViewModel CreateDownloadPageViewModel(
        IGameVersionService gameVersionService,
        FakeGameInstanceService? instanceService = null,
        DownloadTasksPageViewModel? tasksPage = null)
    {
        return new DownloadPageViewModel(
            gameVersionService,
            instanceService ?? new FakeGameInstanceService(),
            tasksPage ?? new DownloadTasksPageViewModel());
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (!condition() && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(10);
    }

    private sealed class TestSettingsService : ISettingsService
    {
        private LauncherSettings settings;

        public TestSettingsService(LauncherSettings settings)
        {
            this.settings = settings;
        }

        public Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(settings);
        }

        public Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken = default)
        {
            this.settings = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeLoaderProvider : ILoaderProvider
    {
        private int installCallCount;

        public LoaderKind Kind => LoaderKind.Vanilla;
        public string DisplayName => "Fake Vanilla";
        public bool IsImplemented => true;
        public string? LastGameDirectory { get; private set; }
        public string? LastIsolatedVersionName { get; private set; }
        public Task? WaitBeforeInstall { get; init; }
        public int InstallCallCount => installCallCount;

        public Task<IReadOnlyList<LoaderVersionInfo>> GetLoaderVersionsAsync(string minecraftVersion, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<LoaderVersionInfo> versions = [new LoaderVersionInfo("fake")];
            return Task.FromResult(versions);
        }

        public async Task<string> InstallAsync(string minecraftVersion, string gameDirectory, string isolatedVersionName, string? loaderVersion, IProgress<LauncherProgress>? progress, CancellationToken cancellationToken = default)
        {
            LastGameDirectory = gameDirectory;
            LastIsolatedVersionName = isolatedVersionName;
            Interlocked.Increment(ref installCallCount);

            if (WaitBeforeInstall is not null)
                await WaitBeforeInstall;

            var versionDirectory = Path.Combine(gameDirectory, "versions", isolatedVersionName);
            Directory.CreateDirectory(versionDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(versionDirectory, $"{isolatedVersionName}.json"),
                $$"""
                {
                  "id": "{{isolatedVersionName}}",
                  "jar": "{{isolatedVersionName}}"
                }
                """,
                cancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(versionDirectory, $"{isolatedVersionName}.jar"),
                "fake jar",
                cancellationToken);
            return isolatedVersionName;
        }
    }

    private sealed class FakeGameInstanceService : IGameInstanceService
    {
        private readonly object syncRoot = new();
        public List<GameInstance> CreatedInstances { get; } = [];
        public Exception? CreateException { get; init; }
        public TaskCompletionSource<bool> CreateStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task? WaitBeforeCreate { get; init; }
        public string? LastMinecraftVersion { get; private set; }
        public LoaderKind LastLoader { get; private set; }
        public string? LastLoaderVersion { get; private set; }
        public string? LastName { get; private set; }
        public int CreateCallCount { get; private set; }
        public int GetInstancesCallCount { get; private set; }

        public Task<IReadOnlyList<GameInstance>> GetInstancesAsync(CancellationToken cancellationToken = default)
        {
            lock (syncRoot)
            {
                GetInstancesCallCount++;
                return Task.FromResult<IReadOnlyList<GameInstance>>(CreatedInstances.ToList());
            }
        }

        public Task<GameInstance?> GetDefaultInstanceAsync(CancellationToken cancellationToken = default)
        {
            lock (syncRoot)
            {
                return Task.FromResult<GameInstance?>(CreatedInstances.FirstOrDefault());
            }
        }

        public async Task<GameInstance> CreateInstanceAsync(
            string minecraftVersion,
            LoaderKind loader,
            string? loaderVersion,
            string? name,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default)
        {
            LastMinecraftVersion = minecraftVersion;
            LastLoader = loader;
            LastLoaderVersion = loaderVersion;
            LastName = name;
            progress?.Report(new LauncherProgress("Install", "Downloading", 25));
            CreateStarted.TrySetResult(true);
            lock (syncRoot)
            {
                CreateCallCount++;
            }

            if (WaitBeforeCreate is not null)
                await WaitBeforeCreate;

            if (CreateException is not null)
                throw CreateException;

            var instance = new GameInstance
            {
                Name = string.IsNullOrWhiteSpace(name) ? minecraftVersion : name,
                MinecraftVersion = minecraftVersion,
                Loader = loader,
                LoaderVersion = loaderVersion,
                VersionName = minecraftVersion,
                InstanceDirectory = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"))
            };
            lock (syncRoot)
            {
                CreatedInstances.Add(instance);
            }
            return instance;
        }

        public Task SaveInstanceAsync(GameInstance instance, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeGameVersionService : IGameVersionService
    {
        private readonly IReadOnlyList<MinecraftVersionInfo> versions;

        public FakeGameVersionService(IReadOnlyList<MinecraftVersionInfo> versions)
        {
            this.versions = versions;
        }

        public Task<IReadOnlyList<MinecraftVersionInfo>> GetVersionsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(versions);
        }
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly string responseBody;

        public CaptureHandler(string responseBody)
        {
            this.responseBody = responseBody;
        }

        public Uri? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request.RequestUri;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody)
            };
            return Task.FromResult(response);
        }
    }
}
