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
            Environment.ProcessPath,
            progress,
            allowRepair: true,
            CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(versionDirectory, "Forge Pack.jar")));
        Assert.Equal(["Forge Pack"], Directory.GetDirectories(Path.Combine(minecraftDirectory, "versions")).Select(Path.GetFileName));
        Assert.Contains(progress.Items, item => item.Stage == LaunchProgressStages.CheckingInstance);
        Assert.Contains(progress.Items, item => item.Stage == LaunchProgressStages.RepairingJar);
        Assert.True(progress.Items.Select(item => item.Percent ?? 0).SequenceEqual(progress.Items.Select(item => item.Percent ?? 0).Order()));
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
            Environment.ProcessPath,
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
        Assert.True(progress.Items.Select(item => item.Percent ?? 0).SequenceEqual(progress.Items.Select(item => item.Percent ?? 0).Order()));
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
            Environment.ProcessPath,
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
            Environment.ProcessPath,
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
            Environment.ProcessPath,
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
            Environment.ProcessPath,
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
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft"),
            DefaultJavaPath = @"C:\Java\bin\java.exe"
        };
        var instance = new GameInstance
        {
            Name = "Forge Pack",
            VersionName = "Forge Pack",
            InstanceDirectory = Path.Combine(settings.MinecraftDirectory, "versions", "Forge Pack"),
            JavaPath = @"D:\Java\bin\java.exe"
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
        Assert.Equal(instance.JavaPath, repairService.LastJavaPath);
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
            Environment.ProcessPath,
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
            Environment.ProcessPath,
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
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft"),
            DefaultJavaPath = @"C:\Java\bin\java.exe"
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
        Assert.True(progress.Items.Select(item => item.Percent ?? 0).SequenceEqual(progress.Items.Select(item => item.Percent ?? 0).Order()));
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
            MinecraftDirectory = minecraftDirectory,
            DefaultJavaPath = @"C:\Java\bin\java.exe"
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
        Assert.Contains("FailureKind: quick_exit", diagnostic);
        Assert.Contains("FailureSummary: Missing launch target", diagnostic);
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
            MinecraftDirectory = minecraftDirectory,
            DefaultJavaPath = @"C:\Java\bin\java.exe"
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

        await Assert.ThrowsAsync<InstanceRepairException>(() => service.LaunchAsync(
            instance,
            account,
            settings,
            progress: null));

        var logsDirectory = Path.Combine(instanceDirectory, "logs", "launcher");
        var diagnosticPath = Directory.GetFiles(logsDirectory, "launch-diagnostics-*.log").Single();
        var diagnostic = await File.ReadAllTextAsync(diagnosticPath);
        Assert.Contains("FailureKind: instance_repair_failed", diagnostic);
        Assert.Contains("FailureSummary: No usable Java runtime was found.", diagnostic);
        Assert.Contains("[ExceptionChain]", diagnostic);
        Assert.Contains("InstanceRepairException", diagnostic);
    }

    private sealed class FakeLaunchAccountSessionService : ILaunchAccountSessionService
    {
        public Task<LaunchAccountSession> CreateSessionAsync(LauncherAccount account, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new LaunchAccountSession(account.DisplayName, "token", account.Uuid ?? string.Empty, account.IsOffline));
        }
    }

    private sealed class FakeManagedVersionRepairService : IManagedVersionRepairService
    {
        public string? LastVersionName { get; private set; }
        public string? LastInstanceDirectory { get; private set; }
        public string? LastJavaPath { get; private set; }
        public bool LastAllowRepair { get; private set; }
        public Func<IProgress<LauncherProgress>?, Task>? OnRepairAsync { get; init; }

        public Task RepairAsync(
            string minecraftDirectory,
            string versionName,
            string instanceDirectory,
            string? javaPath,
            IProgress<LauncherProgress>? progress,
            bool allowRepair,
            CancellationToken cancellationToken)
        {
            LastVersionName = versionName;
            LastInstanceDirectory = instanceDirectory;
            LastJavaPath = javaPath;
            LastAllowRepair = allowRepair;
            if (OnRepairAsync is not null)
                return OnRepairAsync(progress);

            return Task.CompletedTask;
        }
    }

    private sealed class FakeLaunchGameLauncherFactory : ILaunchGameLauncherFactory
    {
        public FakeLaunchGameLauncher Launcher { get; } = new();
        public Func<string, MLaunchOption, Process>? BuildProcess { get; init; }

        public ILaunchGameLauncher Create(string minecraftDirectory, IProgress<LauncherProgress>? progress)
        {
            if (BuildProcess is not null)
                Launcher.BuildProcess = BuildProcess;

            return Launcher;
        }
    }

    private sealed class FakeLaunchGameLauncher : ILaunchGameLauncher
    {
        public string? LastBuiltVersionName { get; private set; }
        public Func<string, MLaunchOption, Process>? BuildProcess { get; set; }

        public ValueTask<Process> BuildProcessAsync(string versionName, MLaunchOption launchOption, CancellationToken cancellationToken)
        {
            LastBuiltVersionName = versionName;
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

            public Task<LaunchCrashMonitorResult?> WaitForQuickExitAsync(Process process, CancellationToken cancellationToken)
            {
                return Task.FromResult<LaunchCrashMonitorResult?>(null);
            }
        }
    }
}
