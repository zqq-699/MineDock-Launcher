using System.Net;
using Launcher.App.ViewModels;
using Launcher.Core.Models;
using Launcher.Core.Services;

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
        var settingsService = new JsonSettingsService(tempRoot);
        var settings = await settingsService.LoadAsync();
        settings.DefaultMemoryMb = 3072;
        await settingsService.SaveAsync(settings);

        var service = new GameInstanceService(settingsService, [new FakeLoaderProvider()]);
        var instance = await service.CreateInstanceAsync("1.20.1", LoaderKind.Vanilla, null, "Test Instance", null);

        Assert.Equal("1.20.1", instance.VersionName);
        Assert.True(Directory.Exists(Path.Combine(instance.InstanceDirectory, "mods")));
        Assert.True(Directory.Exists(Path.Combine(instance.InstanceDirectory, "config")));
        Assert.True(Directory.Exists(Path.Combine(instance.InstanceDirectory, "shaderpacks")));
        Assert.Equal(3072, instance.MemoryMb);
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
        var viewModel = new DownloadPageViewModel(service);

        await viewModel.EnsureVersionsLoadedAsync();

        Assert.True(viewModel.HasVisibleVersions);
        Assert.Equal(["1.21.4", "1.20.1"], viewModel.VisibleVersions.Select(version => version.Name));
        Assert.False(viewModel.HasVersionEmptyMessage);
    }

    [Fact]
    public async Task DownloadPageShowsPlaceholderForUnimplementedCategory()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.21.4", "Release", false)
        ]);
        var viewModel = new DownloadPageViewModel(service);

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
        var viewModel = new DownloadPageViewModel(service);

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
        var viewModel = new DownloadPageViewModel(new FakeGameVersionService(snapshots));

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
        var viewModel = new DownloadPageViewModel(service);

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
        var viewModel = new DownloadPageViewModel(service);

        await viewModel.EnsureVersionsLoadedAsync();
        viewModel.VersionSearchQuery = "1.20";

        Assert.Equal(["1.20.6", "1.20.1"], viewModel.VisibleVersions.Select(version => version.Name));
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, recursive: true);
    }

    private sealed class FakeLoaderProvider : ILoaderProvider
    {
        public LoaderKind Kind => LoaderKind.Vanilla;
        public string DisplayName => "Fake Vanilla";
        public bool IsImplemented => true;

        public Task<IReadOnlyList<LoaderVersionInfo>> GetLoaderVersionsAsync(string minecraftVersion, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<LoaderVersionInfo> versions = [new LoaderVersionInfo("fake")];
            return Task.FromResult(versions);
        }

        public Task<string> InstallAsync(string minecraftVersion, string gameDirectory, string? loaderVersion, IProgress<LauncherProgress>? progress, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(gameDirectory);
            return Task.FromResult(minecraftVersion);
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
