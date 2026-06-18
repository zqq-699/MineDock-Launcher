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

    public async Task<GameLaunchSession> LaunchAsync(
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
        var memoryMb = instance.MemoryMb > 0 ? instance.MemoryMb : settings.DefaultMemoryMb;
        System.Diagnostics.Process? process = null;
        LaunchDiagnosticContext diagnosticContext = CreateDiagnosticContext(
            instance,
            settings,
            versionName,
            javaPath: null,
            memoryMb,
            sensitiveValues: []);

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
            diagnosticContext = CreateDiagnosticContext(
                instance,
                settings,
                versionName,
                selectedJavaRuntime?.ExecutablePath,
                memoryMb,
                [accountSession.AccessToken]);
            var launchOption = new MLaunchOption
            {
                Path = isolatedPath,
                Session = new MSession(
                    accountSession.Username,
                    accountSession.AccessToken,
                    accountSession.Uuid),
                MaximumRamMb = memoryMb,
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
            if (!process.Start())
                throw new InvalidOperationException("Minecraft process did not start.");

            var quickExitResult = await crashMonitorSession.WaitForQuickExitAsync(
                process,
                diagnosticContext,
                cancellationToken);
            if (quickExitResult is not null)
                throw new LaunchProcessExitedException(quickExitResult.Report);

            return crashMonitorSession.CreateGameLaunchSession(process, diagnosticContext);
        }
        catch (LaunchProcessExitedException)
        {
            throw;
        }
        catch (LaunchAccountSessionException exception)
        {
            var report = await WriteFailureDiagnosticAsync(
                diagnosticContext,
                "account_session_failed",
                "Failed to create a valid Minecraft account session.",
                exception,
                process?.StartInfo,
                cancellationToken);
            throw new LaunchFailedException(report, exception);
        }
        catch (InstanceRepairException exception)
        {
            var report = await WriteFailureDiagnosticAsync(
                diagnosticContext,
                "instance_repair_failed",
                exception.Message,
                exception,
                process?.StartInfo,
                cancellationToken);
            throw new LaunchFailedException(report, exception);
        }
        catch (JavaRuntimeSelectionException exception)
        {
            var report = await WriteFailureDiagnosticAsync(
                diagnosticContext,
                "java_runtime_selection_failed",
                exception.Message,
                exception,
                process?.StartInfo,
                cancellationToken);
            throw new LaunchFailedException(report, exception);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var report = await WriteFailureDiagnosticAsync(
                diagnosticContext,
                "launch_failed",
                exception.Message,
                exception,
                process?.StartInfo,
                cancellationToken);
            throw new LaunchFailedException(report, exception);
        }
    }

    private static async Task<LaunchFailureReport> WriteFailureDiagnosticAsync(
        LaunchDiagnosticContext context,
        string failureKind,
        string failureSummary,
        Exception exception,
        System.Diagnostics.ProcessStartInfo? startInfo,
        CancellationToken cancellationToken)
    {
        string? diagnosticPath = null;
        try
        {
            diagnosticPath = await LaunchDiagnosticsWriter.WriteExceptionDiagnosticAsync(
                context,
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

        return new LaunchFailureReport(
            LaunchFailureKind.StartupFailed,
            context.InstanceName,
            context.VersionName,
            null,
            diagnosticPath,
            ResolveDiagnosticDirectory(context, diagnosticPath));
    }

    private static LaunchDiagnosticContext CreateDiagnosticContext(
        GameInstance instance,
        LauncherSettings settings,
        string versionName,
        string? javaPath,
        int memoryMb,
        IReadOnlyList<string> sensitiveValues)
    {
        return new LaunchDiagnosticContext(
            settings.MinecraftDirectory,
            instance.InstanceDirectory,
            instance.Id,
            string.IsNullOrWhiteSpace(instance.Name) ? versionName : instance.Name,
            versionName,
            instance.MinecraftVersion,
            instance.Loader,
            instance.LoaderVersion,
            javaPath,
            memoryMb,
            sensitiveValues);
    }

    private static string ResolveDiagnosticDirectory(LaunchDiagnosticContext context, string? diagnosticPath)
    {
        if (!string.IsNullOrWhiteSpace(diagnosticPath))
            return Path.GetDirectoryName(diagnosticPath) ?? Path.Combine(context.InstanceDirectory, "logs", "launcher");

        return Path.Combine(context.InstanceDirectory, "logs", "launcher");
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
