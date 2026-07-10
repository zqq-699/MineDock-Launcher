/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Diagnostics;
using CmlLib.Core.ProcessBuilder;
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
            BuildProcess = (_, _) => CreateCommandProcess("/c echo Missing launch target 1>&2 & exit 1")
        };
        var service = CreateService(launcher: launcher, crashMonitor: new LaunchCrashMonitor(TimeSpan.FromSeconds(2)));

        var exception = await Assert.ThrowsAsync<LaunchProcessExitedException>(() =>
            service.LaunchAsync(instance, CreateAccount(), settings, null));

        Assert.Equal(LaunchFailureKind.StartupAbnormalExit, exception.Report.Kind);
        Assert.True(File.Exists(exception.DiagnosticPath));
        var diagnostic = await File.ReadAllTextAsync(exception.DiagnosticPath!);
        Assert.Contains("Missing launch target", diagnostic);
        Assert.DoesNotContain("super-secret-access-token", diagnostic);
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
        Assert.Contains("instance_repair_failed", await File.ReadAllTextAsync(exception.Report.DiagnosticPath!));
    }

    private static LaunchService CreateService(
        FakeRepairService? repair = null,
        FakeLauncherFactory? launcher = null,
        ILaunchCrashMonitor? crashMonitor = null,
        IJavaRuntimeSelectionService? javaSelection = null,
        IJavaRuntimeProvisioningService? javaProvisioning = null) =>
        new(new FakeAccountSession(), repair ?? new FakeRepairService(), launcher ?? new FakeLauncherFactory(),
            crashMonitor ?? new NoOpCrashMonitor(), javaRuntimeSelectionService: javaSelection,
            javaRuntimeProvisioningService: javaProvisioning);

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
        IsOffline = true
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

    private sealed class FakeAccountSession : ILaunchAccountSessionService
    {
        public Task<LaunchAccountSession> CreateSessionAsync(LauncherAccount account, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LaunchAccountSession(account.DisplayName, "super-secret-access-token", account.Uuid!, account.IsOffline));
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
            public Task<LaunchCrashMonitorResult?> WaitForQuickExitAsync(Process process, LaunchDiagnosticContext context,
                CancellationToken cancellationToken) => Task.FromResult<LaunchCrashMonitorResult?>(null);
            public GameLaunchSession CreateGameLaunchSession(Process process, LaunchDiagnosticContext context) =>
                new(context.InstanceId, context.InstanceName, Task.FromResult(LaunchExitResult.Success));
        }
    }
}
