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

    public LaunchService(ILaunchAccountSessionService accountSessionService)
        : this(
            accountSessionService,
            new ManagedVersionRepairService(),
            new LaunchGameLauncherFactory(),
            new LaunchCrashMonitor())
    {
    }

    internal LaunchService(
        ILaunchAccountSessionService accountSessionService,
        IManagedVersionRepairService versionRepairService,
        ILaunchGameLauncherFactory launcherFactory,
        ILaunchCrashMonitor crashMonitor)
    {
        this.accountSessionService = accountSessionService;
        this.versionRepairService = versionRepairService;
        this.launcherFactory = launcherFactory;
        this.crashMonitor = crashMonitor;
    }

    public async Task LaunchAsync(
        GameInstance instance,
        LauncherAccount account,
        LauncherSettings settings,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        var versionName = string.IsNullOrWhiteSpace(instance.VersionName) ? instance.MinecraftVersion : instance.VersionName;
        var javaPath = string.IsNullOrWhiteSpace(instance.JavaPath) ? settings.DefaultJavaPath : instance.JavaPath;
        var shouldCheckFilesBeforeLaunch = instance.LaunchSettingsMode is LaunchSettingsMode.UseGlobal
            ? settings.DefaultCheckFilesBeforeLaunch
            : instance.CheckFilesBeforeLaunch;
        var shouldAutoRepairMissingFiles = instance.LaunchSettingsMode is LaunchSettingsMode.UseGlobal
            ? settings.DefaultAutoRepairMissingFiles
            : instance.AutoRepairMissingFiles;
        System.Diagnostics.Process? process = null;

        try
        {
            if (shouldCheckFilesBeforeLaunch)
            {
                await versionRepairService.RepairAsync(
                    settings.MinecraftDirectory,
                    versionName,
                    instance.InstanceDirectory,
                    javaPath,
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
                GameLauncherName = "Launcher",
                GameLauncherVersion = "0.1"
            };

            if (!string.IsNullOrWhiteSpace(javaPath))
                launchOption.JavaPath = javaPath;

            if (!string.IsNullOrWhiteSpace(instance.JvmArguments))
                launchOption.ExtraJvmArguments = [MArgument.FromCommandLine(instance.JvmArguments)];

            process = await launcher.BuildProcessAsync(versionName, launchOption, cancellationToken);
            var crashMonitorSession = crashMonitor.CreateSession(
                settings.MinecraftDirectory,
                instance.InstanceDirectory,
                versionName);
            crashMonitorSession.Configure(process);
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
                javaPath,
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
                javaPath,
                "instance_repair_failed",
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
                javaPath,
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
        string? javaPath,
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
                javaPath,
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
}
