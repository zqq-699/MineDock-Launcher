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
