using System.IO;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;
using Launcher.Application.Accounts;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Minecraft;

public sealed class LaunchService : ILaunchService
{
    private readonly ILaunchAccountSessionService accountSessionService;
    private readonly IManagedVersionRepairService versionRepairService;
    private readonly ILaunchGameLauncherFactory launcherFactory;
    private readonly ILaunchCrashMonitor crashMonitor;
    private readonly ILaunchCommandRunner commandRunner;
    private readonly IJavaRuntimeSelectionService? javaRuntimeSelectionService;

    public LaunchService(
        ILaunchAccountSessionService accountSessionService,
        IJavaRuntimeSelectionService javaRuntimeSelectionService)
        : this(
            accountSessionService,
            new ManagedVersionRepairService(),
            new LaunchGameLauncherFactory(),
            new LaunchCrashMonitor(),
            new LaunchCommandRunner(),
            javaRuntimeSelectionService)
    {
    }

    internal LaunchService(
        ILaunchAccountSessionService accountSessionService,
        IManagedVersionRepairService versionRepairService,
        ILaunchGameLauncherFactory launcherFactory,
        ILaunchCrashMonitor crashMonitor,
        ILaunchCommandRunner? commandRunner = null,
        IJavaRuntimeSelectionService? javaRuntimeSelectionService = null)
    {
        this.accountSessionService = accountSessionService;
        this.versionRepairService = versionRepairService;
        this.launcherFactory = launcherFactory;
        this.crashMonitor = crashMonitor;
        this.commandRunner = commandRunner ?? new LaunchCommandRunner();
        this.javaRuntimeSelectionService = javaRuntimeSelectionService;
    }

    public async Task LaunchAsync(
        GameInstance instance,
        LauncherAccount account,
        LauncherSettings settings,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        var versionName = string.IsNullOrWhiteSpace(instance.VersionName) ? instance.MinecraftVersion : instance.VersionName;
        var shouldCheckFilesBeforeLaunch = instance.LaunchSettingsMode is LaunchSettingsMode.UseGlobal
            ? settings.DefaultCheckFilesBeforeLaunch
            : instance.CheckFilesBeforeLaunch;
        var shouldAutoRepairMissingFiles = instance.LaunchSettingsMode is LaunchSettingsMode.UseGlobal
            ? settings.DefaultAutoRepairMissingFiles
            : instance.AutoRepairMissingFiles;
        var shouldLaunchFullScreen = instance.LaunchSettingsMode is LaunchSettingsMode.UseGlobal
            ? settings.DefaultLaunchFullScreen
            : instance.LaunchFullScreen;
        var preLaunchCommand = instance.LaunchSettingsMode is LaunchSettingsMode.UseGlobal
            ? settings.DefaultPreLaunchCommand
            : instance.PreLaunchCommand;
        var shouldWaitForPreLaunchCommand = instance.LaunchSettingsMode is LaunchSettingsMode.UseGlobal
            ? settings.DefaultWaitForPreLaunchCommand
            : instance.WaitForPreLaunchCommand;
        var postExitCommand = instance.LaunchSettingsMode is LaunchSettingsMode.UseGlobal
            ? settings.DefaultPostExitCommand
            : instance.PostExitCommand;
        var jvmArguments = instance.LaunchSettingsMode is LaunchSettingsMode.UseGlobal
            ? settings.DefaultJvmArguments
            : instance.JvmArguments;
        var gameArguments = instance.LaunchSettingsMode is LaunchSettingsMode.UseGlobal
            ? settings.DefaultGameArguments
            : instance.GameArguments;
        System.Diagnostics.Process? process = null;

        try
        {
            if (!string.IsNullOrWhiteSpace(preLaunchCommand))
            {
                progress?.Report(new LauncherProgress(
                    LaunchProgressStages.RunningPreLaunchCommand,
                    "Running pre-launch command",
                    4));
                await commandRunner.RunAsync(
                    preLaunchCommand,
                    instance.InstanceDirectory,
                    shouldWaitForPreLaunchCommand,
                    cancellationToken);
            }

            if (shouldCheckFilesBeforeLaunch)
            {
                await versionRepairService.RepairAsync(
                    settings.MinecraftDirectory,
                    versionName,
                    instance.InstanceDirectory,
                    progress,
                    shouldAutoRepairMissingFiles,
                    cancellationToken);
            }

            progress?.Report(new LauncherProgress(
                LaunchProgressStages.PreparingProcess,
                "Preparing launch process",
                94));

            var launcher = launcherFactory.Create(settings.MinecraftDirectory, progress);
            var isolatedPath = CreateIsolatedLaunchPath(settings.MinecraftDirectory, versionName);

            var accountSession = await accountSessionService.CreateSessionAsync(account, cancellationToken);
            var selectedJavaRuntime = javaRuntimeSelectionService is null
                ? null
                : await javaRuntimeSelectionService.SelectForLaunchAsync(instance, settings, cancellationToken);
            var launchOption = new MLaunchOption
            {
                Path = isolatedPath,
                Session = new MSession(
                    accountSession.Username,
                    accountSession.AccessToken,
                    accountSession.Uuid),
                MaximumRamMb = instance.MemoryMb > 0 ? instance.MemoryMb : settings.DefaultMemoryMb,
                ScreenWidth = instance.WindowWidth,
                ScreenHeight = instance.WindowHeight,
                FullScreen = shouldLaunchFullScreen,
                GameLauncherName = "Launcher",
                GameLauncherVersion = "0.1",
                JavaPath = ResolveWindowlessJavaPath(selectedJavaRuntime?.ExecutablePath)
            };

            if (!string.IsNullOrWhiteSpace(jvmArguments))
                launchOption.ExtraJvmArguments = [MArgument.FromCommandLine(jvmArguments)];

            if (!string.IsNullOrWhiteSpace(gameArguments))
                launchOption.ExtraGameArguments = [MArgument.FromCommandLine(gameArguments)];

            process = await launcher.BuildProcessAsync(versionName, launchOption, cancellationToken);
            var crashMonitorSession = crashMonitor.CreateSession(
                settings.MinecraftDirectory,
                instance.InstanceDirectory,
                versionName);
            crashMonitorSession.Configure(process);
            ConfigurePostExitCommand(process, postExitCommand, instance.InstanceDirectory);
            progress?.Report(new LauncherProgress(
                LaunchProgressStages.StartingProcess,
                "Starting game process",
                100));
            process.Start();

            var quickExitResult = await crashMonitorSession.WaitForQuickExitAsync(process, cancellationToken);
            if (quickExitResult is not null)
                throw new LaunchProcessExitedException(quickExitResult.DiagnosticPath);
        }
        catch (LaunchProcessExitedException)
        {
            throw;
        }
        catch (LaunchAccountSessionException exception)
        {
            await WriteFailureDiagnosticAsync(
                settings,
                instance,
                versionName,
                "account_session_failed",
                "Failed to create a valid Minecraft account session.",
                exception,
                process?.StartInfo,
                cancellationToken);
            throw;
        }
        catch (InstanceRepairException exception)
        {
            await WriteFailureDiagnosticAsync(
                settings,
                instance,
                versionName,
                "instance_repair_failed",
                exception.Message,
                exception,
                process?.StartInfo,
                cancellationToken);
            throw;
        }
        catch (JavaRuntimeSelectionException exception)
        {
            await WriteFailureDiagnosticAsync(
                settings,
                instance,
                versionName,
                "java_runtime_selection_failed",
                exception.Message,
                exception,
                process?.StartInfo,
                cancellationToken);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await WriteFailureDiagnosticAsync(
                settings,
                instance,
                versionName,
                "launch_failed",
                exception.Message,
                exception,
                process?.StartInfo,
                cancellationToken);
            throw;
        }
    }

