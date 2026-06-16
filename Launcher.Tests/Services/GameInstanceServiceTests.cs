using System.Text.Json;
using System.Text.Json.Nodes;
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
    public async Task InstanceServiceRemovesNewlyCreatedVersionDirectoryWhenCreateIsCanceled()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        var waitBeforeInstall = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeLoaderProvider
        {
            WriteJsonBeforeWaiting = true,
            PartialVersionName = "1.20.1",
            WaitBeforeInstall = waitBeforeInstall.Task
        };
        var service = new GameInstanceService(settingsService, repository, [provider]);
        using var cancellation = new CancellationTokenSource();

        var createTask = service.CreateInstanceAsync("1.20.1", LoaderKind.Vanilla, null, "Custom Name", null, cancellation.Token);
        await provider.InstallStarted.Task;
        var versionDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "1.20.1");
        await TestAsync.WaitForAsync(() => File.Exists(Path.Combine(versionDirectory, "1.20.1.json")));

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => createTask);
        Assert.False(Directory.Exists(versionDirectory));
        Assert.Empty(await repository.GetAllAsync());
    }

    [Fact]
    public async Task InstanceServiceKeepsExistingVersionDirectoryWhenCanceled()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        var versionDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "1.20.1");
        await CreateInstalledVersionAsync(settings.MinecraftDirectory, "1.20.1");

        var waitBeforeInstall = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeLoaderProvider
        {
            WriteJsonBeforeWaiting = true,
            PartialVersionName = "1.20.1",
            WaitBeforeInstall = waitBeforeInstall.Task
        };
        var service = new GameInstanceService(settingsService, repository, [provider]);
        using var cancellation = new CancellationTokenSource();

        var createTask = service.CreateInstanceAsync("1.20.1", LoaderKind.Vanilla, null, "Custom Name", null, cancellation.Token);
        await provider.InstallStarted.Task;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => createTask);
        Assert.True(Directory.Exists(versionDirectory));
        Assert.True(File.Exists(Path.Combine(versionDirectory, "1.20.1.jar")));
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
    public async Task InstanceServiceDiscoversImportedVersionFoldersAndPersistsRecords()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        await CreateInstalledVersionAsync(settings.MinecraftDirectory, "External Pack", type: "release", writeJar: false);
        var service = new GameInstanceService(settingsService, repository, [new FakeLoaderProvider()]);

        var instances = await service.GetInstancesAsync();

        var instance = Assert.Single(instances);
        Assert.StartsWith("local-", instance.Id);
        Assert.Equal("External Pack", instance.Name);
        Assert.Equal("External Pack", instance.MinecraftVersion);
        Assert.Equal("External Pack", instance.VersionName);
        Assert.Equal("release", instance.VersionType);
        Assert.Equal(LoaderKind.Vanilla, instance.Loader);
        Assert.Equal(Path.Combine(settings.MinecraftDirectory, "versions", "External Pack"), instance.InstanceDirectory);

        var stored = Assert.Single(await repository.GetAllAsync());
        Assert.Equal(instance.Id, stored.Id);
        Assert.Equal("External Pack", stored.VersionName);
    }

    [Fact]
    public async Task InstanceServiceDiscoversLoaderAndVersionTypeMetadata()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        await CreateInstalledVersionAsync(
            settings.MinecraftDirectory,
            "fabric-loader-0.16.9-1.21.4",
            type: "snapshot",
            inheritsFrom: "1.21.4",
            libraries: ["net.fabricmc:fabric-loader:0.16.9"],
            writeJar: false);
        await CreateInstalledVersionAsync(
            settings.MinecraftDirectory,
            "forge-1.20.1-47.2.0",
            type: "release",
            inheritsFrom: "1.20.1",
            libraries: ["net.minecraftforge:forge:1.20.1-47.2.0"],
            writeJar: false);
        await CreateInstalledVersionAsync(
            settings.MinecraftDirectory,
            "neoforge-20.4.237",
            type: "release",
            inheritsFrom: "1.20.4",
            libraries: ["net.neoforged:neoforge:20.4.237"],
            writeJar: false);
        await CreateInstalledVersionAsync(
            settings.MinecraftDirectory,
            "quilt-loader-0.26.0-1.20.1",
            type: "release",
            inheritsFrom: "1.20.1",
            libraries: ["org.quiltmc:quilt-loader:0.26.0"],
            writeJar: false);
        var service = new GameInstanceService(settingsService, repository, [new FakeLoaderProvider()]);

        var instances = (await service.GetInstancesAsync()).ToDictionary(instance => instance.VersionName);

        Assert.Equal(LoaderKind.Fabric, instances["fabric-loader-0.16.9-1.21.4"].Loader);
        Assert.Equal("0.16.9", instances["fabric-loader-0.16.9-1.21.4"].LoaderVersion);
        Assert.Equal("snapshot", instances["fabric-loader-0.16.9-1.21.4"].VersionType);
        Assert.Equal("1.21.4", instances["fabric-loader-0.16.9-1.21.4"].MinecraftVersion);
        Assert.Equal(LoaderKind.Forge, instances["forge-1.20.1-47.2.0"].Loader);
        Assert.Equal(LoaderKind.NeoForge, instances["neoforge-20.4.237"].Loader);
        Assert.Equal(LoaderKind.Quilt, instances["quilt-loader-0.26.0-1.20.1"].Loader);
    }

    [Fact]
    public async Task InstanceServiceInheritsVersionTypeFromParentVersion()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        await CreateInstalledVersionAsync(settings.MinecraftDirectory, "24w14a", type: "snapshot", writeJar: false);
        await CreateInstalledVersionAsync(
            settings.MinecraftDirectory,
            "fabric-child",
            inheritsFrom: "24w14a",
            libraries: ["net.fabricmc:fabric-loader:0.16.9"],
            writeJar: false);
        var service = new GameInstanceService(settingsService, repository, [new FakeLoaderProvider()]);

        var instances = await service.GetInstancesAsync();

        var child = instances.Single(instance => instance.VersionName == "fabric-child");
        Assert.Equal("24w14a", child.MinecraftVersion);
        Assert.Equal("snapshot", child.VersionType);
        Assert.Equal(LoaderKind.Fabric, child.Loader);
    }

    [Fact]
    public async Task InstanceServiceKeepsRecordWhenJsonIsReadableEvenIfClientJarFailsValidation()
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

        var instance = Assert.Single(instances);
        Assert.Equal("broken", instance.Id);
        Assert.Single(await repository.GetAllAsync());
        Assert.Equal("broken", (await settingsService.LoadAsync()).DefaultInstanceId);
    }

    [Fact]
    public async Task InstanceServiceRemovesRecordsWhenVersionJsonIsMissingOrInvalid()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft"),
            DefaultInstanceId = "missing-json"
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        Directory.CreateDirectory(Path.Combine(settings.MinecraftDirectory, "versions", "missing-json"));
        var invalidDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "invalid-json");
        Directory.CreateDirectory(invalidDirectory);
        await File.WriteAllTextAsync(Path.Combine(invalidDirectory, "invalid-json.json"), "{ broken json");
        await repository.SaveAllAsync(
        [
            CreateStoredInstance("missing-json"),
            CreateStoredInstance("invalid-json")
        ]);
        var service = new GameInstanceService(settingsService, repository, [new FakeLoaderProvider()]);

        var instances = await service.GetInstancesAsync();

        Assert.Empty(instances);
        Assert.Empty(await repository.GetAllAsync());
        Assert.Equal(string.Empty, (await settingsService.LoadAsync()).DefaultInstanceId);
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

    [Fact]
    public async Task DeleteInstanceRemovesRecordAndVersionDirectory()
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

        var deleted = await service.DeleteInstanceAsync("first");

        Assert.True(deleted);
        Assert.Empty(await repository.GetAllAsync());
        Assert.False(Directory.Exists(Path.Combine(settings.MinecraftDirectory, "versions", "first")));
    }

    [Fact]
    public async Task DeleteInstanceFallsBackToRemainingDefaultInstance()
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

        var deleted = await service.DeleteInstanceAsync("first");

        Assert.True(deleted);
        Assert.Equal("second", (await settingsService.LoadAsync()).DefaultInstanceId);
        Assert.Single(await repository.GetAllAsync());
    }

    [Fact]
    public async Task DeleteInstanceClearsDefaultWhenNoInstancesRemain()
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

        var deleted = await service.DeleteInstanceAsync("first");

        Assert.True(deleted);
        Assert.Equal(string.Empty, (await settingsService.LoadAsync()).DefaultInstanceId);
    }

    [Fact]
    public async Task DeleteInstanceReturnsFalseWhenInstanceIsMissing()
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

        var deleted = await service.DeleteInstanceAsync("missing");

        Assert.False(deleted);
        Assert.Single(await repository.GetAllAsync());
        Assert.True(Directory.Exists(Path.Combine(settings.MinecraftDirectory, "versions", "first")));
    }

    private static async Task CreateInstalledVersionAsync(
        string minecraftDirectory,
        string versionName,
        string? type = null,
        string? inheritsFrom = null,
        string? jar = null,
        IReadOnlyList<string>? libraries = null,
        bool writeJar = true)
    {
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", versionName);
        Directory.CreateDirectory(versionDirectory);
        var versionJson = new JsonObject
        {
            ["id"] = versionName,
            ["jar"] = string.IsNullOrWhiteSpace(jar) ? versionName : jar
        };

        if (!string.IsNullOrWhiteSpace(type))
            versionJson["type"] = type;

        if (!string.IsNullOrWhiteSpace(inheritsFrom))
            versionJson["inheritsFrom"] = inheritsFrom;

        if (libraries is { Count: > 0 })
        {
            var libraryArray = new JsonArray();
            foreach (var library in libraries)
                libraryArray.Add(new JsonObject { ["name"] = library });

            versionJson["libraries"] = libraryArray;
        }

        await File.WriteAllTextAsync(
            Path.Combine(versionDirectory, $"{versionName}.json"),
            versionJson.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        if (writeJar)
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

