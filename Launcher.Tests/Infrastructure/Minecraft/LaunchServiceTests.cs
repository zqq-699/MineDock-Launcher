/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Diagnostics;
using CmlLib.Core.ProcessBuilder;
using Launcher.Application;
using Launcher.Application.Accounts;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class LaunchServiceTests : TestTempDirectory
{
    [Fact]
    public async Task LaunchRepairsBeforeBuildingProcess()
    {
        var repair = new FakeRepairService();
        var launcher = new FakeLauncherFactory();
        var service = CreateService(repair, launcher);
        var settings = CreateSettings();
        var instance = CreateInstance(settings.MinecraftDirectory, "Forge Pack");

        await service.LaunchAsync(instance, CreateAccount(), settings, null);

        Assert.Equal("Forge Pack", repair.LastVersionName);
        Assert.Equal(instance.InstanceDirectory, repair.LastInstanceDirectory);
        Assert.True(repair.LastAllowRepair);
        Assert.Equal("Forge Pack", launcher.Launcher.LastVersionName);
    }

    [Fact]
    public async Task DisabledFileCheckSkipsFileStagesAndReportsHundredOnlyAfterWindowAppears()
    {
        var integrity = new RecordingIntegrityService();
        var launcher = new FakeLauncherFactory();
        var service = CreateService(launcher: launcher, integrity: integrity);
        var settings = CreateSettings();
        settings.DefaultCheckFilesBeforeLaunch = false;
        var reports = new List<LauncherProgress>();

        await service.LaunchAsync(
            CreateInstance(settings.MinecraftDirectory, "No File Check"),
            CreateAccount(),
            settings,
            new InlineProgress(reports));

        Assert.Equal(0, integrity.ValidateCallCount);
        Assert.Equal(1, integrity.FinalValidationCallCount);
        Assert.DoesNotContain(
            reports,
            report => report.Stage is LaunchProgressStages.CheckingFiles
                or LaunchProgressStages.RevalidatingFiles);
        Assert.Equal(
            [99d, 100d],
            reports
                .Where(report => report.Stage == LaunchProgressStages.StartingProcess)
                .Select(report => report.Percent!.Value));
    }

    [Fact]
    public async Task EnabledFileCheckRunsFullAndFinalValidationOnce()
    {
        var integrity = new RecordingIntegrityService();
        var launcher = new FakeLauncherFactory();
        var service = CreateService(launcher: launcher, integrity: integrity);
        var settings = CreateSettings();

        await service.LaunchAsync(
            CreateInstance(settings.MinecraftDirectory, "File Check"),
            CreateAccount(),
            settings,
            progress: null);

        Assert.Equal(1, integrity.ValidateCallCount);
        Assert.Equal(1, integrity.FinalValidationCallCount);
    }

    [Fact]
    public async Task LaunchRemainsAtNinetyNineUntilVisibleWindowIsReported()
    {
        var waiter = new ControlledWindowReadinessWaiter();
        Process? launchedProcess = null;
        var launcher = new FakeLauncherFactory
        {
            BuildProcess = (_, _) => launchedProcess = CreateCommandProcess("/c ping 127.0.0.1 -n 30 >nul")
        };
        var service = CreateService(launcher: launcher, windowReadinessWaiter: waiter);
        var settings = CreateSettings();
        settings.DefaultCheckFilesBeforeLaunch = false;
        var reports = new List<LauncherProgress>();

        try
        {
            var launchTask = service.LaunchAsync(
                CreateInstance(settings.MinecraftDirectory, "Window Wait"),
                CreateAccount(),
                settings,
                new InlineProgress(reports));

            await waiter.WaitUntilCalledAsync();
            Assert.False(launchTask.IsCompleted);
            Assert.Equal(
                [99d],
                reports
                    .Where(report => report.Stage == LaunchProgressStages.StartingProcess)
                    .Select(report => report.Percent!.Value));

            waiter.Complete(GameWindowReadinessResult.WindowVisible);
            await launchTask;

            Assert.Equal(
                [99d, 100d],
                reports
                    .Where(report => report.Stage == LaunchProgressStages.StartingProcess)
                    .Select(report => report.Percent!.Value));
        }
        finally
        {
            TryKillProcess(launchedProcess);
        }
    }

    [Fact]
    public async Task CancelingWindowWaitTerminatesProcessTreeWithoutReportingCompletion()
    {
        var waiter = new ControlledWindowReadinessWaiter();
        var terminator = new RecordingProcessTerminator();
        var childPidPath = Path.Combine(TempRoot, "launch-child.pid");
        var childScriptPath = Path.Combine(TempRoot, "launch-child.ps1");
        Directory.CreateDirectory(TempRoot);
        await File.WriteAllTextAsync(
            childScriptPath,
            "param([string]$PidPath)\n"
            + "$child = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\\ping.exe') "
            + "-ArgumentList @('127.0.0.1','-n','30') -WindowStyle Hidden -PassThru\n"
            + "[IO.File]::WriteAllText($PidPath, [string]$child.Id)\n"
            + "Wait-Process -Id $child.Id\n");
        Process? launchedProcess = null;
        int? childProcessId = null;
        var launcher = new FakeLauncherFactory
        {
            BuildProcess = (_, _) => launchedProcess = CreateProcessWithLongRunningChild(
                childScriptPath,
                childPidPath)
        };
        var service = CreateService(
            launcher: launcher,
            crashMonitor: new LaunchCrashMonitor(),
            windowReadinessWaiter: waiter,
            processTerminator: terminator);
        var settings = CreateSettings();
        settings.DefaultCheckFilesBeforeLaunch = false;
        var reports = new List<LauncherProgress>();
        using var cancellation = new CancellationTokenSource();
        var instance = CreateInstance(settings.MinecraftDirectory, "Canceled Window Wait");

        try
        {
            var launchTask = service.LaunchAsync(
                instance,
                CreateAccount(),
                settings,
                new InlineProgress(reports),
                cancellationToken: cancellation.Token);

            await waiter.WaitUntilCalledAsync();
            childProcessId = await WaitForChildProcessIdAsync(childPidPath);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => launchTask);
            Assert.DoesNotContain(reports, report => report.Percent == 100);
            Assert.True(terminator.ObservedExitedProcess);
            Assert.True(await WaitForProcessExitAsync(childProcessId.Value));
            Assert.Empty(Directory.Exists(Path.Combine(
                    instance.InstanceDirectory,
                    LauncherApplicationIdentity.StorageDirectoryName,
                    "logs"))
                ? Directory.EnumerateFiles(
                    Path.Combine(instance.InstanceDirectory, LauncherApplicationIdentity.StorageDirectoryName, "logs"),
                    "launch-output-*.log")
                : []);
        }
        finally
        {
            TryKillProcess(launchedProcess);
            TryKillProcess(childProcessId);
        }
    }

    [Fact]
    public async Task TerminationFailureIsReportedAsLaunchFailureAndKeepsCrashMonitoring()
    {
        var waiter = new ControlledWindowReadinessWaiter();
        var crashMonitor = new RecordingCrashMonitor();
        Process? launchedProcess = null;
        var launcher = new FakeLauncherFactory
        {
            BuildProcess = (_, _) => launchedProcess = CreateCommandProcess("/c ping 127.0.0.1 -n 30 >nul")
        };
        var service = CreateService(
            launcher: launcher,
            crashMonitor: crashMonitor,
            windowReadinessWaiter: waiter,
            processTerminator: new FailingProcessTerminator());
        var settings = CreateSettings();
        settings.DefaultCheckFilesBeforeLaunch = false;
        using var cancellation = new CancellationTokenSource();

        try
        {
            var launchTask = service.LaunchAsync(
                CreateInstance(settings.MinecraftDirectory, "Failed Cancellation Cleanup"),
                CreateAccount(),
                settings,
                progress: null,
                cancellationToken: cancellation.Token);

            await waiter.WaitUntilCalledAsync();
            cancellation.Cancel();
            var exception = await Assert.ThrowsAsync<LaunchFailedException>(() => launchTask);

            Assert.Contains("terminate", exception.Report.FailureSummary, StringComparison.OrdinalIgnoreCase);
            Assert.True(crashMonitor.MonitorSession.GameSessionCreated);
        }
        finally
        {
            TryKillProcess(launchedProcess);
        }
    }

    [Fact]
    public async Task AutomaticJavaProvisioningRetriesSelection()
    {
        var javaDirectory = Path.Combine(TempRoot, "java", "bin");
        Directory.CreateDirectory(javaDirectory);
        var javaPath = Path.Combine(javaDirectory, "java.exe");
        var javawPath = Path.Combine(javaDirectory, "javaw.exe");
        await File.WriteAllTextAsync(javaPath, string.Empty);
        await File.WriteAllTextAsync(javawPath, string.Empty);
        var runtime = new JavaRuntimeInfo("Java 21", "21", 21, "x64", javaPath, Path.GetDirectoryName(javaDirectory)!, "MinecraftRuntime");
        var selection = new FakeJavaSelection(
            new JavaRuntimeSelectionException("missing", JavaRuntimeSelectionFailureReason.AutomaticRuntimeMissing, 21),
            runtime);
        var provisioning = new FakeJavaProvisioning();
        var launcher = new FakeLauncherFactory();
        var service = CreateService(javaSelection: selection, javaProvisioning: provisioning, launcher: launcher);
        var settings = CreateSettings();
        settings.JavaSelectionMode = JavaSelectionMode.Auto;

        await service.LaunchAsync(CreateInstance(settings.MinecraftDirectory, "1.21.4"), CreateAccount(), settings, null);

        Assert.Equal(2, selection.CallCount);
        Assert.Equal(1, provisioning.CallCount);
        Assert.Equal(javawPath, launcher.Launcher.LastOption?.JavaPath);
    }

    [Fact]
    public async Task ManualJavaFailureDoesNotProvisionRuntime()
    {
        var selection = new FakeJavaSelection(
            new JavaRuntimeSelectionException("manual missing", JavaRuntimeSelectionFailureReason.ManualRuntimeUnavailable));
        var provisioning = new FakeJavaProvisioning();
        var launcher = new FakeLauncherFactory();
        var service = CreateService(javaSelection: selection, javaProvisioning: provisioning, launcher: launcher);
        var settings = CreateSettings();
        settings.JavaSelectionMode = JavaSelectionMode.Manual;
        settings.SelectedJavaExecutablePath = @"C:\Missing\java.exe";

        var exception = await Assert.ThrowsAsync<LaunchFailedException>(() => service.LaunchAsync(
            CreateInstance(settings.MinecraftDirectory, "1.21.4"), CreateAccount(), settings, null));

        Assert.IsType<JavaRuntimeSelectionException>(exception.InnerException);
        Assert.Equal(0, provisioning.CallCount);
        Assert.Null(launcher.Launcher.LastVersionName);
    }

    [Fact]
    public async Task QuickExitWritesRedactedDiagnostic()
    {
        var settings = CreateSettings();
        var instance = CreateInstance(settings.MinecraftDirectory, "Broken Pack");
        Directory.CreateDirectory(Path.Combine(instance.InstanceDirectory, "logs"));
        await File.WriteAllTextAsync(Path.Combine(instance.InstanceDirectory, "logs", "latest.log"), "[ERROR]: Missing launch target");
        var launcher = new FakeLauncherFactory
        {
            BuildProcess = (_, _) => CreateCommandProcess(
                "/c echo ERROR Missing launch target --accessToken super-secret-access-token 1>&2 & exit 1")
        };
        var service = CreateService(
            launcher: launcher,
            crashMonitor: new LaunchCrashMonitor(),
            windowReadinessWaiter: new GameWindowReadinessWaiter());
        var reports = new List<LauncherProgress>();

        var exception = await Assert.ThrowsAsync<LaunchProcessExitedException>(() =>
            service.LaunchAsync(instance, CreateAccount(), settings, new InlineProgress(reports)));

        Assert.Equal(LaunchFailureKind.StartupAbnormalExit, exception.Report.Kind);
        Assert.DoesNotContain(reports, report => report.Percent == 100);
        Assert.Contains("super-secret-access-token", exception.Report.ExportSensitiveValues);
        Assert.True(File.Exists(exception.DiagnosticPath));
        var expectedDiagnosticDirectory = Path.Combine(
            instance.InstanceDirectory,
            LauncherApplicationIdentity.StorageDirectoryName,
            "logs");
        Assert.Equal(expectedDiagnosticDirectory, exception.Report.DiagnosticDirectory);
        Assert.Equal(expectedDiagnosticDirectory, Path.GetDirectoryName(exception.DiagnosticPath));
        Assert.Equal(LaunchDiagnosticType.CapturedOutput, exception.Report.PrimaryDiagnostic?.Type);
        Assert.Equal(expectedDiagnosticDirectory, Path.GetDirectoryName(exception.Report.PrimaryDiagnostic?.Path));
        Assert.Equal(LaunchDiagnosticType.LauncherDiagnostic, exception.Report.DiagnosticCandidates[^1].Type);
        var diagnostic = await File.ReadAllTextAsync(exception.DiagnosticPath!);
        Assert.Contains("Missing launch target", diagnostic);
        Assert.Contains("[PrimaryDiagnostic]", diagnostic);
        Assert.Contains("Type: CapturedOutput", diagnostic);
        Assert.Contains("[RelatedDiagnostics]", diagnostic);
        Assert.Contains("LauncherDiagnostic:", diagnostic);
        Assert.Contains("Evidence.1: Reason:", diagnostic);
        Assert.DoesNotContain("super-secret-access-token", diagnostic);
        var capturedOutput = await File.ReadAllTextAsync(exception.Report.PrimaryDiagnostic!.Path);
        Assert.Contains("[stderr]", capturedOutput);
        Assert.Contains("<redacted>", capturedOutput);
        Assert.DoesNotContain("super-secret-access-token", capturedOutput);
    }

    [Fact]
    public async Task RepairFailureWritesDiagnosticBeforeProcessStarts()
    {
        var settings = CreateSettings();
        var instance = CreateInstance(settings.MinecraftDirectory, "Broken Pack");
        Directory.CreateDirectory(instance.InstanceDirectory);
        var repair = new FakeRepairService
        {
            OnRepair = () => throw new InstanceRepairException("No usable Java runtime was found.")
        };
        var launcher = new FakeLauncherFactory();
        var service = CreateService(repair, launcher);

        var exception = await Assert.ThrowsAsync<LaunchFailedException>(() =>
            service.LaunchAsync(instance, CreateAccount(), settings, null));

        Assert.IsType<InstanceRepairException>(exception.InnerException);
        Assert.Null(launcher.Launcher.LastVersionName);
        Assert.True(File.Exists(exception.Report.DiagnosticPath));
        var expectedDiagnosticDirectory = Path.Combine(
            instance.InstanceDirectory,
            LauncherApplicationIdentity.StorageDirectoryName,
            "logs");
        Assert.Equal(expectedDiagnosticDirectory, exception.Report.DiagnosticDirectory);
        Assert.Equal(expectedDiagnosticDirectory, Path.GetDirectoryName(exception.Report.DiagnosticPath));
        Assert.Contains("instance_repair_failed", await File.ReadAllTextAsync(exception.Report.DiagnosticPath!));
    }

    [Fact]
    public async Task IntegrityCancellationDoesNotWriteRepairFailureDiagnostic()
    {
        using var cancellation = new CancellationTokenSource();
        var integrity = new RecordingIntegrityService
        {
            OnValidate = token =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<GameFileRepairResult>(token);
            }
        };
        var launcher = new FakeLauncherFactory();
        var service = CreateService(integrity: integrity, launcher: launcher);
        var settings = CreateSettings();
        var instance = CreateInstance(settings.MinecraftDirectory, "Canceled Integrity");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.LaunchAsync(
            instance,
            CreateAccount(),
            settings,
            progress: null,
            cancellationToken: cancellation.Token));

        Assert.Null(launcher.Launcher.LastVersionName);
        Assert.False(Directory.Exists(Path.Combine(
            instance.InstanceDirectory,
            LauncherApplicationIdentity.StorageDirectoryName,
            "logs")));
    }

    [Fact]
    public async Task ThirdPartyLaunchPrependsInjectorArgumentsAndUsesMojangIdentity()
    {
        var injectorPath = Path.Combine(TempRoot, "path with spaces", "authlib-injector.jar");
        var accountSession = new FakeAccountSession(new LaunchAccountSession(
            "ThirdPartyPlayer",
            "third-party-secret-token",
            "00112233445566778899aabbccddeeff",
            IsOffline: false,
            Kind: LauncherAccountKind.ThirdParty,
            ThirdParty: new ThirdPartyLaunchContext(
                "https://example.test/api/yggdrasil/",
                "eyJtZXRhIjp7fX0=")));
        var launcher = new FakeLauncherFactory();
        var integrity = new RecordingIntegrityService();
        var service = CreateService(
            launcher: launcher,
            integrity: integrity,
            accountSession: accountSession,
            authlibInjector: new FakeAuthlibInjector(injectorPath));
        var settings = CreateSettings();
        settings.DefaultJvmArguments = "-Duser.option=true";
        var account = new LauncherAccount
        {
            Id = "third-party",
            DisplayName = "ThirdPartyPlayer",
            Kind = LauncherAccountKind.ThirdParty,
            Uuid = "00112233-4455-6677-8899-aabbccddeeff"
        };

        await service.LaunchAsync(CreateInstance(settings.MinecraftDirectory, "Third Party Pack"), account, settings, null);

        var option = Assert.IsType<MLaunchOption>(launcher.Launcher.LastOption);
        var arguments = option.ExtraJvmArguments.SelectMany(argument => argument.Values).ToArray();
        Assert.Equal($"-javaagent:{injectorPath}=https://example.test/api/yggdrasil/", arguments[0]);
        Assert.Equal("-Dauthlibinjector.yggdrasil.prefetched=eyJtZXRhIjp7fX0=", arguments[1]);
        Assert.Equal("-Duser.option=true", arguments[2]);
        Assert.Equal("mojang", option.ArgumentDictionary["user_type"]);
        Assert.Equal("{}", option.UserProperties);
        Assert.Equal("ThirdPartyPlayer", option.Session?.Username);
        Assert.Equal("00112233445566778899aabbccddeeff", option.Session?.UUID);
        Assert.Contains(
            Path.GetFullPath(injectorPath),
            Assert.IsType<GameFileIntegrityRequest>(integrity.FinalRequest).AllowedAdditionalCommandFilePaths,
            StringComparer.OrdinalIgnoreCase);
    }

    private static LaunchService CreateService(
        FakeRepairService? repair = null,
        FakeLauncherFactory? launcher = null,
        IGameFileIntegrityService? integrity = null,
        ILaunchCrashMonitor? crashMonitor = null,
        IJavaRuntimeSelectionService? javaSelection = null,
        IJavaRuntimeProvisioningService? javaProvisioning = null,
        ILaunchAccountSessionService? accountSession = null,
        IAuthlibInjectorProvisioningService? authlibInjector = null,
        IGameWindowReadinessWaiter? windowReadinessWaiter = null,
        ILaunchProcessTerminator? processTerminator = null)
    {
        var resolvedAccountSession = accountSession ?? new FakeAccountSession();
        var resolvedLauncher = launcher ?? new FakeLauncherFactory();
        var resolvedCrashMonitor = crashMonitor ?? new NoOpCrashMonitor();
        var resolvedWindowWaiter = windowReadinessWaiter ?? new ImmediateWindowReadinessWaiter();
        return integrity is not null
            ? new LaunchService(
                resolvedAccountSession,
                integrity,
                resolvedLauncher,
                resolvedCrashMonitor,
                javaRuntimeSelectionService: javaSelection,
                javaRuntimeProvisioningService: javaProvisioning,
                authlibInjectorProvisioningService: authlibInjector,
                gameWindowReadinessWaiter: resolvedWindowWaiter,
                launchProcessTerminator: processTerminator)
            : new LaunchService(
                resolvedAccountSession,
                repair ?? new FakeRepairService(),
                resolvedLauncher,
                resolvedCrashMonitor,
                javaRuntimeSelectionService: javaSelection,
                javaRuntimeProvisioningService: javaProvisioning,
                authlibInjectorProvisioningService: authlibInjector,
                gameWindowReadinessWaiter: resolvedWindowWaiter,
                launchProcessTerminator: processTerminator);
    }

    private LauncherSettings CreateSettings() => new()
    {
        MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
    };

    private static GameInstance CreateInstance(string minecraftDirectory, string name) => new()
    {
        Id = name,
        Name = name,
        MinecraftVersion = "1.20.1",
        VersionName = name,
        InstanceDirectory = Path.Combine(minecraftDirectory, "versions", name),
        Loader = LoaderKind.Vanilla,
        MemoryMb = 4096
    };

    private static LauncherAccount CreateAccount() => new()
    {
        Id = "offline",
        DisplayName = "Player",
        Uuid = "00000000-0000-0000-0000-000000000001",
        Kind = LauncherAccountKind.Offline
    };

    private static Process CreateCommandProcess(string arguments) => new()
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false
        }
    };

    private static Process CreateProcessWithLongRunningChild(string scriptPath, string childPidPath)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(
                    Environment.SystemDirectory,
                    "WindowsPowerShell",
                    "v1.0",
                    "powershell.exe"),
                CreateNoWindow = true,
                UseShellExecute = false
            }
        };
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-NonInteractive");
        process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
        process.StartInfo.ArgumentList.Add("Bypass");
        process.StartInfo.ArgumentList.Add("-File");
        process.StartInfo.ArgumentList.Add(scriptPath);
        process.StartInfo.ArgumentList.Add("-PidPath");
        process.StartInfo.ArgumentList.Add(childPidPath);
        return process;
    }

    private static async Task<int> WaitForChildProcessIdAsync(string path)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (true)
            {
                if (File.Exists(path)
                    && int.TryParse(await File.ReadAllTextAsync(path, timeout.Token), out var processId))
                {
                    return processId;
                }

                await Task.Delay(50, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException("The launch test child process did not publish its process id.");
        }
    }

    private static async Task<bool> WaitForProcessExitAsync(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
                return true;
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static void TryKillProcess(Process? process)
    {
        if (process is null)
            return;

        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void TryKillProcess(int? processId)
    {
        if (processId is null)
            return;
        try
        {
            using var process = Process.GetProcessById(processId.Value);
            TryKillProcess(process);
        }
        catch (ArgumentException)
        {
        }
    }

    private sealed class FakeAccountSession(LaunchAccountSession? session = null) : ILaunchAccountSessionService
    {
        public Task<LaunchAccountSession> CreateSessionAsync(LauncherAccount account, CancellationToken cancellationToken = default) =>
            Task.FromResult(session ?? new LaunchAccountSession(
                account.DisplayName,
                "super-secret-access-token",
                account.Uuid!,
                account.IsOffline));
    }

    private sealed class InlineProgress(List<LauncherProgress> reports) : IProgress<LauncherProgress>
    {
        public void Report(LauncherProgress value) => reports.Add(value);
    }

    private sealed class FakeAuthlibInjector(string filePath) : IAuthlibInjectorProvisioningService
    {
        public Task<AuthlibInjectorArtifact> EnsureAvailableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AuthlibInjectorArtifact(filePath, "1.2.7", 55));
    }

    private sealed class RecordingIntegrityService : IGameFileIntegrityService
    {
        public Func<CancellationToken, Task<GameFileRepairResult>>? OnValidate { get; init; }
        public GameFileIntegrityRequest? FinalRequest { get; private set; }
        public int ValidateCallCount { get; private set; }
        public int FinalValidationCallCount { get; private set; }

        public Task<GameFileRepairResult> ValidateAndRepairAsync(
            GameFileIntegrityRequest request,
            GameFileRepairOptions options,
            IProgress<LauncherProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ValidateCallCount++;
            return OnValidate?.Invoke(cancellationToken) ?? Task.FromResult(GameFileRepairResult.Empty);
        }

        public Task<GameFileRepairResult> ValidateFinalLaunchCommandAsync(
            GameFileIntegrityRequest request,
            ProcessStartInfo startInfo,
            CancellationToken cancellationToken = default)
        {
            FinalValidationCallCount++;
            FinalRequest = request;
            return Task.FromResult(GameFileRepairResult.Empty);
        }
    }

    private sealed class FakeRepairService : IManagedVersionRepairService
    {
        public string? LastVersionName { get; private set; }
        public string? LastInstanceDirectory { get; private set; }
        public bool LastAllowRepair { get; private set; }
        public Func<Task>? OnRepair { get; init; }

        public Task RepairAsync(string minecraftDirectory, string versionName, string instanceDirectory,
            IProgress<LauncherProgress>? progress, bool allowRepair, CancellationToken cancellationToken = default,
            DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            LastVersionName = versionName;
            LastInstanceDirectory = instanceDirectory;
            LastAllowRepair = allowRepair;
            return OnRepair?.Invoke() ?? Task.CompletedTask;
        }
    }

    private sealed class FakeJavaSelection(params object[] results) : IJavaRuntimeSelectionService
    {
        private readonly Queue<object> results = new(results);
        public int CallCount { get; private set; }

        public Task<JavaRuntimeInfo> SelectForLaunchAsync(GameInstance instance, LauncherSettings settings,
            LaunchRequestOptions? options = null, CancellationToken cancellationToken = default)
        {
            CallCount++;
            var result = results.Dequeue();
            if (result is Exception exception) throw exception;
            return Task.FromResult((JavaRuntimeInfo)result);
        }
    }

    private sealed class FakeJavaProvisioning : IJavaRuntimeProvisioningService
    {
        public int CallCount { get; private set; }
        public Task EnsureForLaunchAsync(GameInstance instance, LauncherSettings settings,
            IProgress<LauncherProgress>? progress, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeLauncherFactory : ILaunchGameLauncherFactory
    {
        public FakeLauncher Launcher { get; } = new();
        public Func<string, MLaunchOption, Process>? BuildProcess { get; init; }
        public ILaunchGameLauncher Create(string minecraftDirectory, IProgress<LauncherProgress>? progress,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            Launcher.BuildProcess = BuildProcess;
            return Launcher;
        }
    }

    private sealed class FakeLauncher : ILaunchGameLauncher
    {
        public string? LastVersionName { get; private set; }
        public MLaunchOption? LastOption { get; private set; }
        public Func<string, MLaunchOption, Process>? BuildProcess { get; set; }

        public ValueTask<Process> BuildProcessAsync(string versionName, MLaunchOption option, CancellationToken cancellationToken)
        {
            LastVersionName = versionName;
            LastOption = option;
            return ValueTask.FromResult(BuildProcess?.Invoke(versionName, option) ?? CreateCommandProcess("/c exit 0"));
        }
    }

    private sealed class NoOpCrashMonitor : ILaunchCrashMonitor
    {
        public ILaunchCrashMonitorSession CreateSession(string minecraftDirectory, string instanceDirectory, string versionName) => new Session();

        private sealed class Session : ILaunchCrashMonitorSession
        {
            public void Configure(Process process) { }
            public void BeginMonitoring(Process process, LaunchDiagnosticContext context) { }
            public Task<LaunchCrashMonitorResult> CreateStartupExitResultAsync(
                Process process,
                LaunchDiagnosticContext context,
                CancellationToken cancellationToken) => throw new InvalidOperationException("The fake process did not exit during startup.");
            public Task CompleteCanceledStartupAsync(Process process) => Task.CompletedTask;
            public GameLaunchSession CreateGameLaunchSession(Process process, LaunchDiagnosticContext context) =>
                new(context.InstanceId, context.InstanceName, Task.FromResult(LaunchExitResult.Success));
        }
    }

    private sealed class RecordingProcessTerminator : ILaunchProcessTerminator
    {
        private readonly LaunchProcessTerminator inner = new();

        public bool ObservedExitedProcess { get; private set; }

        public async Task TerminateAsync(Process process)
        {
            await inner.TerminateAsync(process);
            ObservedExitedProcess = process.HasExited;
        }
    }

    private sealed class FailingProcessTerminator : ILaunchProcessTerminator
    {
        public Task TerminateAsync(Process process) =>
            Task.FromException(new IOException("Injected process-tree termination failure."));
    }

    private sealed class RecordingCrashMonitor : ILaunchCrashMonitor
    {
        public Session MonitorSession { get; } = new();

        public ILaunchCrashMonitorSession CreateSession(
            string minecraftDirectory,
            string instanceDirectory,
            string versionName) => MonitorSession;

        internal sealed class Session : ILaunchCrashMonitorSession
        {
            public bool GameSessionCreated { get; private set; }

            public void Configure(Process process) { }
            public void BeginMonitoring(Process process, LaunchDiagnosticContext context) { }
            public Task<LaunchCrashMonitorResult> CreateStartupExitResultAsync(
                Process process,
                LaunchDiagnosticContext context,
                CancellationToken cancellationToken) => throw new InvalidOperationException();
            public Task CompleteCanceledStartupAsync(Process process) => Task.CompletedTask;
            public GameLaunchSession CreateGameLaunchSession(Process process, LaunchDiagnosticContext context)
            {
                GameSessionCreated = true;
                return new GameLaunchSession(
                    context.InstanceId,
                    context.InstanceName,
                    Task.FromResult(LaunchExitResult.Success));
            }
        }
    }

    private sealed class ImmediateWindowReadinessWaiter : IGameWindowReadinessWaiter
    {
        public Task<GameWindowReadinessResult> WaitAsync(Process process, CancellationToken cancellationToken) =>
            Task.FromResult(GameWindowReadinessResult.WindowVisible);
    }

    private sealed class ControlledWindowReadinessWaiter : IGameWindowReadinessWaiter
    {
        private readonly TaskCompletionSource called = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<GameWindowReadinessResult> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<GameWindowReadinessResult> WaitAsync(Process process, CancellationToken cancellationToken)
        {
            called.TrySetResult();
            return await completion.Task.WaitAsync(cancellationToken);
        }

        public Task WaitUntilCalledAsync() => called.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public void Complete(GameWindowReadinessResult result) => completion.TrySetResult(result);
    }
}