    private static async Task WriteFailureDiagnosticAsync(
        LauncherSettings settings,
        GameInstance instance,
        string versionName,
        string failureKind,
        string failureSummary,
        Exception exception,
        System.Diagnostics.ProcessStartInfo? startInfo,
        CancellationToken cancellationToken)
    {
        try
        {
            var diagnosticPath = await LaunchDiagnosticsWriter.WriteExceptionDiagnosticAsync(
                settings.MinecraftDirectory,
                instance.InstanceDirectory,
                versionName,
                failureKind,
                failureSummary,
                exception,
                startInfo,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(diagnosticPath))
                exception.Data["DiagnosticPath"] = diagnosticPath;
        }
        catch
        {
        }
    }

    private static MinecraftPath CreateIsolatedLaunchPath(string minecraftDirectory, string versionName)
    {
        var sharedPath = new MinecraftPath(minecraftDirectory);
        var versionDirectory = Path.Combine(sharedPath.Versions, versionName);
        var isolatedPath = new MinecraftPath(versionDirectory)
        {
            Versions = sharedPath.Versions,
            Library = sharedPath.Library,
            Assets = sharedPath.Assets,
            Runtime = sharedPath.Runtime
        };

        return isolatedPath;
    }

    internal static string? ResolveWindowlessJavaPath(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)
            || !OperatingSystem.IsWindows()
            || !string.Equals(Path.GetFileName(executablePath), "java.exe", StringComparison.OrdinalIgnoreCase))
        {
            return executablePath;
        }

        var directory = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrWhiteSpace(directory))
            return executablePath;

        var javawPath = Path.Combine(directory, "javaw.exe");
        return File.Exists(javawPath) ? javawPath : executablePath;
    }

    private void ConfigurePostExitCommand(
        System.Diagnostics.Process process,
        string postExitCommand,
        string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(postExitCommand))
            return;

        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await commandRunner.RunAsync(
                        postExitCommand,
                        workingDirectory,
                        waitForExit: false,
                        CancellationToken.None);
                }
                catch
                {
                }
            });
        };
    }
}
