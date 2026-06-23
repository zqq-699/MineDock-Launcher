using System.Diagnostics;
using System.Text.Json.Nodes;
using CmlLib.Core.ProcessBuilder;
using Launcher.Application.Accounts;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;
using Launcher.Tests.Helpers;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class LaunchServiceTests : TestTempDirectory
{
    [Fact]
    public async Task ManagedVersionRepairDownloadsMissingJarWithoutCreatingSiblingVersion()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", "Forge Pack");
        Directory.CreateDirectory(versionDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(versionDirectory, "Forge Pack.json"),
            """
            {
              "id": "Forge Pack",
              "jar": "Forge Pack",
              "downloads": {
                "client": {
                  "url": "https://example.test/client.jar"
                }
              }
            }
            """);

        var repairService = new ManagedVersionRepairService(new HttpClient(new RepairHttpHandler()));
        var progress = new RecordingProgress();
        await repairService.RepairAsync(
            minecraftDirectory,
            "Forge Pack",
            versionDirectory,
            progress,
            allowRepair: true,
            CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(versionDirectory, "Forge Pack.jar")));
        Assert.Equal(["Forge Pack"], Directory.GetDirectories(Path.Combine(minecraftDirectory, "versions")).Select(Path.GetFileName));
        Assert.Contains(progress.Items, item => item.Stage == LaunchProgressStages.CheckingInstance);
        Assert.Contains(progress.Items, item => item.Stage == LaunchProgressStages.RepairingJar);
        AssertProgressPercentIsMonotonic(progress.Items);
    }

    [Fact]
    public async Task ManagedVersionRepairDownloadsLibrariesAssetsAndLoggingWithoutCreatingSiblingVersion()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", "Fabric Pack");
        Directory.CreateDirectory(versionDirectory);
        await File.WriteAllTextAsync(Path.Combine(versionDirectory, "Fabric Pack.jar"), "jar");
        await File.WriteAllTextAsync(
            Path.Combine(versionDirectory, "Fabric Pack.json"),
            """
            {
              "id": "Fabric Pack",
              "jar": "Fabric Pack",
              "libraries": [
                {
                  "downloads": {
                    "artifact": {
                      "url": "https://example.test/libraries/example.jar",
                      "path": "com/example/demo/1.0.0/demo-1.0.0.jar"
                    }
                  }
                }
              ],
              "assetIndex": {
                "id": "1.20.1",
                "url": "https://example.test/assets/index.json"
              },
              "logging": {
                "client": {
                  "file": {
                    "id": "client-1.12.xml",
                    "url": "https://example.test/logging/client.xml"
                  }
                }
              }
            }
            """);

        var repairService = new ManagedVersionRepairService(new HttpClient(new RepairHttpHandler()));
        var progress = new RecordingProgress();
        await repairService.RepairAsync(
            minecraftDirectory,
            "Fabric Pack",
            versionDirectory,
            progress,
            allowRepair: true,
            CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(minecraftDirectory, "libraries", "com", "example", "demo", "1.0.0", "demo-1.0.0.jar")));
        Assert.True(File.Exists(Path.Combine(minecraftDirectory, "assets", "indexes", "1.20.1.json")));
        Assert.True(File.Exists(Path.Combine(minecraftDirectory, "assets", "objects", "aa", "aa00000000000000000000000000000000000000")));
        Assert.True(File.Exists(Path.Combine(minecraftDirectory, "assets", "log_configs", "client-1.12.xml")));
        Assert.Equal(["Fabric Pack"], Directory.GetDirectories(Path.Combine(minecraftDirectory, "versions")).Select(Path.GetFileName));
        Assert.Contains(progress.Items, item => item.Stage == LaunchProgressStages.RepairingLibraries);
        Assert.Contains(progress.Items, item => item.Stage == LaunchProgressStages.RepairingAssets);
        Assert.Contains(progress.Items, item => item.Stage == LaunchProgressStages.RepairingLogging);
        Assert.Contains(progress.Items, item => item.Stage == LaunchProgressStages.CheckingJava);
        AssertProgressPercentIsMonotonic(progress.Items);
    }

    [Fact]
    public async Task ManagedVersionRepairDownloadsMissingAssetsConcurrently()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", "Fabric Pack");
        Directory.CreateDirectory(versionDirectory);
        await File.WriteAllTextAsync(Path.Combine(versionDirectory, "Fabric Pack.jar"), "jar");
        await File.WriteAllTextAsync(
            Path.Combine(versionDirectory, "Fabric Pack.json"),
            """
            {
              "id": "Fabric Pack",
              "jar": "Fabric Pack",
              "libraries": [],
              "assetIndex": {
                "id": "concurrent-assets",
                "url": "https://example.test/assets/concurrent-index.json"
              }
            }
            """);

        var handler = new ConcurrentAssetRepairHttpHandler();
        var repairService = new ManagedVersionRepairService(new HttpClient(handler));

        await repairService.RepairAsync(
            minecraftDirectory,
            "Fabric Pack",
            versionDirectory,
            progress: null,
            allowRepair: true,
            CancellationToken.None);

        Assert.True(handler.MaxConcurrentAssetRequests > 1);
        Assert.True(File.Exists(Path.Combine(minecraftDirectory, "assets", "objects", "aa", ConcurrentAssetRepairHttpHandler.AssetHashes[0])));
        Assert.True(File.Exists(Path.Combine(minecraftDirectory, "assets", "objects", "dd", ConcurrentAssetRepairHttpHandler.AssetHashes[3])));
    }

    [Fact]
    public async Task ManagedVersionRepairDownloadsForgePatchedClientArtifactWhenArtifactUrlIsMissing()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", "Forge Pack");
        Directory.CreateDirectory(versionDirectory);
        await File.WriteAllTextAsync(Path.Combine(versionDirectory, "Forge Pack.jar"), "jar");
        await File.WriteAllTextAsync(
            Path.Combine(versionDirectory, "Forge Pack.json"),
            """
            {
              "id": "Forge Pack",
              "jar": "Forge Pack",
              "libraries": [
                {
                  "name": "net.minecraftforge:forge:26.1.2-64.0.9:client",
                  "downloads": {
                    "artifact": {
                      "path": "net/minecraftforge/forge/26.1.2-64.0.9/forge-26.1.2-64.0.9-client.jar",
                      "url": ""
                    }
                  }
                }
              ]
            }
            """);

        var repairService = new ManagedVersionRepairService(new HttpClient(new RepairHttpHandler()));
        await repairService.RepairAsync(
            minecraftDirectory,
            "Forge Pack",
            versionDirectory,
            progress: null,
            allowRepair: true,
            CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(
            minecraftDirectory,
            "libraries",
            "net",
            "minecraftforge",
            "forge",
            "26.1.2-64.0.9",
            "forge-26.1.2-64.0.9-client.jar")));
        Assert.Equal(["Forge Pack"], Directory.GetDirectories(Path.Combine(minecraftDirectory, "versions")).Select(Path.GetFileName));
    }

    [Fact]
    public async Task ManagedVersionRepairNormalizesDuplicatedLegacyMinecraftArguments()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", "RLCraft");
        Directory.CreateDirectory(versionDirectory);
        await File.WriteAllTextAsync(Path.Combine(versionDirectory, "RLCraft.jar"), "jar");
        await File.WriteAllTextAsync(
            Path.Combine(versionDirectory, "RLCraft.json"),
            """
            {
              "id": "RLCraft",
              "jar": "RLCraft",
              "mainClass": "net.minecraft.launchwrapper.Launch",
              "minecraftArguments": "--username ${auth_player_name} --version ${version_name} --gameDir ${game_directory} --assetsDir ${assets_root} --assetIndex ${assets_index_name} --uuid ${auth_uuid} --accessToken ${auth_access_token} --userType ${user_type} --versionType ${version_type} --username ${auth_player_name} --version ${version_name} --gameDir ${game_directory} --assetsDir ${assets_root} --assetIndex ${assets_index_name} --uuid ${auth_uuid} --accessToken ${auth_access_token} --userType ${user_type} --tweakClass net.minecraftforge.fml.common.launcher.FMLTweaker --versionType Forge",
              "libraries": []
            }
            """);

        var repairService = new ManagedVersionRepairService(new HttpClient(new RepairHttpHandler()));
        await repairService.RepairAsync(
            minecraftDirectory,
            "RLCraft",
            versionDirectory,
            progress: null,
            allowRepair: true,
            CancellationToken.None);

        var repairedJson = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(versionDirectory, "RLCraft.json")))!.AsObject();
        var arguments = repairedJson["minecraftArguments"]!.GetValue<string>();
        Assert.Equal(1, CountArgument(arguments, "--gameDir"));
        Assert.Equal(1, CountArgument(arguments, "--assetsDir"));
        Assert.Equal(1, CountArgument(arguments, "--accessToken"));
        Assert.Contains("--tweakClass net.minecraftforge.fml.common.launcher.FMLTweaker", arguments);
        Assert.Contains("--versionType Forge", arguments);
    }

    [Fact]
    public async Task ManagedVersionRepairFlattensInstanceFromLocalParentWithoutCreatingSiblingVersion()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var parentDirectory = Path.Combine(minecraftDirectory, "versions", "1.20.1");
        Directory.CreateDirectory(parentDirectory);
        await File.WriteAllTextAsync(Path.Combine(parentDirectory, "1.20.1.jar"), "base jar");
        await File.WriteAllTextAsync(
            Path.Combine(parentDirectory, "1.20.1.json"),
            """
            {
              "id": "1.20.1",
              "type": "release",
              "downloads": {
                "client": {
                  "url": "https://example.test/client.jar"
                }
              }
            }
            """);

        var versionDirectory = Path.Combine(minecraftDirectory, "versions", "External Pack");
        Directory.CreateDirectory(versionDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(versionDirectory, "External Pack.json"),
            """
            {
              "id": "child-pack",
              "inheritsFrom": "1.20.1",
              "mainClass": "net.fabricmc.loader.impl.launch.knot.KnotClient"
            }
            """);

        var repairService = new ManagedVersionRepairService(new HttpClient(new RepairHttpHandler()));
        await repairService.RepairAsync(
            minecraftDirectory,
            "External Pack",
            versionDirectory,
            progress: null,
            allowRepair: true,
            CancellationToken.None);

        var repairedJson = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(versionDirectory, "External Pack.json")))!.AsObject();
        Assert.False(repairedJson.ContainsKey("inheritsFrom"));
        Assert.Equal("External Pack", repairedJson["id"]!.GetValue<string>());
        Assert.Equal("External Pack", repairedJson["jar"]!.GetValue<string>());
        Assert.True(File.Exists(Path.Combine(versionDirectory, "External Pack.jar")));
        Assert.Equal(["1.20.1", "External Pack"], Directory.GetDirectories(Path.Combine(minecraftDirectory, "versions")).Select(Path.GetFileName).Order());
    }

    [Fact]
    public async Task ManagedVersionRepairFlattensInstanceFromRemoteVanillaMetadataWithoutCreatingSiblingVersion()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", "Imported Pack");
        Directory.CreateDirectory(versionDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(versionDirectory, "Imported Pack.json"),
            """
            {
              "id": "imported-pack",
              "inheritsFrom": "1.20.1",
              "mainClass": "net.minecraft.client.main.Main"
            }
            """);

        var repairService = new ManagedVersionRepairService(new HttpClient(new RepairHttpHandler()));
        await repairService.RepairAsync(
            minecraftDirectory,
            "Imported Pack",
            versionDirectory,
            progress: null,
            allowRepair: true,
            CancellationToken.None);

        var repairedJson = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(versionDirectory, "Imported Pack.json")))!.AsObject();
        Assert.False(repairedJson.ContainsKey("inheritsFrom"));
        Assert.True(File.Exists(Path.Combine(versionDirectory, "Imported Pack.jar")));
        Assert.Equal(["Imported Pack"], Directory.GetDirectories(Path.Combine(minecraftDirectory, "versions")).Select(Path.GetFileName));
    }

    [Fact]
    public async Task ManagedVersionRepairThrowsFriendlyFailureWhenParentCannotBeResolvedInPlace()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", "Broken Pack");
        Directory.CreateDirectory(versionDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(versionDirectory, "Broken Pack.json"),
            """
            {
              "id": "Broken Pack",
              "inheritsFrom": "missing-parent"
            }
            """);

        var repairService = new ManagedVersionRepairService(new HttpClient(new RepairHttpHandler(includeVanilla1201: false)));

        await Assert.ThrowsAsync<InstanceRepairException>(() => repairService.RepairAsync(
            minecraftDirectory,
            "Broken Pack",
            versionDirectory,
            progress: null,
            allowRepair: true,
            CancellationToken.None));

        Assert.Equal(["Broken Pack"], Directory.GetDirectories(Path.Combine(minecraftDirectory, "versions")).Select(Path.GetFileName));
    }

    [Fact]
    public async Task LaunchServiceRepairsInstanceBeforeBuildingProcess()
    {
        var repairService = new FakeManagedVersionRepairService();
        var launcherFactory = new FakeLaunchGameLauncherFactory();
        var service = new LaunchService(
            new FakeLaunchAccountSessionService(),
            repairService,
            launcherFactory,
            new NoOpLaunchCrashMonitor());
        var settings = new LauncherSettings
        {
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var instance = new GameInstance
        {
            Name = "Forge Pack",
            VersionName = "Forge Pack",
            InstanceDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "Forge Pack")
        };
        var account = new LauncherAccount
        {
            Id = "offline",
            DisplayName = "Player",
            Uuid = "00000000-0000-0000-0000-000000000001",
            IsOffline = true
        };

        await service.LaunchAsync(instance, account, settings, progress: null);

        Assert.Equal("Forge Pack", repairService.LastVersionName);
        Assert.Equal(instance.InstanceDirectory, repairService.LastInstanceDirectory);
        Assert.True(repairService.LastAllowRepair);
        Assert.Equal("Forge Pack", launcherFactory.Launcher.LastBuiltVersionName);
    }

    [Fact]
    public async Task LaunchServiceSkipsCheckWhenDisabledByGlobalSettings()
    {
        var repairService = new FakeManagedVersionRepairService();
        var launcherFactory = new FakeLaunchGameLauncherFactory();
        var service = new LaunchService(
            new FakeLaunchAccountSessionService(),
            repairService,
            launcherFactory,
            new NoOpLaunchCrashMonitor());
        var settings = new LauncherSettings
        {
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft"),
            DefaultCheckFilesBeforeLaunch = false,
            DefaultAutoRepairMissingFiles = true
        };
        var instance = new GameInstance
        {
            Name = "Forge Pack",
            VersionName = "Forge Pack",
            InstanceDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "Forge Pack"),
            LaunchSettingsMode = LaunchSettingsMode.UseGlobal
        };
        var account = new LauncherAccount
        {
            Id = "offline",
            DisplayName = "Player",
            Uuid = "00000000-0000-0000-0000-000000000001",
            IsOffline = true
        };

        await service.LaunchAsync(instance, account, settings, progress: null);

        Assert.Null(repairService.LastVersionName);
        Assert.Equal("Forge Pack", launcherFactory.Launcher.LastBuiltVersionName);
    }

    [Fact]
    public async Task LaunchServiceUsesCheckOnlyModeWhenAutoRepairDisabled()
    {
        var repairService = new FakeManagedVersionRepairService();
        var launcherFactory = new FakeLaunchGameLauncherFactory();
        var service = new LaunchService(
            new FakeLaunchAccountSessionService(),
            repairService,
            launcherFactory,
            new NoOpLaunchCrashMonitor());
        var settings = new LauncherSettings
        {
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft"),
            DefaultCheckFilesBeforeLaunch = true,
            DefaultAutoRepairMissingFiles = false
        };
        var instance = new GameInstance
        {
            Name = "Forge Pack",
            VersionName = "Forge Pack",
            InstanceDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "Forge Pack"),
            LaunchSettingsMode = LaunchSettingsMode.UseGlobal
        };
        var account = new LauncherAccount
        {
            Id = "offline",
            DisplayName = "Player",
            Uuid = "00000000-0000-0000-0000-000000000001",
            IsOffline = true
        };

        await service.LaunchAsync(instance, account, settings, progress: null);

        Assert.False(repairService.LastAllowRepair);
    }

    [Fact]
    public async Task LaunchServicePassesDownloadSourceAndSpeedLimitToRepairAndLauncher()
    {
        var repairService = new FakeManagedVersionRepairService();
        var launcherFactory = new FakeLaunchGameLauncherFactory();
        var service = new LaunchService(
            new FakeLaunchAccountSessionService(),
            repairService,
            launcherFactory,
            new NoOpLaunchCrashMonitor());
        var settings = new LauncherSettings
        {
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft"),
            DefaultCheckFilesBeforeLaunch = true,
            DefaultAutoRepairMissingFiles = true,
            DownloadSourcePreference = DownloadSourcePreference.BmclApi,
            DownloadSpeedLimitMbPerSecond = 20
        };
        var instance = new GameInstance
        {
            Name = "Fabric Pack",
            VersionName = "Fabric Pack",
            InstanceDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "Fabric Pack")
        };
        var account = new LauncherAccount
        {
            Id = "offline",
            DisplayName = "Player",
            Uuid = "00000000-0000-0000-0000-000000000001",
            IsOffline = true
        };

        await service.LaunchAsync(instance, account, settings, progress: null);

        Assert.Equal(DownloadSourcePreference.BmclApi, repairService.LastDownloadSourcePreference);
        Assert.Equal(20, repairService.LastDownloadSpeedLimitMbPerSecond);
        Assert.Equal(20, launcherFactory.LastDownloadSpeedLimitMbPerSecond);
    }

    [Fact]
    public async Task LaunchServiceAppliesFullscreenLaunchOptionFromEffectiveSettings()
    {
        var launcherFactory = new FakeLaunchGameLauncherFactory();
        var service = new LaunchService(
            new FakeLaunchAccountSessionService(),
            new FakeManagedVersionRepairService(),
            launcherFactory,
            new NoOpLaunchCrashMonitor());
        var settings = new LauncherSettings
        {
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft"),
            DefaultLaunchFullScreen = true
        };
        var instance = new GameInstance
        {
            Name = "Vanilla World",
            VersionName = "1.21.4",
            InstanceDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "1.21.4"),
            LaunchSettingsMode = LaunchSettingsMode.UseGlobal,
            LaunchFullScreen = false
        };
        var account = new LauncherAccount
        {
            Id = "offline",
            DisplayName = "Player",
            Uuid = "00000000-0000-0000-0000-000000000001",
            IsOffline = true
        };

        await service.LaunchAsync(instance, account, settings, progress: null);

        Assert.True(launcherFactory.Launcher.LastLaunchOption?.FullScreen);

        settings.DefaultLaunchFullScreen = false;
        instance.LaunchSettingsMode = LaunchSettingsMode.PerInstance;
        instance.LaunchFullScreen = true;

        await service.LaunchAsync(instance, account, settings, progress: null);

        Assert.True(launcherFactory.Launcher.LastLaunchOption?.FullScreen);
    }

    [Fact]
    public async Task LaunchServiceAppliesManualGlobalMemoryFromSettings()
    {
        var launcherFactory = new FakeLaunchGameLauncherFactory();
        var service = new LaunchService(
            new FakeLaunchAccountSessionService(),
            new FakeManagedVersionRepairService(),
            launcherFactory,
            new NoOpLaunchCrashMonitor());
        var settings = new LauncherSettings
        {
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft"),
            DefaultMemorySettingsMode = MemorySettingsMode.Manual,
            DefaultMemoryMb = 5120
        };
        var instance = new GameInstance
        {
            Name = "Vanilla World",
            VersionName = "1.21.4",
            InstanceDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "1.21.4"),
            LaunchSettingsMode = LaunchSettingsMode.UseGlobal,
            MemoryMb = 2048
        };

        await service.LaunchAsync(instance, CreateAccount(), settings, progress: null);

        Assert.Equal(5120, launcherFactory.Launcher.LastLaunchOption?.MaximumRamMb);
    }

    [Fact]
    public async Task LaunchServiceAppliesAutomaticGlobalMemoryFromSystemMemory()
    {
        var launcherFactory = new FakeLaunchGameLauncherFactory();
        var service = new LaunchService(
            new FakeLaunchAccountSessionService(),
            new FakeManagedVersionRepairService(),
            launcherFactory,
            new NoOpLaunchCrashMonitor(),
            systemMemoryService: new FakeSystemMemoryService(totalMemoryGb: 16, availableMemoryGb: 8),
            modService: new FakeModService(enabledModCount: 81, disabledModCount: 12));
        var settings = new LauncherSettings
        {
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft"),
            DefaultMemorySettingsMode = MemorySettingsMode.Auto,
            DefaultMemoryMb = 4096
        };
        var instance = new GameInstance
        {
            Name = "Vanilla World",
            VersionName = "1.21.4",
            InstanceDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "1.21.4"),
            LaunchSettingsMode = LaunchSettingsMode.UseGlobal,
            Loader = LoaderKind.Forge,
            MemoryMb = 2048
        };

        await service.LaunchAsync(instance, CreateAccount(), settings, progress: null);

        Assert.Equal(5632, launcherFactory.Launcher.LastLaunchOption?.MaximumRamMb);
    }

    [Fact]
    public async Task LaunchServiceUsesPerInstanceMemoryWhenLaunchSettingsArePerInstance()
    {
        var launcherFactory = new FakeLaunchGameLauncherFactory();
        var service = new LaunchService(
            new FakeLaunchAccountSessionService(),
            new FakeManagedVersionRepairService(),
            launcherFactory,
            new NoOpLaunchCrashMonitor(),
            systemMemoryService: new FakeSystemMemoryService(totalMemoryGb: 16, availableMemoryGb: 8));
        var settings = new LauncherSettings
        {
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft"),
            DefaultMemorySettingsMode = MemorySettingsMode.Auto,
            DefaultMemoryMb = 4096
        };
        var instance = new GameInstance
        {
            Name = "Vanilla World",
            VersionName = "1.21.4",
            InstanceDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "1.21.4"),
            LaunchSettingsMode = LaunchSettingsMode.PerInstance,
            MemoryMb = 6144
        };

        await service.LaunchAsync(instance, CreateAccount(), settings, progress: null);

        Assert.Equal(6144, launcherFactory.Launcher.LastLaunchOption?.MaximumRamMb);
    }

    [Fact]
    public async Task LaunchServiceUsesAutomaticPerInstanceMemoryWhenInstanceMemoryModeIsAuto()
    {
        var launcherFactory = new FakeLaunchGameLauncherFactory();
        var service = new LaunchService(
            new FakeLaunchAccountSessionService(),
            new FakeManagedVersionRepairService(),
            launcherFactory,
            new NoOpLaunchCrashMonitor(),
            systemMemoryService: new FakeSystemMemoryService(totalMemoryGb: 16, availableMemoryGb: 8),
            modService: new FakeModService(enabledModCount: 151));
        var settings = new LauncherSettings
        {
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft"),
            DefaultMemorySettingsMode = MemorySettingsMode.Manual,
            DefaultMemoryMb = 2048
        };
        var instance = new GameInstance
        {
            Name = "Forge Pack",
            VersionName = "Forge Pack",
            InstanceDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "Forge Pack"),
            LaunchSettingsMode = LaunchSettingsMode.PerInstance,
            MemorySettingsMode = MemorySettingsMode.Auto,
            Loader = LoaderKind.Forge,
            MemoryMb = 4096
        };

        await service.LaunchAsync(instance, CreateAccount(), settings, progress: null);

        Assert.Equal(6144, launcherFactory.Launcher.LastLaunchOption?.MaximumRamMb);
    }

    [Fact]
    public async Task LaunchServiceAppliesSelectedJavaRuntimeToLaunchOption()
    {
        var launcherFactory = new FakeLaunchGameLauncherFactory();
        var javaDirectory = Path.Combine(TempRoot, "java", "jdk-21", "bin");
        Directory.CreateDirectory(javaDirectory);
        var javaPath = Path.Combine(javaDirectory, "java.exe");
        var javawPath = Path.Combine(javaDirectory, "javaw.exe");
        await File.WriteAllTextAsync(javaPath, string.Empty);
        await File.WriteAllTextAsync(javawPath, string.Empty);
        var javaRuntime = new JavaRuntimeInfo(
            "Java 21",
            "21.0.0",
            21,
            "x64",
            javaPath,
            Path.Combine(TempRoot, "java", "jdk-21"),
            "Test");
        var service = new LaunchService(
            new FakeLaunchAccountSessionService(),
            new FakeManagedVersionRepairService(),
            launcherFactory,
            new NoOpLaunchCrashMonitor(),
            javaRuntimeSelectionService: new FakeJavaRuntimeSelectionService(javaRuntime));
        var settings = new LauncherSettings
        {
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft"),
            JavaSelectionMode = JavaSelectionMode.Auto
        };
        var instance = new GameInstance
        {
            Name = "Vanilla World",
            VersionName = "1.21.4",
            InstanceDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "1.21.4")
        };
        var account = new LauncherAccount
        {
            Id = "offline",
            DisplayName = "Player",
            Uuid = "00000000-0000-0000-0000-000000000001",
            IsOffline = true
        };

        await service.LaunchAsync(instance, account, settings, progress: null);

        Assert.Equal(javawPath, launcherFactory.Launcher.LastLaunchOption?.JavaPath);
    }

    [Fact]
    public async Task LaunchServicePassesManualJavaSelectionSettingsToSelectionService()
    {
        var launcherFactory = new FakeLaunchGameLauncherFactory();
        var javaDirectory = Path.Combine(TempRoot, "java", "jdk-17", "bin");
        Directory.CreateDirectory(javaDirectory);
        var javaPath = Path.Combine(javaDirectory, "java.exe");
        var javawPath = Path.Combine(javaDirectory, "javaw.exe");
        await File.WriteAllTextAsync(javaPath, string.Empty);
        await File.WriteAllTextAsync(javawPath, string.Empty);
        var javaRuntime = new JavaRuntimeInfo(
            "Java 17",
            "17.0.0",
            17,
            "x64",
            javaPath,
            Path.Combine(TempRoot, "java", "jdk-17"),
            "Test");
        var javaRuntimeSelectionService = new FakeJavaRuntimeSelectionService(javaRuntime);
        var service = new LaunchService(
            new FakeLaunchAccountSessionService(),
            new FakeManagedVersionRepairService(),
            launcherFactory,
            new NoOpLaunchCrashMonitor(),
            javaRuntimeSelectionService: javaRuntimeSelectionService);
        var settings = new LauncherSettings
        {
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft"),
            JavaSelectionMode = JavaSelectionMode.Manual,
            SelectedJavaExecutablePath = javaRuntime.ExecutablePath
        };
        var instance = new GameInstance
        {
            Name = "Vanilla World",
            VersionName = "1.20.1",
            InstanceDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "1.20.1")
        };
        var account = new LauncherAccount
        {
            Id = "offline",
            DisplayName = "Player",
            Uuid = "00000000-0000-0000-0000-000000000001",
            IsOffline = true
        };

        await service.LaunchAsync(instance, account, settings, progress: null);

        Assert.Same(settings, javaRuntimeSelectionService.LastSettings);
        Assert.Equal(JavaSelectionMode.Manual, javaRuntimeSelectionService.LastSettings?.JavaSelectionMode);
        Assert.Equal(javawPath, launcherFactory.Launcher.LastLaunchOption?.JavaPath);
    }

    [Fact]
    public void ResolveWindowlessJavaPathKeepsJavaExeWhenJavawIsMissing()
    {
        var javaDirectory = Path.Combine(TempRoot, "java", "jdk-8", "bin");
        Directory.CreateDirectory(javaDirectory);
        var javaPath = Path.Combine(javaDirectory, "java.exe");
        File.WriteAllText(javaPath, string.Empty);

        Assert.Equal(javaPath, LaunchService.ResolveWindowlessJavaPath(javaPath));
    }

    [Fact]
    public async Task LaunchServiceAppliesAdvancedLaunchSettingsFromEffectiveSettings()
    {
        var launcherFactory = new FakeLaunchGameLauncherFactory();
        var commandRunner = new FakeLaunchCommandRunner();
        var service = new LaunchService(
            new FakeLaunchAccountSessionService(),
            new FakeManagedVersionRepairService(),
            launcherFactory,
            new NoOpLaunchCrashMonitor(),
            commandRunner);
        var settings = new LauncherSettings
        {
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft"),
            DefaultPreLaunchCommand = "echo global-before",
            DefaultWaitForPreLaunchCommand = false,
            DefaultPostExitCommand = "echo global-after",
            DefaultJvmArguments = "-Dglobal=true",
            DefaultGameArguments = "--global"
        };
        var instance = new GameInstance
        {
            Name = "Vanilla World",
            VersionName = "1.21.4",
            InstanceDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "1.21.4"),
            LaunchSettingsMode = LaunchSettingsMode.UseGlobal,
            PreLaunchCommand = "echo instance-before",
            WaitForPreLaunchCommand = true,
            PostExitCommand = "echo instance-after",
            JvmArguments = "-Dinstance=true",
            GameArguments = "--instance"
        };
        Directory.CreateDirectory(instance.InstanceDirectory);
        var account = new LauncherAccount
        {
            Id = "offline",
            DisplayName = "Player",
            Uuid = "00000000-0000-0000-0000-000000000001",
            IsOffline = true
        };
        var progress = new RecordingProgress();

        await service.LaunchAsync(instance, account, settings, progress);
        await TestAsync.WaitForAsync(() => commandRunner.CommandCount >= 2);

        Assert.Contains(progress.Items, item => item.Stage == LaunchProgressStages.RunningPreLaunchCommand);
        Assert.Equal("-Dglobal=true", FlattenArguments(launcherFactory.Launcher.LastLaunchOption?.ExtraJvmArguments));
        Assert.Equal("--global", FlattenArguments(launcherFactory.Launcher.LastLaunchOption?.ExtraGameArguments));
        var globalCommands = commandRunner.Snapshot();
        Assert.Contains(globalCommands, command => command.Command == "echo global-before" && !command.WaitForExit);
        Assert.Contains(globalCommands, command => command.Command == "echo global-after" && !command.WaitForExit);

        commandRunner.Clear();
        instance.LaunchSettingsMode = LaunchSettingsMode.PerInstance;

        await service.LaunchAsync(instance, account, settings, progress: null);
        await TestAsync.WaitForAsync(() => commandRunner.CommandCount >= 2);

        Assert.Equal("-Dinstance=true", FlattenArguments(launcherFactory.Launcher.LastLaunchOption?.ExtraJvmArguments));
        Assert.Equal("--instance", FlattenArguments(launcherFactory.Launcher.LastLaunchOption?.ExtraGameArguments));
        var instanceCommands = commandRunner.Snapshot();
        Assert.Contains(instanceCommands, command => command.Command == "echo instance-before" && command.WaitForExit);
        Assert.Contains(instanceCommands, command => command.Command == "echo instance-after" && !command.WaitForExit);
    }

    [Fact]
    public async Task ManagedVersionRepairThrowsWhenJarIsMissingAndAutoRepairDisabled()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", "Forge Pack");
        Directory.CreateDirectory(versionDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(versionDirectory, "Forge Pack.json"),
            """
            {
              "id": "Forge Pack",
              "jar": "Forge Pack",
              "downloads": {
                "client": {
                  "url": "https://example.test/client.jar"
                }
              }
            }
            """);

        var repairService = new ManagedVersionRepairService(new HttpClient(new RepairHttpHandler()));

        await Assert.ThrowsAsync<InstanceRepairException>(() => repairService.RepairAsync(
            minecraftDirectory,
            "Forge Pack",
            versionDirectory,
            progress: null,
            allowRepair: false,
            CancellationToken.None));

        Assert.False(File.Exists(Path.Combine(versionDirectory, "Forge Pack.jar")));
    }

    [Fact]
    public async Task ManagedVersionRepairReportsCheckingFilesWhenAutoRepairDisabled()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", "Fabric Pack");
        Directory.CreateDirectory(versionDirectory);
        await File.WriteAllTextAsync(Path.Combine(versionDirectory, "Fabric Pack.jar"), "jar");
        await File.WriteAllTextAsync(
            Path.Combine(versionDirectory, "Fabric Pack.json"),
            """
            {
              "id": "Fabric Pack",
              "jar": "Fabric Pack",
              "libraries": [],
              "assetIndex": {
                "id": "1.20.1",
                "url": "https://example.test/assets/index.json"
              }
            }
            """);
        Directory.CreateDirectory(Path.Combine(minecraftDirectory, "assets", "indexes"));
        await File.WriteAllTextAsync(
            Path.Combine(minecraftDirectory, "assets", "indexes", "1.20.1.json"),
            """
            {
              "objects": {}
            }
            """);

        var repairService = new ManagedVersionRepairService(new HttpClient(new RepairHttpHandler()));
        var progress = new RecordingProgress();

        await repairService.RepairAsync(
            minecraftDirectory,
            "Fabric Pack",
            versionDirectory,
            progress,
            allowRepair: false,
            CancellationToken.None);

        Assert.Contains(progress.Items, item => item.Stage == LaunchProgressStages.CheckingFiles);
        Assert.DoesNotContain(progress.Items, item => item.Stage == LaunchProgressStages.RepairingAssets);
    }

    [Fact]
    public async Task LaunchServiceReportsLaunchStagesWithMonotonicPercent()
    {
        var repairService = new FakeManagedVersionRepairService
        {
            OnRepairAsync = progress =>
            {
                progress?.Report(new LauncherProgress(
                    LaunchProgressStages.RepairingAssets,
                    "Repairing shared assets",
                    64));
                return Task.CompletedTask;
            }
        };
        var launcherFactory = new FakeLaunchGameLauncherFactory();
        var service = new LaunchService(
            new FakeLaunchAccountSessionService(),
            repairService,
            launcherFactory,
            new NoOpLaunchCrashMonitor());
        var settings = new LauncherSettings
        {
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var instance = new GameInstance
        {
            Name = "Forge Pack",
            VersionName = "Forge Pack",
            InstanceDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "Forge Pack")
        };
        var account = new LauncherAccount
        {
            Id = "offline",
            DisplayName = "Player",
            Uuid = "00000000-0000-0000-0000-000000000001",
            IsOffline = true
        };
        var progress = new RecordingProgress();

        await service.LaunchAsync(
            instance,
            account,
            settings,
            progress);

        Assert.Equal(
            [LaunchProgressStages.RepairingAssets, LaunchProgressStages.PreparingProcess, LaunchProgressStages.StartingProcess],
            progress.Items.Select(item => item.Stage));
        AssertProgressPercentIsMonotonic(progress.Items);
    }

    [Fact]
    public async Task LaunchServiceWritesDiagnosticLogWhenProcessExitsQuickly()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var instanceDirectory = Path.Combine(minecraftDirectory, "versions", "Forge Pack");
        Directory.CreateDirectory(Path.Combine(instanceDirectory, "logs"));
        await File.WriteAllTextAsync(
            Path.Combine(instanceDirectory, "logs", "latest.log"),
            """
            [12:00:00] [main/INFO]: Starting Forge client
            [12:00:01] [main/ERROR]: Missing launch target
            """);

        var launcherFactory = new FakeLaunchGameLauncherFactory
        {
            BuildProcess = (_, _) => new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                    Arguments = "/c echo Missing launch target 1>&2 & exit 1",
                    CreateNoWindow = true,
                    UseShellExecute = false
                }
            }
        };
        var service = new LaunchService(
            new FakeLaunchAccountSessionService(),
            new FakeManagedVersionRepairService(),
            launcherFactory,
            new LaunchCrashMonitor(TimeSpan.FromSeconds(2)));
        var settings = new LauncherSettings
        {
            MinecraftDirectory = minecraftDirectory
        };
        var instance = new GameInstance
        {
            Name = "Forge Pack",
            VersionName = "Forge Pack",
            InstanceDirectory = instanceDirectory
        };
        var account = new LauncherAccount
        {
            Id = "offline",
            DisplayName = "Player",
            Uuid = "00000000-0000-0000-0000-000000000001",
            IsOffline = true
        };

        var exception = await Assert.ThrowsAsync<LaunchProcessExitedException>(() => service.LaunchAsync(
            instance,
            account,
            settings,
            progress: null));

        Assert.False(string.IsNullOrWhiteSpace(exception.DiagnosticPath));
        Assert.True(File.Exists(exception.DiagnosticPath));
        var diagnostic = await File.ReadAllTextAsync(exception.DiagnosticPath);
        Assert.Equal(LaunchFailureKind.StartupAbnormalExit, exception.Report.Kind);
        Assert.Equal(1, exception.Report.ExitCode);
        Assert.Contains("FailureKind: startup_abnormal_exit", diagnostic);
        Assert.Contains("FailureSummary: Missing launch target", diagnostic);
        Assert.Contains("VersionName: Forge Pack", diagnostic);
        Assert.Contains("MinecraftVersion:", diagnostic);
        Assert.Contains("Loader: Vanilla", diagnostic);
        Assert.Contains("MemoryMb:", diagnostic);
        Assert.DoesNotContain("super-secret-access-token", diagnostic);
        Assert.Contains("[MatchedErrorLines]", diagnostic);
        Assert.Contains("[Process]", diagnostic);
        Assert.Contains("Missing launch target", diagnostic);
        Assert.Contains("LatestLogTail", diagnostic);
    }

    [Fact]
    public async Task LaunchServiceWritesDiagnosticLogWhenRepairFailsBeforeProcessStarts()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var instanceDirectory = Path.Combine(minecraftDirectory, "versions", "Broken Pack");
        Directory.CreateDirectory(instanceDirectory);
        var launcherFactory = new FakeLaunchGameLauncherFactory();
        var service = new LaunchService(
            new FakeLaunchAccountSessionService(),
            new FakeManagedVersionRepairService
            {
                OnRepairAsync = _ => throw new InstanceRepairException("No usable Java runtime was found.")
            },
            launcherFactory,
            new NoOpLaunchCrashMonitor());
        var settings = new LauncherSettings
        {
            MinecraftDirectory = minecraftDirectory
        };
        var instance = new GameInstance
        {
            Name = "Broken Pack",
            VersionName = "Broken Pack",
            InstanceDirectory = instanceDirectory
        };
        var account = new LauncherAccount
        {
            Id = "offline",
            DisplayName = "Player",
            Uuid = "00000000-0000-0000-0000-000000000001",
            IsOffline = true
        };

        var exception = await Assert.ThrowsAsync<LaunchFailedException>(() => service.LaunchAsync(
            instance,
            account,
            settings,
            progress: null));

        Assert.IsType<InstanceRepairException>(exception.InnerException);
        Assert.Equal(LaunchFailureKind.StartupFailed, exception.Report.Kind);
        Assert.False(string.IsNullOrWhiteSpace(exception.Report.DiagnosticPath));
        var logsDirectory = Path.Combine(instanceDirectory, "logs", "launcher");
        var diagnosticPath = Directory.GetFiles(logsDirectory, "launch-diagnostics-*.log").Single();
        var diagnostic = await File.ReadAllTextAsync(diagnosticPath);
        Assert.Contains("FailureKind: instance_repair_failed", diagnostic);
        Assert.Contains("FailureSummary: No usable Java runtime was found.", diagnostic);
        Assert.Contains("[ExceptionChain]", diagnostic);
        Assert.Contains("InstanceRepairException", diagnostic);
    }

    [Fact]
    public async Task LaunchServiceAnalyzesMissingClientJarRepairFailure()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var instanceDirectory = Path.Combine(minecraftDirectory, "versions", "1.21.9-fabric-0.19.3");
        Directory.CreateDirectory(instanceDirectory);
        var service = new LaunchService(
            new FakeLaunchAccountSessionService(),
            new FakeManagedVersionRepairService
            {
                OnRepairAsync = _ => throw new InstanceRepairException(
                    "Version 1.21.9-fabric-0.19.3 is missing its client jar and automatic repair is disabled.")
            },
            new FakeLaunchGameLauncherFactory(),
            new NoOpLaunchCrashMonitor());

        var exception = await Assert.ThrowsAsync<LaunchFailedException>(() => service.LaunchAsync(
            CreateInstance(instanceDirectory, "1.21.9-fabric-0.19.3"),
            CreateAccount(),
            new LauncherSettings { MinecraftDirectory = minecraftDirectory },
            progress: null));

        var expectedMissingPath = Path.Combine(instanceDirectory, "1.21.9-fabric-0.19.3.jar");
        Assert.Equal(LaunchFailureKind.StartupFailed, exception.Report.Kind);
        Assert.NotNull(exception.Report.Analysis);
        Assert.Equal(LaunchFailureCategory.MissingGameFiles, exception.Report.Analysis.Category);
        Assert.Equal("missing_client_jar", exception.Report.Analysis.ReasonDetail);
        Assert.Equal(expectedMissingPath, exception.Report.Analysis.MissingPath);
        var diagnostic = await File.ReadAllTextAsync(exception.Report.DiagnosticPath!);
        Assert.Contains("Category: MissingGameFiles", diagnostic);
        Assert.Contains($"MissingPath: {expectedMissingPath}", diagnostic);
    }

    [Fact]
    public async Task LaunchServiceKeepsOnlyLatestFiftyDiagnosticLogs()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var instanceDirectory = Path.Combine(minecraftDirectory, "versions", "Broken Pack");
        var logsDirectory = Path.Combine(instanceDirectory, "logs", "launcher");
        Directory.CreateDirectory(logsDirectory);
        var oldLogPaths = new List<string>();
        for (var index = 0; index < 55; index++)
        {
            var path = Path.Combine(logsDirectory, $"launch-diagnostics-20260101-0000{index:D2}.log");
            await File.WriteAllTextAsync(path, $"old {index}");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-120 + index));
            oldLogPaths.Add(path);
        }

        var service = new LaunchService(
            new FakeLaunchAccountSessionService(),
            new FakeManagedVersionRepairService
            {
                OnRepairAsync = _ => throw new InstanceRepairException("No usable Java runtime was found.")
            },
            new FakeLaunchGameLauncherFactory(),
            new NoOpLaunchCrashMonitor());

        var exception = await Assert.ThrowsAsync<LaunchFailedException>(() => service.LaunchAsync(
            CreateInstance(instanceDirectory, "Broken Pack"),
            CreateAccount(),
            new LauncherSettings { MinecraftDirectory = minecraftDirectory },
            progress: null));

        var diagnostics = Directory.GetFiles(logsDirectory, "launch-diagnostics-*.log");
        Assert.Equal(50, diagnostics.Length);
        Assert.Contains(exception.Report.DiagnosticPath!, diagnostics);
        Assert.DoesNotContain(oldLogPaths[0], diagnostics);
        Assert.DoesNotContain(oldLogPaths[5], diagnostics);
        Assert.Contains(oldLogPaths[6], diagnostics);
    }

    [Fact]
    public async Task LaunchServiceDiagnosticIncludesRepairDownloadContext()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var instanceDirectory = Path.Combine(minecraftDirectory, "versions", "Forge Pack");
        Directory.CreateDirectory(instanceDirectory);
        await File.WriteAllTextAsync(Path.Combine(instanceDirectory, "Forge Pack.jar"), "jar");
        const string libraryName = "net.minecraftforge:forge:1.21.11-61.1.8:client";
        const string artifactPath = "net/minecraftforge/forge/1.21.11-61.1.8/forge-1.21.11-61.1.8-client.jar";
        var downloadUrl = $"https://maven.minecraftforge.net/{artifactPath}";
        var expectedDestinationPath = Path.Combine(
            minecraftDirectory,
            "libraries",
            artifactPath.Replace('/', Path.DirectorySeparatorChar));
        await File.WriteAllTextAsync(
            Path.Combine(instanceDirectory, "Forge Pack.json"),
            $$"""
            {
              "id": "Forge Pack",
              "jar": "Forge Pack",
              "libraries": [
                {
                  "name": "{{libraryName}}",
                  "downloads": {
                    "artifact": {
                      "url": "{{downloadUrl}}",
                      "path": "{{artifactPath}}"
                    }
                  }
                }
              ]
            }
            """);
        var service = new LaunchService(
            new FakeLaunchAccountSessionService(),
            new ManagedVersionRepairService(new HttpClient(new NotFoundDownloadHandler(downloadUrl))),
            new FakeLaunchGameLauncherFactory(),
            new NoOpLaunchCrashMonitor());

        var exception = await Assert.ThrowsAsync<LaunchFailedException>(() => service.LaunchAsync(
            CreateInstance(instanceDirectory, "Forge Pack"),
            CreateAccount(),
            new LauncherSettings
            {
                MinecraftDirectory = minecraftDirectory,
                DefaultCheckFilesBeforeLaunch = true,
                DefaultAutoRepairMissingFiles = true,
                DownloadSourcePreference = DownloadSourcePreference.Official
            },
            progress: null));

        Assert.IsType<InstanceRepairException>(exception.InnerException);
        var repairException = (InstanceRepairException)exception.InnerException!;
        Assert.NotNull(repairException.DownloadDiagnostic);
        Assert.Equal(downloadUrl, repairException.DownloadDiagnostic.OriginalUrl);
        Assert.Equal(downloadUrl, repairException.DownloadDiagnostic.ActualUrl);
        Assert.Equal(expectedDestinationPath, repairException.DownloadDiagnostic.DestinationPath);
        Assert.Equal(404, repairException.DownloadDiagnostic.HttpStatusCode);
        Assert.Equal(libraryName, repairException.DownloadDiagnostic.LibraryName);
        Assert.Equal(artifactPath, repairException.DownloadDiagnostic.ArtifactPath);
        Assert.Equal(DownloadSourcePreference.Official.ToString(), repairException.DownloadDiagnostic.RequestedSourcePreference);
        Assert.Equal("ForgeOfficial", repairException.DownloadDiagnostic.ResolvedSourceKind);
        Assert.Equal("Forge", repairException.DownloadDiagnostic.ResourceCategory);

        var diagnostic = await File.ReadAllTextAsync(exception.Report.DiagnosticPath!);
        Assert.Contains("[Download]", diagnostic);
        Assert.Contains($"OriginalUrl: {downloadUrl}", diagnostic);
        Assert.Contains($"ActualUrl: {downloadUrl}", diagnostic);
        Assert.Contains($"DestinationPath: {expectedDestinationPath}", diagnostic);
        Assert.Contains("HttpStatusCode: 404", diagnostic);
        Assert.Contains($"LibraryName: {libraryName}", diagnostic);
        Assert.Contains($"ArtifactPath: {artifactPath}", diagnostic);
        Assert.Contains($"RequestedSourcePreference: {DownloadSourcePreference.Official}", diagnostic);
        Assert.Contains("ResolvedSourceKind: ForgeOfficial", diagnostic);
        Assert.Contains("ResourceCategory: Forge", diagnostic);
    }

    [Fact]
    public async Task LaunchServiceClassifiesZeroQuickExitWithoutNewCrashAsStartupProcessExited()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var instanceDirectory = Path.Combine(minecraftDirectory, "versions", "Vanilla Pack");
        Directory.CreateDirectory(instanceDirectory);
        var service = new LaunchService(
            new FakeLaunchAccountSessionService(),
            new FakeManagedVersionRepairService(),
            new FakeLaunchGameLauncherFactory
            {
                BuildProcess = (_, _) => CreateCommandProcess("/c exit 0")
            },
            new LaunchCrashMonitor(TimeSpan.FromSeconds(2)));

        var exception = await Assert.ThrowsAsync<LaunchProcessExitedException>(() => service.LaunchAsync(
            CreateInstance(instanceDirectory, "Vanilla Pack"),
            CreateAccount(),
            new LauncherSettings { MinecraftDirectory = minecraftDirectory },
            progress: null));

        Assert.Equal(LaunchFailureKind.StartupProcessExited, exception.Report.Kind);
        Assert.Equal(0, exception.Report.ExitCode);
    }

    [Fact]
    public async Task LaunchServiceIgnoresOldCrashReportsWhenClassifyingQuickExit()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var instanceDirectory = Path.Combine(minecraftDirectory, "versions", "Vanilla Pack");
        var crashDirectory = Path.Combine(instanceDirectory, "crash-reports");
        Directory.CreateDirectory(crashDirectory);
        var oldCrashPath = Path.Combine(crashDirectory, "crash-old.txt");
        await File.WriteAllTextAsync(oldCrashPath, "old crash");
        File.SetLastWriteTimeUtc(oldCrashPath, DateTime.UtcNow.AddMinutes(-5));
        var service = new LaunchService(
            new FakeLaunchAccountSessionService(),
            new FakeManagedVersionRepairService(),
            new FakeLaunchGameLauncherFactory
            {
                BuildProcess = (_, _) => CreateCommandProcess("/c exit 0")
            },
            new LaunchCrashMonitor(TimeSpan.FromSeconds(2)));

        var exception = await Assert.ThrowsAsync<LaunchProcessExitedException>(() => service.LaunchAsync(
            CreateInstance(instanceDirectory, "Vanilla Pack"),
            CreateAccount(),
            new LauncherSettings { MinecraftDirectory = minecraftDirectory },
            progress: null));

        Assert.Equal(LaunchFailureKind.StartupProcessExited, exception.Report.Kind);
    }

    [Fact]
    public async Task LaunchServiceTreatsNewCrashReportAsAbnormalEvenWithZeroExitCode()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var instanceDirectory = Path.Combine(minecraftDirectory, "versions", "Vanilla Pack");
        var crashDirectory = Path.Combine(instanceDirectory, "crash-reports");
        Directory.CreateDirectory(crashDirectory);
        var crashPath = Path.Combine(crashDirectory, "crash-new.txt");
        var service = new LaunchService(
            new FakeLaunchAccountSessionService(),
            new FakeManagedVersionRepairService(),
            new FakeLaunchGameLauncherFactory
            {
                BuildProcess = (_, _) => CreateCommandProcess($"/c echo crash> \"{crashPath}\" & exit 0")
            },
            new LaunchCrashMonitor(TimeSpan.FromSeconds(2)));

        var exception = await Assert.ThrowsAsync<LaunchProcessExitedException>(() => service.LaunchAsync(
            CreateInstance(instanceDirectory, "Vanilla Pack"),
            CreateAccount(),
            new LauncherSettings { MinecraftDirectory = minecraftDirectory },
            progress: null));

        Assert.Equal(LaunchFailureKind.StartupAbnormalExit, exception.Report.Kind);
        Assert.Equal(0, exception.Report.ExitCode);
    }

    [Fact]
    public async Task LaunchServiceReturnsSuccessfulExitForStableZeroExitWithoutNewCrash()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var instanceDirectory = Path.Combine(minecraftDirectory, "versions", "Stable Pack");
        Directory.CreateDirectory(instanceDirectory);
        var service = new LaunchService(
            new FakeLaunchAccountSessionService(),
            new FakeManagedVersionRepairService(),
            new FakeLaunchGameLauncherFactory
            {
                BuildProcess = (_, _) => CreateCommandProcess("/c ping -n 2 127.0.0.1 > nul & exit 0")
            },
            new LaunchCrashMonitor(TimeSpan.FromMilliseconds(150)));

        var session = await service.LaunchAsync(
            CreateInstance(instanceDirectory, "Stable Pack"),
            CreateAccount(),
            new LauncherSettings { MinecraftDirectory = minecraftDirectory },
            progress: null);

        var result = await session.ExitTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(result.IsFailure);
    }

    [Fact]
    public async Task LaunchServiceReportsStableNonZeroExitOnlyOnce()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var instanceDirectory = Path.Combine(minecraftDirectory, "versions", "Stable Pack");
        Directory.CreateDirectory(Path.Combine(instanceDirectory, "logs"));
        var latestLogPath = Path.Combine(instanceDirectory, "logs", "latest.log");
        await File.WriteAllTextAsync(
            latestLogPath,
            """
            [main/ERROR]: Incompatible mods found!
            More details:
             - Mod 'Fabric API' (fabric-api) 0.134.1+1.21.9 requires version 21 or later of 'Java HotSpot(TM) 64-Bit Server VM' (java), but only the wrong version is present: 8!
            """);
        var service = new LaunchService(
            new FakeLaunchAccountSessionService(),
            new FakeManagedVersionRepairService(),
            new FakeLaunchGameLauncherFactory
            {
                BuildProcess = (_, _) => CreateCommandProcess("/c ping -n 2 127.0.0.1 > nul & exit 2")
            },
            new LaunchCrashMonitor(TimeSpan.FromMilliseconds(150)));

        var session = await service.LaunchAsync(
            CreateInstance(instanceDirectory, "Stable Pack"),
            CreateAccount(),
            new LauncherSettings { MinecraftDirectory = minecraftDirectory },
            progress: null);
        File.SetLastWriteTimeUtc(latestLogPath, DateTime.UtcNow.AddSeconds(1));

        var result = await session.ExitTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(result.IsFailure);
        Assert.Equal(LaunchFailureKind.RuntimeAbnormalExit, result.FailureReport?.Kind);
        Assert.Equal(2, result.FailureReport?.ExitCode);
        Assert.Equal(LaunchFailureCategory.JavaVersionMismatch, result.FailureReport?.Analysis?.Category);
        Assert.Equal(21, result.FailureReport?.Analysis?.RequiredJavaMajorVersion);
        Assert.Equal(8, result.FailureReport?.Analysis?.CurrentJavaMajorVersion);
        var diagnostic = await File.ReadAllTextAsync(result.FailureReport!.DiagnosticPath!);
        Assert.Contains("[Analysis]", diagnostic);
        Assert.Contains("Category: JavaVersionMismatch", diagnostic);
        Assert.True(session.TryMarkExitHandled());
        Assert.False(session.TryMarkExitHandled());
    }

    private sealed class FakeLaunchAccountSessionService : ILaunchAccountSessionService
    {
        public Task<LaunchAccountSession> CreateSessionAsync(LauncherAccount account, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new LaunchAccountSession(
                account.DisplayName,
                "super-secret-access-token",
                account.Uuid ?? string.Empty,
                account.IsOffline));
        }
    }

    private static Process CreateCommandProcess(string arguments)
    {
        return new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false
            }
        };
    }

    private static GameInstance CreateInstance(string instanceDirectory, string name)
    {
        return new GameInstance
        {
            Id = name,
            Name = name,
            MinecraftVersion = "1.20.1",
            VersionName = name,
            InstanceDirectory = instanceDirectory,
            Loader = LoaderKind.Vanilla,
            MemoryMb = 4096
        };
    }

    private static LauncherAccount CreateAccount()
    {
        return new LauncherAccount
        {
            Id = "offline",
            DisplayName = "Player",
            Uuid = "00000000-0000-0000-0000-000000000001",
            IsOffline = true
        };
    }

    private sealed class FakeManagedVersionRepairService : IManagedVersionRepairService
    {
        public string? LastVersionName { get; private set; }
        public string? LastInstanceDirectory { get; private set; }
        public bool LastAllowRepair { get; private set; }
        public DownloadSourcePreference LastDownloadSourcePreference { get; private set; } = DownloadSourcePreference.Auto;
        public int LastDownloadSpeedLimitMbPerSecond { get; private set; }
        public Func<IProgress<LauncherProgress>?, Task>? OnRepairAsync { get; init; }

        public Task RepairAsync(
            string minecraftDirectory,
            string versionName,
            string instanceDirectory,
            IProgress<LauncherProgress>? progress,
            bool allowRepair,
            CancellationToken cancellationToken = default,
            DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            LastVersionName = versionName;
            LastInstanceDirectory = instanceDirectory;
            LastAllowRepair = allowRepair;
            LastDownloadSourcePreference = downloadSourcePreference;
            LastDownloadSpeedLimitMbPerSecond = downloadSpeedLimitMbPerSecond;
            if (OnRepairAsync is not null)
                return OnRepairAsync(progress);

            return Task.CompletedTask;
        }
    }

    private sealed class FakeJavaRuntimeSelectionService : IJavaRuntimeSelectionService
    {
        private readonly JavaRuntimeInfo runtime;

        public FakeJavaRuntimeSelectionService(JavaRuntimeInfo runtime)
        {
            this.runtime = runtime;
        }

        public LauncherSettings? LastSettings { get; private set; }

        public Task<JavaRuntimeInfo> SelectForLaunchAsync(
            GameInstance instance,
            LauncherSettings settings,
            CancellationToken cancellationToken = default)
        {
            LastSettings = settings;
            return Task.FromResult(runtime);
        }
    }

    private sealed class FakeSystemMemoryService : ISystemMemoryService
    {
        private readonly int totalMemoryGb;
        private readonly int availableMemoryGb;

        public FakeSystemMemoryService(int totalMemoryGb, int availableMemoryGb)
        {
            this.totalMemoryGb = totalMemoryGb;
            this.availableMemoryGb = availableMemoryGb;
        }

        public SystemMemorySnapshot GetSnapshot()
        {
            return new SystemMemorySnapshot(
                TotalMemoryBytes: totalMemoryGb * 1024L * 1024L * 1024L,
                AvailableMemoryBytes: availableMemoryGb * 1024L * 1024L * 1024L);
        }
    }

    private sealed class FakeModService : IModService
    {
        private readonly int enabledModCount;
        private readonly int disabledModCount;

        public FakeModService(int enabledModCount, int disabledModCount = 0)
        {
            this.enabledModCount = enabledModCount;
            this.disabledModCount = disabledModCount;
        }

        public Task<IReadOnlyList<LocalMod>> GetModsAsync(GameInstance instance, CancellationToken cancellationToken = default)
        {
            var mods = Enumerable
                .Range(0, enabledModCount)
                .Select(index => new LocalMod { Name = $"enabled-{index}", IsEnabled = true })
                .Concat(Enumerable
                    .Range(0, disabledModCount)
                    .Select(index => new LocalMod { Name = $"disabled-{index}", IsEnabled = false }))
                .ToList();

            return Task.FromResult<IReadOnlyList<LocalMod>>(mods);
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

    private sealed class FakeLaunchGameLauncherFactory : ILaunchGameLauncherFactory
    {
        public FakeLaunchGameLauncher Launcher { get; } = new();
        public Func<string, MLaunchOption, Process>? BuildProcess { get; init; }
        public int LastDownloadSpeedLimitMbPerSecond { get; private set; }

        public ILaunchGameLauncher Create(
            string minecraftDirectory,
            IProgress<LauncherProgress>? progress,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            LastDownloadSpeedLimitMbPerSecond = downloadSpeedLimitMbPerSecond;
            if (BuildProcess is not null)
                Launcher.BuildProcess = BuildProcess;

            return Launcher;
        }
    }

    private sealed class FakeLaunchGameLauncher : ILaunchGameLauncher
    {
        public string? LastBuiltVersionName { get; private set; }
        public MLaunchOption? LastLaunchOption { get; private set; }
        public Func<string, MLaunchOption, Process>? BuildProcess { get; set; }

        public ValueTask<Process> BuildProcessAsync(string versionName, MLaunchOption launchOption, CancellationToken cancellationToken)
        {
            LastBuiltVersionName = versionName;
            LastLaunchOption = launchOption;
            if (BuildProcess is not null)
                return ValueTask.FromResult(BuildProcess(versionName, launchOption));

            return ValueTask.FromResult(new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                    Arguments = "/c exit 0",
                    CreateNoWindow = true,
                    UseShellExecute = false
                }
            });
        }
    }

    private sealed class FakeLaunchCommandRunner : ILaunchCommandRunner
    {
        private readonly object syncRoot = new();

        private readonly List<(string Command, string WorkingDirectory, bool WaitForExit)> commands = [];

        public int CommandCount
        {
            get
            {
                lock (syncRoot)
                {
                    return commands.Count;
                }
            }
        }

        public Task RunAsync(string command, string workingDirectory, bool waitForExit, CancellationToken cancellationToken)
        {
            lock (syncRoot)
            {
                commands.Add((command, workingDirectory, waitForExit));
            }

            return Task.CompletedTask;
        }

        public IReadOnlyList<(string Command, string WorkingDirectory, bool WaitForExit)> Snapshot()
        {
            lock (syncRoot)
            {
                return commands.ToList();
            }
        }

        public void Clear()
        {
            lock (syncRoot)
            {
                commands.Clear();
            }
        }
    }

    private sealed class RepairHttpHandler : HttpMessageHandler
    {
        private readonly bool includeVanilla1201;

        public RepairHttpHandler(bool includeVanilla1201 = true)
        {
            this.includeVanilla1201 = includeVanilla1201;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!.AbsoluteUri;
            return Task.FromResult(uri switch
            {
                "https://example.test/client.jar" => CreateBinaryResponse(request, "client jar"),
                "https://example.test/libraries/example.jar" => CreateBinaryResponse(request, "library"),
                "https://example.test/assets/index.json" => CreateJsonResponse(request, """
                    {
                      "objects": {
                        "minecraft/lang/zh_cn.json": {
                          "hash": "aa00000000000000000000000000000000000000",
                          "size": 4
                        }
                      }
                    }
                    """),
                "https://resources.download.minecraft.net/aa/aa00000000000000000000000000000000000000" => CreateBinaryResponse(request, "asset"),
                "https://example.test/logging/client.xml" => CreateBinaryResponse(request, "<xml />"),
                "https://maven.minecraftforge.net/net/minecraftforge/forge/26.1.2-64.0.9/forge-26.1.2-64.0.9-client.jar" => CreateBinaryResponse(request, "forge client"),
                "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json" when includeVanilla1201 => CreateJsonResponse(request, """
                    {
                      "versions": [
                        {
                          "id": "1.20.1",
                          "url": "https://example.test/mojang/1.20.1.json"
                        }
                      ]
                    }
                    """),
                "https://example.test/mojang/1.20.1.json" when includeVanilla1201 => CreateJsonResponse(request, """
                    {
                      "id": "1.20.1",
                      "type": "release",
                      "downloads": {
                        "client": {
                          "url": "https://example.test/client.jar"
                        }
                      },
                      "libraries": [],
                      "assetIndex": {
                        "id": "1.20.1",
                        "url": "https://example.test/assets/index.json"
                      }
                    }
                    """),
                "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json" => CreateJsonResponse(request, """
                    {
                      "versions": []
                    }
                    """),
                _ => throw new InvalidOperationException($"Unexpected request: {uri}")
            });
        }

        private static HttpResponseMessage CreateJsonResponse(HttpRequestMessage request, string json)
        {
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(json)
            };
        }

        private static HttpResponseMessage CreateBinaryResponse(HttpRequestMessage request, string content)
        {
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(content))
            };
        }
    }

    private sealed class NotFoundDownloadHandler : HttpMessageHandler
    {
        private readonly string expectedUrl;

        public NotFoundDownloadHandler(string expectedUrl)
        {
            this.expectedUrl = expectedUrl;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = request.RequestUri!.AbsoluteUri == expectedUrl
                ? new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
                : new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("ok"))
                };
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private sealed class ConcurrentAssetRepairHttpHandler : HttpMessageHandler
    {
        public static readonly string[] AssetHashes =
        [
            "aa00000000000000000000000000000000000000",
            "bb00000000000000000000000000000000000000",
            "cc00000000000000000000000000000000000000",
            "dd00000000000000000000000000000000000000"
        ];

        private int activeAssetRequests;
        private int maxConcurrentAssetRequests;

        public int MaxConcurrentAssetRequests => Volatile.Read(ref maxConcurrentAssetRequests);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!.AbsoluteUri;
            if (uri == "https://example.test/assets/concurrent-index.json")
            {
                return CreateJsonResponse(request, $$"""
                    {
                      "objects": {
                        "asset/a.txt": { "hash": "{{AssetHashes[0]}}", "size": 4 },
                        "asset/b.txt": { "hash": "{{AssetHashes[1]}}", "size": 4 },
                        "asset/c.txt": { "hash": "{{AssetHashes[2]}}", "size": 4 },
                        "asset/d.txt": { "hash": "{{AssetHashes[3]}}", "size": 4 }
                      }
                    }
                    """);
            }

            if (AssetHashes.Any(hash => uri == $"https://resources.download.minecraft.net/{hash[..2]}/{hash}"))
                return await CreateDelayedAssetResponseAsync(request, cancellationToken);

            throw new InvalidOperationException($"Unexpected request: {uri}");
        }

        private async Task<HttpResponseMessage> CreateDelayedAssetResponseAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var activeRequests = Interlocked.Increment(ref activeAssetRequests);
            TrackMaxConcurrentRequests(activeRequests);
            try
            {
                await Task.Delay(100, cancellationToken);
                return CreateBinaryResponse(request, "asset");
            }
            finally
            {
                Interlocked.Decrement(ref activeAssetRequests);
            }
        }

        private void TrackMaxConcurrentRequests(int activeRequests)
        {
            while (true)
            {
                var observedMax = Volatile.Read(ref maxConcurrentAssetRequests);
                if (activeRequests <= observedMax)
                    return;

                if (Interlocked.CompareExchange(ref maxConcurrentAssetRequests, activeRequests, observedMax) == observedMax)
                    return;
            }
        }

        private static HttpResponseMessage CreateJsonResponse(HttpRequestMessage request, string json)
        {
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(json)
            };
        }

        private static HttpResponseMessage CreateBinaryResponse(HttpRequestMessage request, string content)
        {
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(content))
            };
        }
    }

    private sealed class RecordingProgress : IProgress<LauncherProgress>
    {
        public List<LauncherProgress> Items { get; } = [];

        public void Report(LauncherProgress value)
        {
            Items.Add(value);
        }
    }

    private sealed class NoOpLaunchCrashMonitor : ILaunchCrashMonitor
    {
        public ILaunchCrashMonitorSession CreateSession(string minecraftDirectory, string instanceDirectory, string versionName)
        {
            return new NoOpSession();
        }

        private sealed class NoOpSession : ILaunchCrashMonitorSession
        {
            public void Configure(Process process)
            {
            }

            public Task<LaunchCrashMonitorResult?> WaitForQuickExitAsync(
                Process process,
                LaunchDiagnosticContext context,
                CancellationToken cancellationToken)
            {
                return Task.FromResult<LaunchCrashMonitorResult?>(null);
            }

            public GameLaunchSession CreateGameLaunchSession(Process process, LaunchDiagnosticContext context)
            {
                return new GameLaunchSession(
                    context.InstanceId,
                    context.InstanceName,
                    Task.FromResult(LaunchExitResult.Success));
            }
        }
    }

    private static void AssertProgressPercentIsMonotonic(IEnumerable<LauncherProgress> items)
    {
        var percents = items
            .Where(item => item.Percent is not null)
            .Select(item => item.Percent!.Value)
            .ToList();
        Assert.True(percents.SequenceEqual(percents.Order()));
    }

    private static int CountArgument(string arguments, string argument)
    {
        return arguments
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Count(token => string.Equals(token, argument, StringComparison.OrdinalIgnoreCase));
    }

    private static string FlattenArguments(IEnumerable<MArgument>? arguments)
    {
        return string.Join(" ", arguments?.SelectMany(argument => argument.Values) ?? []);
    }
}
