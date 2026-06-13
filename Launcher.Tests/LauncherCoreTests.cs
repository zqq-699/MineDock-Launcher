using System.Net;
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
