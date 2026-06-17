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
    public async Task InstanceServiceAutomaticallyInstallsFabricApiForFabricInstances()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        var provider = new FakeLoaderProvider { Kind = LoaderKind.Fabric, DisplayName = "Fabric" };
        var modrinthService = new FakeModrinthService();
        var service = new GameInstanceService(settingsService, repository, [provider], modrinthService);

        var instance = await service.CreateInstanceAsync("1.20.1", LoaderKind.Fabric, "0.16.10", "Fabric Pack", null);

        Assert.Equal("Fabric Pack", instance.VersionName);
        Assert.Equal(1, modrinthService.InstallFabricApiCallCount);
        Assert.Equal(instance.InstanceDirectory, modrinthService.LastInstance?.InstanceDirectory);
        Assert.Equal("1.20.1", modrinthService.LastInstance?.MinecraftVersion);
        Assert.True(File.Exists(Path.Combine(instance.InstanceDirectory, "mods", "fabric-api.jar")));
    }

    [Fact]
    public async Task InstanceServiceFailsFabricCreateWhenFabricApiInstallFails()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        var provider = new FakeLoaderProvider { Kind = LoaderKind.Fabric, DisplayName = "Fabric" };
        var modrinthService = new FakeModrinthService { InstallFabricApiException = new InvalidOperationException("fabric api failed") };
        var service = new GameInstanceService(settingsService, repository, [provider], modrinthService);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateInstanceAsync("1.20.1", LoaderKind.Fabric, "0.16.10", "Fabric Pack", null));

        Assert.Empty(await repository.GetAllAsync());
        Assert.False(Directory.Exists(Path.Combine(settings.MinecraftDirectory, "versions", "Fabric Pack")));
    }

    [Fact]
    public async Task InstanceServicePassesDefaultJavaPathToForgeProvider()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft"),
            DefaultJavaPath = @"C:\Java\bin\java.exe"
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        var provider = new FakeLoaderProvider { Kind = LoaderKind.Forge, DisplayName = "Forge" };
        var modrinthService = new FakeModrinthService();
        var service = new GameInstanceService(settingsService, repository, [provider], modrinthService);

        var instance = await service.CreateInstanceAsync("1.20.1", LoaderKind.Forge, "47.4.20", "Forge Pack", null);

        Assert.Equal("Forge Pack", instance.VersionName);
        Assert.Equal(settings.DefaultJavaPath, provider.LastJavaPath);
        Assert.Equal(0, modrinthService.InstallFabricApiCallCount);
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
    public async Task InstanceServiceDoesNotDiscoverVersionWhileInstallIsInProgress()
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
            WaitBeforeInstall = waitBeforeInstall.Task
        };
        var service = new GameInstanceService(settingsService, repository, [provider]);

        var createTask = service.CreateInstanceAsync("1.20.1", LoaderKind.Vanilla, null, "1.20.1", null);
        await provider.InstallStarted.Task;
        await TestAsync.WaitForAsync(() => File.Exists(Path.Combine(settings.MinecraftDirectory, "versions", "1.20.1", "1.20.1.json")));

        var instancesDuringInstall = await service.GetInstancesAsync();
        Assert.Empty(instancesDuringInstall);

        waitBeforeInstall.SetResult(true);
        await createTask;

        var instancesAfterInstall = await service.GetInstancesAsync();
        Assert.Single(instancesAfterInstall);
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
    public async Task VersionIsolatorCanAliasDerivedVersionWithoutClientJar()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var sourceDirectory = Path.Combine(minecraftDirectory, "versions", "fabric-loader-0.16.10-1.20.1");
        Directory.CreateDirectory(sourceDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(sourceDirectory, "fabric-loader-0.16.10-1.20.1.json"),
            """
            {
              "id": "fabric-loader-0.16.10-1.20.1",
              "inheritsFrom": "1.20.1",
              "mainClass": "net.fabricmc.loader.impl.launch.knot.KnotClient"
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(sourceDirectory, "win_args.txt"),
            "--launchTarget forge_client");

        var versionName = await VanillaVersionIsolator.CreateIsolatedVersionFromSourceAsync(
            "fabric-loader-0.16.10-1.20.1",
            "1.20.1-fabric-0.16.10",
            minecraftDirectory);

        var destinationDirectory = Path.Combine(minecraftDirectory, "versions", "1.20.1-fabric-0.16.10");
        Assert.Equal("1.20.1-fabric-0.16.10", versionName);
        Assert.False(File.Exists(Path.Combine(destinationDirectory, "1.20.1-fabric-0.16.10.jar")));
        Assert.True(File.Exists(Path.Combine(destinationDirectory, "win_args.txt")));

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(destinationDirectory, "1.20.1-fabric-0.16.10.json")));
        Assert.Equal("1.20.1-fabric-0.16.10", json.RootElement.GetProperty("id").GetString());
        Assert.Equal("1.20.1", json.RootElement.GetProperty("inheritsFrom").GetString());
        Assert.False(json.RootElement.TryGetProperty("jar", out _));
    }

    [Fact]
    public async Task VersionIsolatorCanFlattenDerivedVersionIntoSingleDirectory()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var baseDirectory = Path.Combine(minecraftDirectory, "versions", "1.20.1");
        Directory.CreateDirectory(baseDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(baseDirectory, "1.20.1.json"),
            """
            {
              "id": "1.20.1",
              "type": "release",
              "mainClass": "net.minecraft.client.main.Main",
              "libraries": [
                { "name": "com.mojang:patchy:2.2.10" }
              ],
              "arguments": {
                "game": [ "--username", "${auth_player_name}" ],
                "jvm": [ "-Djava.library.path=${natives_directory}" ]
              }
            }
            """);
        await File.WriteAllTextAsync(Path.Combine(baseDirectory, "1.20.1.jar"), "base jar");

        var derivedDirectory = Path.Combine(minecraftDirectory, "versions", "fabric-loader-0.16.10-1.20.1");
        Directory.CreateDirectory(derivedDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(derivedDirectory, "fabric-loader-0.16.10-1.20.1.json"),
            """
            {
              "id": "fabric-loader-0.16.10-1.20.1",
              "inheritsFrom": "1.20.1",
              "mainClass": "net.fabricmc.loader.impl.launch.knot.KnotClient",
              "libraries": [
                { "name": "net.fabricmc:fabric-loader:0.16.10" }
              ],
              "arguments": {
                "game": [ "--fabric", "1" ],
                "jvm": [ "-Dfabric.skipMcProvider=true" ]
              }
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(derivedDirectory, "win_args.txt"),
            "--launchTarget forge_client");

        var versionName = await VanillaVersionIsolator.CreateFlattenedDerivedVersionAsync(
            "1.20.1",
            "fabric-loader-0.16.10-1.20.1",
            "1.20.1-fabric-0.16.10",
            minecraftDirectory);

        var destinationDirectory = Path.Combine(minecraftDirectory, "versions", "1.20.1-fabric-0.16.10");
        Assert.Equal("1.20.1-fabric-0.16.10", versionName);
        Assert.True(File.Exists(Path.Combine(destinationDirectory, "1.20.1-fabric-0.16.10.jar")));
        Assert.True(File.Exists(Path.Combine(destinationDirectory, "win_args.txt")));

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(destinationDirectory, "1.20.1-fabric-0.16.10.json")));
        Assert.Equal("1.20.1-fabric-0.16.10", json.RootElement.GetProperty("id").GetString());
        Assert.Equal("1.20.1-fabric-0.16.10", json.RootElement.GetProperty("jar").GetString());
        Assert.False(json.RootElement.TryGetProperty("inheritsFrom", out _));
        Assert.Equal("net.fabricmc.loader.impl.launch.knot.KnotClient", json.RootElement.GetProperty("mainClass").GetString());

        var libraries = json.RootElement.GetProperty("libraries").EnumerateArray()
            .Select(item => item.GetProperty("name").GetString())
            .ToList();
        Assert.Contains("com.mojang:patchy:2.2.10", libraries);
        Assert.Contains("net.fabricmc:fabric-loader:0.16.10", libraries);

        var gameArguments = json.RootElement.GetProperty("arguments").GetProperty("game").EnumerateArray()
            .Select(item => item.GetString())
            .ToList();
        Assert.Contains("--username", gameArguments);
        Assert.Contains("--fabric", gameArguments);

        var jvmArguments = json.RootElement.GetProperty("arguments").GetProperty("jvm").EnumerateArray()
            .Select(item => item.GetString())
            .ToList();
        Assert.Contains("-Djava.library.path=${natives_directory}", jvmArguments);
        Assert.Contains("-Dfabric.skipMcProvider=true", jvmArguments);
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
        Assert.Equal(string.Empty, instance.MinecraftVersion);
        Assert.Equal("External Pack", instance.VersionName);
        Assert.Equal("release", instance.VersionType);
        Assert.Equal(LoaderKind.Vanilla, instance.Loader);
        Assert.Equal(Path.Combine(settings.MinecraftDirectory, "versions", "External Pack"), instance.InstanceDirectory);

        var stored = Assert.Single(await repository.GetAllAsync());
        Assert.Equal(instance.Id, stored.Id);
        Assert.Equal("External Pack", stored.VersionName);
    }

    [Fact]
    public async Task InstanceServiceUsesVersionNameForImportedVanillaInstanceWhenItIsARealMinecraftVersion()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        await CreateInstalledVersionAsync(settings.MinecraftDirectory, "1.20.1", type: "release", writeJar: false);
        var service = new GameInstanceService(settingsService, repository, [new FakeLoaderProvider()]);

        var instance = Assert.Single(await service.GetInstancesAsync());

        Assert.Equal("1.20.1", instance.MinecraftVersion);
        Assert.Equal("1.20.1", instance.VersionName);
    }

    [Fact]
    public async Task InstanceServiceUsesBaseMinecraftVersionForRenamedSelfContainedInstance()
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
            "My Forge Pack",
            type: "release",
            libraries: ["net.minecraftforge:forge:1.20.1-47.4.20"],
            launcherMinecraftVersion: "1.20.1",
            writeJar: false);
        var service = new GameInstanceService(settingsService, repository, [new FakeLoaderProvider()]);

        var instances = await service.GetInstancesAsync();

        var instance = Assert.Single(instances);
        Assert.Equal("My Forge Pack", instance.Name);
        Assert.Equal("1.20.1", instance.MinecraftVersion);
        Assert.Equal("My Forge Pack", instance.VersionName);
        Assert.Equal(LoaderKind.Forge, instance.Loader);
    }

    [Fact]
    public async Task InstanceServiceIgnoresNumericAssetIndexAndUsesForgeMinecraftVersion()
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
            "My Forge Pack",
            type: "release",
            libraries: ["net.minecraftforge:forge:1.20.1-47.4.20"],
            assetIndexId: "30",
            writeJar: false);
        var service = new GameInstanceService(settingsService, repository, [new FakeLoaderProvider()]);

        var instance = Assert.Single(await service.GetInstancesAsync());

        Assert.Equal("1.20.1", instance.MinecraftVersion);
        Assert.Equal("47.4.20", instance.LoaderVersion);
    }

    [Fact]
    public async Task InstanceServiceRepairsStoredMinecraftVersionWhenRenamedInstanceMetadataHasBaseVersion()
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
            "My Forge Pack",
            type: "release",
            libraries: ["net.minecraftforge:forge:1.20.1-47.4.20"],
            launcherMinecraftVersion: "1.20.1",
            writeJar: false);
        await repository.SaveAllAsync(
        [
            new GameInstance
            {
                Id = "stored",
                Name = "Renamed Display Name",
                MinecraftVersion = "My Forge Pack",
                Loader = LoaderKind.Forge,
                LoaderVersion = "47.4.20",
                VersionName = "My Forge Pack",
                VersionType = "release",
                InstanceDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "My Forge Pack")
            }
        ]);
        var service = new GameInstanceService(settingsService, repository, [new FakeLoaderProvider()]);

        var instance = Assert.Single(await service.GetInstancesAsync());

        Assert.Equal("1.20.1", instance.MinecraftVersion);

        var stored = Assert.Single(await repository.GetAllAsync());
        Assert.Equal("1.20.1", stored.MinecraftVersion);
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
        Assert.Equal("47.2.0", instances["forge-1.20.1-47.2.0"].LoaderVersion);
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
        string? launcherMinecraftVersion = null,
        string? assetIndexId = null,
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

        if (!string.IsNullOrWhiteSpace(launcherMinecraftVersion))
        {
            versionJson["launcher"] = new JsonObject
            {
                ["minecraftVersion"] = launcherMinecraftVersion
            };
        }

        if (!string.IsNullOrWhiteSpace(assetIndexId))
        {
            versionJson["assetIndex"] = new JsonObject
            {
                ["id"] = assetIndexId
            };
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
    
    private sealed class FakeModrinthService : IModrinthService
    {
        public int InstallFabricApiCallCount { get; private set; }
        public GameInstance? LastInstance { get; private set; }
        public Exception? InstallFabricApiException { get; init; }

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

        public async Task<string> InstallFabricApiAsync(
            GameInstance instance,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default)
        {
            InstallFabricApiCallCount++;
            LastInstance = instance;

            if (InstallFabricApiException is not null)
                throw InstallFabricApiException;

            var modsDirectory = Path.Combine(instance.InstanceDirectory, "mods");
            Directory.CreateDirectory(modsDirectory);
            var target = Path.Combine(modsDirectory, "fabric-api.jar");
            await File.WriteAllTextAsync(target, "fabric api", cancellationToken);
            return target;
        }
    }
}

