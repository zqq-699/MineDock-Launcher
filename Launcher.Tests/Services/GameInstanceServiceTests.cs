using System.Text.Json;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;
using Launcher.Infrastructure.Persistence;

namespace Launcher.Tests.Services;

public sealed class GameInstanceServiceTests : TestTempDirectory
{
    [Fact]
    public async Task InstanceServiceCreatesIsolatedDirectoriesWithProvider()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft"),
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
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        });

        var repository = new JsonGameInstanceRepository(settingsService);
        var allowInstall = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeLoaderProvider { WaitBeforeInstall = allowInstall.Task };
        var service = new GameInstanceService(settingsService, repository, [provider]);

        var firstCreate = service.CreateInstanceAsync("1.20.1", LoaderKind.Vanilla, null, "First", null);
        await TestAsync.WaitForAsync(() => provider.InstallCallCount == 1);

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
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
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
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft"),
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
                InstanceDirectory = Path.Combine(TempRoot, "instances", "old")
            },
            new GameInstance
            {
                Id = "duplicate",
                Name = "Duplicate",
                MinecraftVersion = "1.21.5",
                VersionName = "1.21.5",
                InstanceDirectory = Path.Combine(TempRoot, "instances", "duplicate")
            },
            new GameInstance
            {
                Id = "missing",
                Name = "Missing",
                MinecraftVersion = "1.20.1",
                VersionName = "1.20.1",
                InstanceDirectory = Path.Combine(TempRoot, "instances", "missing")
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
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft"),
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
    public async Task InstanceServiceSavesExistingDefaultInstanceSelection()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft"),
            DefaultInstanceId = "first"
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        await CreateInstalledVersionAsync(settings.MinecraftDirectory, "first");
        await CreateInstalledVersionAsync(settings.MinecraftDirectory, "second");
        await repository.SaveAllAsync(
        [
            CreateStoredInstance("first"),
            CreateStoredInstance("second")
        ]);
        var service = new GameInstanceService(settingsService, repository, [new FakeLoaderProvider()]);

        var saved = await service.SetDefaultInstanceAsync("second");

        Assert.True(saved);
        Assert.Equal("second", (await settingsService.LoadAsync()).DefaultInstanceId);
    }

    [Fact]
    public async Task InstanceServiceDoesNotSaveMissingDefaultInstanceSelection()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft"),
            DefaultInstanceId = "first"
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        await CreateInstalledVersionAsync(settings.MinecraftDirectory, "first");
        await repository.SaveAllAsync([CreateStoredInstance("first")]);
        var service = new GameInstanceService(settingsService, repository, [new FakeLoaderProvider()]);

        var saved = await service.SetDefaultInstanceAsync("missing");

        Assert.False(saved);
        Assert.Equal("first", (await settingsService.LoadAsync()).DefaultInstanceId);
    }

    [Fact]
    public async Task InstanceServiceReturnsNewlySelectedDefaultInstance()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft"),
            DefaultInstanceId = "first"
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        await CreateInstalledVersionAsync(settings.MinecraftDirectory, "first");
        await CreateInstalledVersionAsync(settings.MinecraftDirectory, "second");
        await repository.SaveAllAsync(
        [
            CreateStoredInstance("first"),
            CreateStoredInstance("second")
        ]);
        var service = new GameInstanceService(settingsService, repository, [new FakeLoaderProvider()]);

        await service.SetDefaultInstanceAsync("second");

        var selected = await service.GetDefaultInstanceAsync();
        Assert.NotNull(selected);
        Assert.Equal("second", selected.Id);
    }

    private static async Task CreateInstalledVersionAsync(string minecraftDirectory, string versionName)
    {
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", versionName);
        Directory.CreateDirectory(versionDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(versionDirectory, $"{versionName}.json"),
            $$"""
            {
              "id": "{{versionName}}",
              "jar": "{{versionName}}"
            }
            """);
        await File.WriteAllTextAsync(Path.Combine(versionDirectory, $"{versionName}.jar"), "fake jar");
    }

    private static GameInstance CreateStoredInstance(string versionName)
    {
        return new GameInstance
        {
            Id = versionName,
            Name = versionName,
            MinecraftVersion = versionName,
            VersionName = versionName,
            InstanceDirectory = string.Empty
        };
    }

}

