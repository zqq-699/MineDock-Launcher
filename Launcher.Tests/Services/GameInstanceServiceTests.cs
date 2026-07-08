using System.Text.Json;
using System.Text.Json.Nodes;
using Launcher.Application.Repositories;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;
using Launcher.Infrastructure.Persistence;
using System.IO.Compression;

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
    public async Task InstanceServiceRejectsPathLikeInstanceName()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        var provider = new FakeLoaderProvider();
        var service = new GameInstanceService(settingsService, repository, [provider]);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateInstanceAsync("1.20.1", LoaderKind.Vanilla, null, @"..\Pack", null));

        Assert.Null(provider.LastIsolatedVersionName);
        Assert.False(Directory.Exists(Path.Combine(settings.MinecraftDirectory, "versions", "Pack")));
        Assert.False(Directory.Exists(Path.Combine(settings.MinecraftDirectory, "Pack")));
    }

    [Fact]
    public async Task InstanceServiceInstallsFabricApiWhenVersionIsSelected()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        var provider = new FakeLoaderProvider { Kind = LoaderKind.Fabric };
        var modrinthService = new FakeModrinthService();
        var service = new GameInstanceService(settingsService, repository, [provider], modrinthService);

        var instance = await service.CreateInstanceAsync(
            "1.20.1",
            LoaderKind.Fabric,
            "0.16.10",
            "Fabric Pack",
            null,
            fabricApiVersionId: "fabric-api-new");

        Assert.Equal("Fabric Pack", instance.VersionName);
        Assert.Equal(1, modrinthService.InstallFabricApiCallCount);
        Assert.Equal("fabric-api-new", modrinthService.LastFabricApiVersionId);
        Assert.Equal(instance.InstanceDirectory, modrinthService.LastInstance?.InstanceDirectory);
        Assert.Equal("1.20.1", modrinthService.LastInstance?.MinecraftVersion);
        Assert.True(File.Exists(Path.Combine(instance.InstanceDirectory, "mods", "fabric-api.jar")));
    }

    [Fact]
    public async Task InstanceServiceSkipsFabricApiInstallWithoutVersionSelection()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        var provider = new FakeLoaderProvider { Kind = LoaderKind.Fabric };
        var modrinthService = new FakeModrinthService();
        var service = new GameInstanceService(settingsService, repository, [provider], modrinthService);

        var instance = await service.CreateInstanceAsync(
            "1.20.1",
            LoaderKind.Fabric,
            "0.16.10",
            "Fabric Pack",
            null);

        Assert.Equal("Fabric Pack", instance.VersionName);
        Assert.Equal(0, modrinthService.InstallFabricApiCallCount);
        Assert.False(File.Exists(Path.Combine(instance.InstanceDirectory, "mods", "fabric-api.jar")));
    }

    [Fact]
    public async Task InstanceServiceDoesNotInstallFabricApiForOtherLoaders()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        var provider = new FakeLoaderProvider { Kind = LoaderKind.Forge };
        var modrinthService = new FakeModrinthService();
        var service = new GameInstanceService(settingsService, repository, [provider], modrinthService);

        await service.CreateInstanceAsync(
            "1.20.1",
            LoaderKind.Forge,
            "47.4.20",
            "Forge Pack",
            null,
            fabricApiVersionId: "fabric-api-new");

        Assert.Equal(0, modrinthService.InstallFabricApiCallCount);
    }

    [Fact]
    public async Task InstanceServiceInstallsQuiltStandardLibraryWhenVersionIsSelected()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        var provider = new FakeLoaderProvider { Kind = LoaderKind.Quilt };
        var modrinthService = new FakeModrinthService();
        var service = new GameInstanceService(settingsService, repository, [provider], modrinthService);

        var instance = await service.CreateInstanceAsync(
            "1.20.2",
            LoaderKind.Quilt,
            "0.29.2",
            "Quilt Pack",
            null,
            quiltStandardLibraryVersionId: "qsl-new");

        Assert.Equal("Quilt Pack", instance.VersionName);
        Assert.Equal(1, modrinthService.InstallQuiltStandardLibraryCallCount);
        Assert.Equal("qsl-new", modrinthService.LastQuiltStandardLibraryVersionId);
        Assert.Equal(instance.InstanceDirectory, modrinthService.LastInstance?.InstanceDirectory);
        Assert.True(File.Exists(Path.Combine(instance.InstanceDirectory, "mods", "qsl.jar")));
    }

    [Fact]
    public async Task InstanceServiceDoesNotInstallQuiltStandardLibraryForOtherLoaders()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        var provider = new FakeLoaderProvider { Kind = LoaderKind.Forge };
        var modrinthService = new FakeModrinthService();
        var service = new GameInstanceService(settingsService, repository, [provider], modrinthService);

        await service.CreateInstanceAsync(
            "1.20.1",
            LoaderKind.Forge,
            "47.4.20",
            "Forge Pack",
            null,
            quiltStandardLibraryVersionId: "qsl-new");

        Assert.Equal(0, modrinthService.InstallQuiltStandardLibraryCallCount);
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
        var provider = new FakeLoaderProvider { Kind = LoaderKind.Fabric };
        var modrinthService = new FakeModrinthService { InstallFabricApiException = new InvalidOperationException("fabric api failed") };
        var service = new GameInstanceService(settingsService, repository, [provider], modrinthService);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateInstanceAsync(
                "1.20.1",
                LoaderKind.Fabric,
                "0.16.10",
                "Fabric Pack",
                null,
                fabricApiVersionId: "fabric-api-new"));

        Assert.Empty(await repository.GetAllAsync());
        Assert.False(Directory.Exists(Path.Combine(settings.MinecraftDirectory, "versions", "Fabric Pack")));
    }

    [Fact]
    public async Task InstanceServiceFailsQuiltCreateWhenStandardLibraryInstallFails()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        var provider = new FakeLoaderProvider { Kind = LoaderKind.Quilt };
        var modrinthService = new FakeModrinthService { InstallQuiltStandardLibraryException = new InvalidOperationException("qsl failed") };
        var service = new GameInstanceService(settingsService, repository, [provider], modrinthService);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateInstanceAsync(
                "1.20.2",
                LoaderKind.Quilt,
                "0.29.2",
                "Quilt Pack",
                null,
                quiltStandardLibraryVersionId: "qsl-new"));

        Assert.Empty(await repository.GetAllAsync());
        Assert.False(Directory.Exists(Path.Combine(settings.MinecraftDirectory, "versions", "Quilt Pack")));
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

        var createTask = service.CreateInstanceAsync("1.20.1", LoaderKind.Vanilla, null, "Custom Name", null, cancellationToken: cancellation.Token);
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

        var createTask = service.CreateInstanceAsync("1.20.1", LoaderKind.Vanilla, null, "Custom Name", null, cancellationToken: cancellation.Token);
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
    public async Task InstanceServiceDoesNotDiscoverVersionMarkedBySharedInstallCoordinator()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        var coordinator = new GameInstallCoordinator();
        var versionName = "Importing Pack";
        await CreateInstalledVersionAsync(settings.MinecraftDirectory, versionName);
        var service = new GameInstanceService(
            settingsService,
            repository,
            [new FakeLoaderProvider()],
            installCoordinator: coordinator);

        var installLease = await coordinator.AcquireInstallAsync(settings.MinecraftDirectory, versionName, progress: null);
        await using (installLease)
        {
            var instancesDuringInstall = await service.GetInstancesAsync();
            Assert.Empty(instancesDuringInstall);
            Assert.Empty(await repository.GetAllAsync());
        }

        var instancesAfterInstall = await service.GetInstancesAsync();
        var instance = Assert.Single(instancesAfterInstall);
        Assert.Equal(versionName, instance.VersionName);
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
        Assert.Equal("Old Display Name", instance.Name);
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
    public async Task InstanceServicePrefersFabricLoaderVersionOverMixinLibraryMetadata()
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
            libraries:
            [
                "net.fabricmc:sponge-mixin:0.15.0+mixin.0.8.7",
                "net.fabricmc:fabric-loader:0.16.9"
            ],
            writeJar: false);
        var service = new GameInstanceService(settingsService, repository, [new FakeLoaderProvider()]);

        var instance = (await service.GetInstancesAsync()).Single(item => item.VersionName == "fabric-loader-0.16.9-1.21.4");

        Assert.Equal(LoaderKind.Fabric, instance.Loader);
        Assert.Equal("0.16.9", instance.LoaderVersion);
    }

    [Fact]
    public async Task InstanceServiceDetectsModernNeoForgeImportedModpackVersions()
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
            "All of Create 1.21.1",
            type: "release",
            jar: "All of Create 1.21.1",
            libraries:
            [
                "net.neoforged.fancymodloader:loader:4.0.42",
                "net.neoforged:accesstransformers:10.0.1",
                "cpw.mods:modlauncher:11.0.5"
            ],
            launcherMinecraftVersion: "1.21.1",
            mainClass: "cpw.mods.bootstraplauncher.BootstrapLauncher",
            gameArguments:
            [
                "--fml.neoForgeVersion",
                "21.1.233",
                "--fml.fmlVersion",
                "4.0.42",
                "--fml.mcVersion",
                "1.21.1",
                "--launchTarget",
                "forgeclient"
            ]);
        var service = new GameInstanceService(settingsService, repository, [new FakeLoaderProvider()]);

        var instance = (await service.GetInstancesAsync()).Single(item => item.VersionName == "All of Create 1.21.1");

        Assert.Equal(LoaderKind.NeoForge, instance.Loader);
        Assert.Equal("21.1.233", instance.LoaderVersion);
        Assert.Equal("1.21.1", instance.MinecraftVersion);
    }

    [Fact]
    public async Task InstanceServiceDiscoversImportedVersionWhenJsonFileNameDiffersFromFolderName()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        var versionDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "Imported Pack");
        Directory.CreateDirectory(versionDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(versionDirectory, "actual-version.json"),
            """
            {
              "id": "actual-version",
              "type": "release",
              "clientVersion": "1.20.1",
              "mainClass": "net.minecraft.client.main.Main"
            }
            """);
        var service = new GameInstanceService(settingsService, repository, [new FakeLoaderProvider()]);

        var instance = Assert.Single(await service.GetInstancesAsync());

        Assert.Equal("Imported Pack", instance.VersionName);
        Assert.Equal("1.20.1", instance.MinecraftVersion);
        Assert.True(File.Exists(Path.Combine(versionDirectory, "actual-version.json")));
        Assert.False(File.Exists(Path.Combine(versionDirectory, "Imported Pack.json")));
    }

    [Fact]
    public async Task InstanceServiceUsesClientVersionMetadataForImportedVersion()
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
            "client-version-pack",
            type: "release",
            clientVersion: "1.20.4",
            writeJar: false);
        var service = new GameInstanceService(settingsService, repository, [new FakeLoaderProvider()]);

        var instance = Assert.Single(await service.GetInstancesAsync());

        Assert.Equal("1.20.4", instance.MinecraftVersion);
    }

    [Fact]
    public async Task InstanceServiceReadsHmclPatchMetadataForMinecraftAndLoaderVersions()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        var versionDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "hmcl-patch-pack");
        Directory.CreateDirectory(versionDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(versionDirectory, "hmcl-patch-pack.json"),
            """
            {
              "id": "hmcl-patch-pack",
              "patches": [
                {
                  "id": "game",
                  "version": "1.20.1"
                },
                {
                  "id": "fabric",
                  "version": "0.16.10",
                  "mainClass": "net.fabricmc.loader.impl.launch.knot.KnotClient",
                  "libraries": [
                    { "name": "net.fabricmc:fabric-loader:0.16.10" }
                  ]
                }
              ]
            }
            """);
        var service = new GameInstanceService(settingsService, repository, [new FakeLoaderProvider()]);

        var instance = Assert.Single(await service.GetInstancesAsync());

        Assert.Equal("1.20.1", instance.MinecraftVersion);
        Assert.Equal(LoaderKind.Fabric, instance.Loader);
        Assert.Equal("0.16.10", instance.LoaderVersion);
    }

    [Fact]
    public async Task InstanceServiceReadsNeoForgeMinecraftAndLoaderVersionsFromFmlArguments()
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
            "argument-neoforge-pack",
            type: "release",
            libraries: ["net.neoforged.fancymodloader:loader:4.0.42"],
            mainClass: "cpw.mods.bootstraplauncher.BootstrapLauncher",
            gameArguments:
            [
                "--fml.forgeVersion",
                "21.1.233",
                "--fml.mcVersion",
                "1.21.1",
                "--launchTarget",
                "forgeclient"
            ],
            writeJar: false);
        var service = new GameInstanceService(settingsService, repository, [new FakeLoaderProvider()]);

        var instance = Assert.Single(await service.GetInstancesAsync());

        Assert.Equal("1.21.1", instance.MinecraftVersion);
        Assert.Equal(LoaderKind.NeoForge, instance.Loader);
        Assert.Equal("21.1.233", instance.LoaderVersion);
    }

    [Fact]
    public async Task InstanceServiceReadsMinecraftVersionFromFabricAndQuiltIntermediaryLibraries()
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
            "fabric-intermediary-pack",
            type: "release",
            libraries:
            [
                "net.fabricmc:fabric-loader:0.16.10",
                "net.fabricmc:intermediary:1.20.2"
            ],
            writeJar: false);
        await CreateInstalledVersionAsync(
            settings.MinecraftDirectory,
            "quilt-intermediary-pack",
            type: "release",
            libraries:
            [
                "org.quiltmc:quilt-loader:0.26.0",
                "org.quiltmc:intermediary:1.20.4"
            ],
            writeJar: false);
        var service = new GameInstanceService(settingsService, repository, [new FakeLoaderProvider()]);

        var instances = (await service.GetInstancesAsync()).ToDictionary(instance => instance.VersionName);

        Assert.Equal("1.20.2", instances["fabric-intermediary-pack"].MinecraftVersion);
        Assert.Equal(LoaderKind.Fabric, instances["fabric-intermediary-pack"].Loader);
        Assert.Equal("1.20.4", instances["quilt-intermediary-pack"].MinecraftVersion);
        Assert.Equal(LoaderKind.Quilt, instances["quilt-intermediary-pack"].Loader);
    }

    [Fact]
    public async Task InstanceServiceExtractsPreReleaseAndReleaseCandidateMinecraftVersionsFromNames()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        await CreateInstalledVersionAsync(settings.MinecraftDirectory, "custom-1.20.2-rc1", type: "release", writeJar: false);
        await CreateInstalledVersionAsync(settings.MinecraftDirectory, "custom-1.20.2-pre1", type: "release", writeJar: false);
        var service = new GameInstanceService(settingsService, repository, [new FakeLoaderProvider()]);

        var instances = (await service.GetInstancesAsync()).ToDictionary(instance => instance.VersionName);

        Assert.Equal("1.20.2-rc1", instances["custom-1.20.2-rc1"].MinecraftVersion);
        Assert.Equal("1.20.2-pre1", instances["custom-1.20.2-pre1"].MinecraftVersion);
    }

    [Fact]
    public async Task InstanceServiceUsesJarVersionJsonNameAsFallbackMinecraftVersion()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        await CreateInstalledVersionAsync(settings.MinecraftDirectory, "jar-version-pack", type: "release", writeJar: false);
        CreateVersionJarWithVersionJson(
            Path.Combine(settings.MinecraftDirectory, "versions", "jar-version-pack", "jar-version-pack.jar"),
            "1.19.4");
        var service = new GameInstanceService(settingsService, repository, [new FakeLoaderProvider()]);

        var instance = Assert.Single(await service.GetInstancesAsync());

        Assert.Equal("1.19.4", instance.MinecraftVersion);
    }

    [Fact]
    public async Task InstanceServiceSkipsDirectoryWithAmbiguousJsonCandidates()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        var versionDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "ambiguous-pack");
        Directory.CreateDirectory(versionDirectory);
        await File.WriteAllTextAsync(Path.Combine(versionDirectory, "first.json"), """{ "id": "first", "clientVersion": "1.20.1" }""");
        await File.WriteAllTextAsync(Path.Combine(versionDirectory, "second.json"), """{ "id": "second", "clientVersion": "1.20.2" }""");
        var service = new GameInstanceService(settingsService, repository, [new FakeLoaderProvider()]);

        var instances = await service.GetInstancesAsync();

        Assert.Empty(instances);
    }

    [Fact]
    public async Task InstanceServiceIgnoresNonVersionJsonCandidates()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        var versionDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "not-a-version");
        Directory.CreateDirectory(versionDirectory);
        await File.WriteAllTextAsync(Path.Combine(versionDirectory, "metadata.json"), """{ "id": "metadata" }""");
        var service = new GameInstanceService(settingsService, repository, [new FakeLoaderProvider()]);

        var instances = await service.GetInstancesAsync();

        Assert.Empty(instances);
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
    public async Task InstanceServiceSavesDefaultInstanceSelection()
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
        var selected = CreateStoredInstance("second");
        selected.Name = "Second Pack";
        selected.MinecraftVersion = "1.21.4";
        selected.Loader = LoaderKind.Fabric;
        selected.LoaderVersion = "0.16.10";
        selected.IconSource = "/custom/icon.png";
        selected.VersionType = "release";
        await repository.SaveAllAsync(
        [
            CreateStoredInstance("first"),
            selected
        ]);
        var service = new GameInstanceService(settingsService, repository, [new FakeLoaderProvider()]);

        var saved = await service.SetDefaultInstanceAsync("second");

        var savedSettings = await settingsService.LoadAsync();
        Assert.True(saved);
        Assert.Equal("second", savedSettings.DefaultInstanceId);
    }

    [Fact]
    public async Task InstanceServiceLoadsStoredInstancesWithoutDiscoveringVersionsOrSaving()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft"),
            DefaultInstanceId = "first"
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new CountingGameInstanceRepository(
        [
            CreateStoredInstance("first"),
            CreateStoredInstance("second")
        ]);
        var service = new GameInstanceService(settingsService, repository, [new FakeLoaderProvider()]);

        var instances = await service.GetStoredInstancesAsync(settings);

        Assert.Equal(["first", "second"], instances.Select(instance => instance.Id));
        Assert.Equal(1, repository.GetAllForDirectoryCallCount);
        Assert.Equal(settings.MinecraftDirectory, repository.LastGetAllMinecraftDirectory);
        Assert.Equal(0, repository.DiscoverInstalledVersionsCallCount);
        Assert.Equal(0, repository.SaveAllCallCount);
        Assert.Equal(0, settingsService.SaveCount);
    }

    [Fact]
    public async Task InstanceServiceSavesDefaultInstanceSelectionWithoutDiscoveringVersions()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft"),
            DefaultInstanceId = "first"
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new CountingGameInstanceRepository(
        [
            CreateStoredInstance("first"),
            CreateStoredInstance("second")
        ]);
        var service = new GameInstanceService(settingsService, repository, [new FakeLoaderProvider()]);

        var saved = await service.SetDefaultInstanceAsync("second");

        Assert.True(saved);
        Assert.Equal("second", (await settingsService.LoadAsync()).DefaultInstanceId);
        Assert.Equal(0, repository.DiscoverInstalledVersionsCallCount);
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
    public async Task InstanceServiceDoesNotSaveMissingDefaultInstanceSelectionWithoutDiscoveringVersions()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft"),
            DefaultInstanceId = "first"
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new CountingGameInstanceRepository([CreateStoredInstance("first")]);
        var service = new GameInstanceService(settingsService, repository, [new FakeLoaderProvider()]);

        var saved = await service.SetDefaultInstanceAsync("missing");

        Assert.False(saved);
        Assert.Equal("first", (await settingsService.LoadAsync()).DefaultInstanceId);
        Assert.Equal(0, repository.DiscoverInstalledVersionsCallCount);
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
        var savedSettings = await settingsService.LoadAsync();
        Assert.Equal("second", savedSettings.DefaultInstanceId);
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
        var savedSettings = await settingsService.LoadAsync();
        Assert.Equal(string.Empty, savedSettings.DefaultInstanceId);
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

    [Fact]
    public async Task RenameInstanceAsyncRenamesVersionDirectoryAndPrimaryFiles()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft"),
            DefaultInstanceId = "old"
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        await CreateInstalledVersionAsync(
            settings.MinecraftDirectory,
            "Old Pack",
            type: "release",
            launcherMinecraftVersion: "1.20.1");
        var oldDirectoryBeforeRename = Path.Combine(settings.MinecraftDirectory, "versions", "Old Pack");
        Directory.CreateDirectory(Path.Combine(oldDirectoryBeforeRename, "mods"));
        Directory.CreateDirectory(Path.Combine(oldDirectoryBeforeRename, "config"));
        Directory.CreateDirectory(Path.Combine(oldDirectoryBeforeRename, "saves", "World One"));
        await File.WriteAllTextAsync(Path.Combine(oldDirectoryBeforeRename, "mods", "example.jar"), "mod");
        await File.WriteAllTextAsync(Path.Combine(oldDirectoryBeforeRename, "config", "example.cfg"), "config");
        await File.WriteAllTextAsync(Path.Combine(oldDirectoryBeforeRename, "saves", "World One", "level.dat"), "save");
        await repository.SaveAllAsync(
        [
            new GameInstance
            {
                Id = "old",
                Name = "Old Pack",
                MinecraftVersion = "1.20.1",
                VersionName = "Old Pack",
                InstanceDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "Old Pack")
            }
        ]);
        var service = new GameInstanceService(settingsService, repository, [new FakeLoaderProvider()]);

        var updated = await service.RenameInstanceAsync("old", "Renamed Pack", "/Assets/Icons/block/diamond_block.png");

        Assert.Equal("Renamed Pack", updated.Name);
        Assert.Equal("Renamed Pack", updated.VersionName);
        Assert.Equal("/Assets/Icons/block/diamond_block.png", updated.IconSource);
        Assert.Equal(
            Path.Combine(settings.MinecraftDirectory, "versions", "Renamed Pack"),
            updated.InstanceDirectory);

        var oldDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "Old Pack");
        var newDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "Renamed Pack");
        Assert.False(Directory.Exists(oldDirectory));
        Assert.True(Directory.Exists(newDirectory));
        Assert.False(File.Exists(Path.Combine(newDirectory, "Old Pack.json")));
        Assert.False(File.Exists(Path.Combine(newDirectory, "Old Pack.jar")));
        Assert.True(File.Exists(Path.Combine(newDirectory, "Renamed Pack.json")));
        Assert.True(File.Exists(Path.Combine(newDirectory, "Renamed Pack.jar")));
        Assert.True(File.Exists(Path.Combine(newDirectory, "mods", "example.jar")));
        Assert.True(File.Exists(Path.Combine(newDirectory, "config", "example.cfg")));
        Assert.True(File.Exists(Path.Combine(newDirectory, "saves", "World One", "level.dat")));

        using var jsonDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(newDirectory, "Renamed Pack.json")));
        Assert.Equal("Renamed Pack", jsonDocument.RootElement.GetProperty("id").GetString());
        Assert.Equal("Renamed Pack", jsonDocument.RootElement.GetProperty("jar").GetString());

        var storedInstance = Assert.Single(await repository.GetAllAsync());
        Assert.Equal("Renamed Pack", storedInstance.Name);
        Assert.Equal("Renamed Pack", storedInstance.VersionName);
        Assert.Equal("/Assets/Icons/block/diamond_block.png", storedInstance.IconSource);
        Assert.Equal(
            Path.Combine(settings.MinecraftDirectory, "versions", "Renamed Pack"),
            storedInstance.InstanceDirectory);
    }

    [Fact]
    public async Task RenameInstanceAsyncRejectsPathLikeName()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        await CreateInstalledVersionAsync(settings.MinecraftDirectory, "Old Pack");
        await repository.SaveAllAsync(
        [
            new GameInstance
            {
                Id = "old",
                Name = "Old Pack",
                MinecraftVersion = "1.20.1",
                VersionName = "Old Pack",
                InstanceDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "Old Pack")
            }
        ]);
        var service = new GameInstanceService(settingsService, repository, [new FakeLoaderProvider()]);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.RenameInstanceAsync("old", @"..\Renamed Pack", null));

        var storedInstance = Assert.Single(await repository.GetAllAsync());
        Assert.Equal("Old Pack", storedInstance.VersionName);
        Assert.True(Directory.Exists(Path.Combine(settings.MinecraftDirectory, "versions", "Old Pack")));
        Assert.False(Directory.Exists(Path.Combine(settings.MinecraftDirectory, "versions", "Renamed Pack")));
        Assert.False(Directory.Exists(Path.Combine(settings.MinecraftDirectory, "Renamed Pack")));
    }

    [Fact]
    public async Task RenameInstanceAsyncRollsBackWhenPrimaryJarRenameFails()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        await CreateInstalledVersionAsync(settings.MinecraftDirectory, "Old Pack");
        var oldDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "Old Pack");
        await File.WriteAllTextAsync(Path.Combine(oldDirectory, "Renamed Pack.jar"), "conflict");
        await repository.SaveAllAsync(
        [
            new GameInstance
            {
                Id = "old",
                Name = "Old Pack",
                MinecraftVersion = "1.20.1",
                VersionName = "Old Pack",
                InstanceDirectory = oldDirectory
            }
        ]);
        var service = new GameInstanceService(settingsService, repository, [new FakeLoaderProvider()]);

        await Assert.ThrowsAsync<IOException>(() =>
            service.RenameInstanceAsync("old", "Renamed Pack", null));

        var newDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "Renamed Pack");
        Assert.True(Directory.Exists(oldDirectory));
        Assert.False(Directory.Exists(newDirectory));
        Assert.True(File.Exists(Path.Combine(oldDirectory, "Old Pack.json")));
        Assert.True(File.Exists(Path.Combine(oldDirectory, "Old Pack.jar")));
        Assert.True(File.Exists(Path.Combine(oldDirectory, "Renamed Pack.jar")));

        using var jsonDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(oldDirectory, "Old Pack.json")));
        Assert.Equal("Old Pack", jsonDocument.RootElement.GetProperty("id").GetString());
        Assert.Equal("Old Pack", jsonDocument.RootElement.GetProperty("jar").GetString());
    }

    [Fact]
    public async Task RenameInstanceAsyncKeepsInheritedVersionReference()
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
            "fabric-child",
            type: "snapshot",
            inheritsFrom: "1.20.1");
        await repository.SaveAllAsync(
        [
            new GameInstance
            {
                Id = "child",
                Name = "fabric-child",
                MinecraftVersion = "1.20.1",
                VersionName = "fabric-child",
                InstanceDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "fabric-child")
            }
        ]);
        var service = new GameInstanceService(settingsService, repository, [new FakeLoaderProvider()]);

        await service.RenameInstanceAsync("child", "fabric-details", null);

        using var jsonDocument = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(settings.MinecraftDirectory, "versions", "fabric-details", "fabric-details.json")));
        Assert.Equal("fabric-details", jsonDocument.RootElement.GetProperty("id").GetString());
        Assert.Equal("1.20.1", jsonDocument.RootElement.GetProperty("inheritsFrom").GetString());
    }

    [Fact]
    public async Task RenameInstanceAsyncRejectsDuplicateVersionIdentity()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
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

        await Assert.ThrowsAsync<DuplicateGameInstanceNameException>(() =>
            service.RenameInstanceAsync("first", "second", null));

        Assert.True(Directory.Exists(Path.Combine(settings.MinecraftDirectory, "versions", "first")));
        Assert.True(Directory.Exists(Path.Combine(settings.MinecraftDirectory, "versions", "second")));
    }

    private static async Task CreateInstalledVersionAsync(
        string minecraftDirectory,
        string versionName,
        string? type = null,
        string? inheritsFrom = null,
        string? jar = null,
        IReadOnlyList<string>? libraries = null,
        string? launcherMinecraftVersion = null,
        string? clientVersion = null,
        string? assetIndexId = null,
        string? mainClass = null,
        IReadOnlyList<string>? gameArguments = null,
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

        if (!string.IsNullOrWhiteSpace(mainClass))
            versionJson["mainClass"] = mainClass;

        if (libraries is { Count: > 0 })
        {
            var libraryArray = new JsonArray();
            foreach (var library in libraries)
                libraryArray.Add(new JsonObject { ["name"] = library });

            versionJson["libraries"] = libraryArray;
        }

        if (gameArguments is { Count: > 0 })
        {
            var argumentArray = new JsonArray();
            foreach (var argument in gameArguments)
                argumentArray.Add(JsonValue.Create(argument));

            versionJson["arguments"] = new JsonObject
            {
                ["game"] = argumentArray
            };
        }

        if (!string.IsNullOrWhiteSpace(launcherMinecraftVersion))
        {
            versionJson["launcher"] = new JsonObject
            {
                ["minecraftVersion"] = launcherMinecraftVersion
            };
        }

        if (!string.IsNullOrWhiteSpace(clientVersion))
            versionJson["clientVersion"] = clientVersion;

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

    private static void CreateVersionJarWithVersionJson(string jarPath, string minecraftVersion)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(jarPath)!);
        using var archive = ZipFile.Open(jarPath, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("version.json");
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream);
        writer.Write($$"""{ "name": "{{minecraftVersion}}" }""");
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

    private sealed class CountingGameInstanceRepository(IReadOnlyList<GameInstance> instances) : IGameInstanceRepository
    {
        private List<GameInstance> instances = [.. instances];

        public int GetAllForDirectoryCallCount { get; private set; }
        public int DiscoverInstalledVersionsCallCount { get; private set; }
        public int SaveAllCallCount { get; private set; }
        public string? LastGetAllMinecraftDirectory { get; private set; }

        public Task<IReadOnlyList<GameInstance>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<GameInstance>>(instances.ToList());
        }

        public Task<IReadOnlyList<GameInstance>> GetAllAsync(
            string minecraftDirectory,
            CancellationToken cancellationToken = default)
        {
            GetAllForDirectoryCallCount++;
            LastGetAllMinecraftDirectory = minecraftDirectory;
            return Task.FromResult<IReadOnlyList<GameInstance>>(instances.ToList());
        }

        public Task SaveAllAsync(IReadOnlyCollection<GameInstance> instances, CancellationToken cancellationToken = default)
        {
            SaveAllCallCount++;
            this.instances = instances.ToList();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<InstalledGameVersion>> DiscoverInstalledVersionsAsync(
            string minecraftDirectory,
            CancellationToken cancellationToken = default)
        {
            DiscoverInstalledVersionsCallCount++;
            return Task.FromResult<IReadOnlyList<InstalledGameVersion>>([]);
        }

        public string GetUniqueInstanceDirectory(string dataDirectory, string name)
        {
            return Path.Combine(dataDirectory, "instances", name);
        }

        public string GetVersionDirectory(string minecraftDirectory, string versionName)
        {
            return Path.Combine(minecraftDirectory, "versions", versionName);
        }

        public bool IsInstanceInstalled(GameInstance instance, string minecraftDirectory)
        {
            return true;
        }

        public void CreateInstanceDirectories(string directory)
        {
        }

        public void DeleteVersionDirectory(string minecraftDirectory, string versionName)
        {
        }

        public Task RenameVersionAsync(
            string minecraftDirectory,
            string oldVersionName,
            string newVersionName,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
    
    private sealed class FakeModrinthService : IModrinthService
    {
        public int InstallFabricApiCallCount { get; private set; }
        public int InstallQuiltStandardLibraryCallCount { get; private set; }
        public GameInstance? LastInstance { get; private set; }
        public string? LastFabricApiVersionId { get; private set; }
        public string? LastQuiltStandardLibraryVersionId { get; private set; }
        public Exception? InstallFabricApiException { get; init; }
        public Exception? InstallQuiltStandardLibraryException { get; init; }

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

        public async Task<string> InstallFabricApiAsync(
            GameInstance instance,
            string versionId,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default)
        {
            InstallFabricApiCallCount++;
            LastInstance = instance;
            LastFabricApiVersionId = versionId;

            if (InstallFabricApiException is not null)
                throw InstallFabricApiException;

            var modsDirectory = Path.Combine(instance.InstanceDirectory, "mods");
            Directory.CreateDirectory(modsDirectory);
            var target = Path.Combine(modsDirectory, "fabric-api.jar");
            await File.WriteAllTextAsync(target, "fabric api", cancellationToken);
            return target;
        }

        public async Task<string> InstallQuiltStandardLibraryAsync(
            GameInstance instance,
            string versionId,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default)
        {
            InstallQuiltStandardLibraryCallCount++;
            LastInstance = instance;
            LastQuiltStandardLibraryVersionId = versionId;

            if (InstallQuiltStandardLibraryException is not null)
                throw InstallQuiltStandardLibraryException;

            var modsDirectory = Path.Combine(instance.InstanceDirectory, "mods");
            Directory.CreateDirectory(modsDirectory);
            var target = Path.Combine(modsDirectory, "qsl.jar");
            await File.WriteAllTextAsync(target, "qsl", cancellationToken);
            return target;
        }
    }
}

