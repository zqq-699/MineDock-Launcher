/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO;
using System.Text.RegularExpressions;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;
using Launcher.Application.Accounts;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Minecraft;

public sealed class LaunchService : ILaunchService
{
    private readonly ILaunchAccountSessionService accountSessionService;
    private readonly IManagedVersionRepairService versionRepairService;
    private readonly ILaunchGameLauncherFactory launcherFactory;
    private readonly ILaunchCrashMonitor crashMonitor;
    private readonly ILaunchCommandRunner commandRunner;
    private readonly IJavaRuntimeSelectionService? javaRuntimeSelectionService;
    private readonly IJavaRuntimeProvisioningService? javaRuntimeProvisioningService;
    private readonly ISystemMemoryService? systemMemoryService;
    private readonly IModService? modService;
    private readonly IGameLanguageService? gameLanguageService;
    private readonly ILogger<LaunchService> logger;

    public LaunchService(
        ILaunchAccountSessionService accountSessionService,
        IJavaRuntimeSelectionService javaRuntimeSelectionService,
        IJavaRuntimeProvisioningService? javaRuntimeProvisioningService = null,
        IDownloadSpeedLimitState? downloadSpeedLimitState = null,
        ISystemMemoryService? systemMemoryService = null,
        IModService? modService = null,
        IGameLanguageService? gameLanguageService = null,
        ILogger<LaunchService>? logger = null)
        : this(
            accountSessionService,
            new ManagedVersionRepairService(downloadSpeedLimitState: downloadSpeedLimitState),
            new LaunchGameLauncherFactory(downloadSpeedLimitState),
            new LaunchCrashMonitor(),
            new LaunchCommandRunner(),
            javaRuntimeSelectionService,
            javaRuntimeProvisioningService,
            systemMemoryService,
            modService,
            gameLanguageService,
            logger)
    {
    }

    internal LaunchService(
        ILaunchAccountSessionService accountSessionService,
        IManagedVersionRepairService versionRepairService,
        ILaunchGameLauncherFactory launcherFactory,
        ILaunchCrashMonitor crashMonitor,
        ILaunchCommandRunner? commandRunner = null,
        IJavaRuntimeSelectionService? javaRuntimeSelectionService = null,
        IJavaRuntimeProvisioningService? javaRuntimeProvisioningService = null,
        ISystemMemoryService? systemMemoryService = null,
        IModService? modService = null,
        IGameLanguageService? gameLanguageService = null,
        ILogger<LaunchService>? logger = null)
    {
        this.accountSessionService = accountSessionService;
        this.versionRepairService = versionRepairService;
        this.launcherFactory = launcherFactory;
        this.crashMonitor = crashMonitor;
        this.commandRunner = commandRunner ?? new LaunchCommandRunner();
        this.javaRuntimeSelectionService = javaRuntimeSelectionService;
        this.javaRuntimeProvisioningService = javaRuntimeProvisioningService;
        this.systemMemoryService = systemMemoryService;
        this.modService = modService;
        this.gameLanguageService = gameLanguageService;
        this.logger = logger ?? NullLogger<LaunchService>.Instance;
    }

    public async Task<GameLaunchSession> LaunchAsync(
        GameInstance instance,
        LauncherAccount account,
        LauncherSettings settings,
        IProgress<LauncherProgress>? progress,
        LaunchRequestOptions? options = null,
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
        var memoryMb = await ResolveMemoryMbAsync(instance, settings, cancellationToken);
        System.Diagnostics.Process? process = null;
        JavaRuntimeInfo? selectedJavaRuntime = null;
        LaunchDiagnosticContext diagnosticContext = CreateDiagnosticContext(
            instance,
            settings,
            versionName,
            javaRuntime: null,
            memoryMb,
            sensitiveValues: []);
        logger.LogInformation(
            "Game launch started. InstanceId={InstanceId} InstanceName={InstanceName} VersionName={VersionName} Loader={Loader} MemoryMb={MemoryMb}",
            instance.Id,
            instance.Name,
            versionName,
            instance.Loader,
            memoryMb);
        logger.LogInformation(
            "Launch settings resolved. InstanceId={InstanceId} PreLaunchCommand={PreLaunchCommand} WaitForPreLaunchCommand={WaitForPreLaunchCommand} PostExitCommand={PostExitCommand} ExtraJvmArguments={ExtraJvmArguments} ExtraGameArguments={ExtraGameArguments}",
            instance.Id,
            RedactLaunchSettingText(preLaunchCommand),
            shouldWaitForPreLaunchCommand,
            RedactLaunchSettingText(postExitCommand),
            RedactLaunchSettingText(jvmArguments),
            RedactLaunchSettingText(gameArguments));

        try
        {
            if (!string.IsNullOrWhiteSpace(preLaunchCommand))
            {
                logger.LogInformation(
                    "Running pre-launch command. InstanceId={InstanceId} WaitForExit={WaitForExit}",
                    instance.Id,
                    shouldWaitForPreLaunchCommand);
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
                logger.LogInformation(
                    "Checking game files before launch. VersionName={VersionName} AutoRepair={AutoRepair}",
                    versionName,
                    shouldAutoRepairMissingFiles);
                await versionRepairService.RepairAsync(
                    settings.MinecraftDirectory,
                    versionName,
                    instance.InstanceDirectory,
                    progress,
                    shouldAutoRepairMissingFiles,
                    cancellationToken,
                    downloadSourcePreference: settings.DownloadSourcePreference,
                    downloadSpeedLimitMbPerSecond: settings.DownloadSpeedLimitMbPerSecond);
            }

            var accountSession = await accountSessionService.CreateSessionAsync(account, cancellationToken);
            await ApplyGameLanguageAsync(instance, settings, cancellationToken);
            selectedJavaRuntime = await ResolveJavaRuntimeForLaunchAsync(
                instance,
                settings,
                options,
                progress,
                cancellationToken);
            diagnosticContext = CreateDiagnosticContext(
                instance,
                settings,
                versionName,
                selectedJavaRuntime,
                memoryMb,
                [accountSession.AccessToken]);
            logger.LogInformation(
                "Launch account session and Java runtime prepared. InstanceId={InstanceId} JavaSelected={JavaSelected} JavaPath={JavaPath} JavaVersion={JavaVersion} JavaSource={JavaSource}",
                instance.Id,
                selectedJavaRuntime is not null,
                selectedJavaRuntime?.ExecutablePath,
                selectedJavaRuntime?.Version,
                selectedJavaRuntime?.Source);
            progress?.Report(new LauncherProgress(
                LaunchProgressStages.PreparingProcess,
                "Preparing launch process",
                94));

            var launcher = launcherFactory.Create(
                settings.MinecraftDirectory,
                progress,
                settings.DownloadSpeedLimitMbPerSecond);
            var isolatedPath = CreateIsolatedLaunchPath(settings.MinecraftDirectory, versionName);
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
            logger.LogInformation("Minecraft process started. VersionName={VersionName} ProcessId={ProcessId}", versionName, process.Id);

            var quickExitResult = await crashMonitorSession.WaitForQuickExitAsync(
                process,
                diagnosticContext,
                cancellationToken);
            if (quickExitResult is not null)
            {
                LogLaunchFailureReport(
                    LogLevel.Warning,
                    "Minecraft process exited during startup.",
                    quickExitResult.Report,
                    diagnosticContext);
                throw new LaunchProcessExitedException(quickExitResult.Report);
            }
            var session = crashMonitorSession.CreateGameLaunchSession(process, diagnosticContext);
            return new GameLaunchSession(
                session.InstanceId,
                session.InstanceName,
                LogGameExitAsync(session.ExitTask, diagnosticContext));
        }
        catch (LaunchProcessExitedException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation(
                "Game launch canceled. InstanceId={InstanceId} InstanceName={InstanceName} VersionName={VersionName} JavaPath={JavaPath} JavaVersion={JavaVersion} JavaSource={JavaSource}",
                diagnosticContext.InstanceId,
                diagnosticContext.InstanceName,
                diagnosticContext.VersionName,
                diagnosticContext.JavaPath,
                diagnosticContext.JavaVersion,
                diagnosticContext.JavaSource);
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

    private async Task ApplyGameLanguageAsync(
        GameInstance instance,
        LauncherSettings settings,
        CancellationToken cancellationToken)
    {
        if (!settings.AutoSetGameLanguageToLauncherLanguage || gameLanguageService is null)
            return;

        try
        {
            await gameLanguageService.ApplyLauncherLanguageAsync(
                instance,
                settings.LauncherLanguage,
                cancellationToken);
            logger.LogInformation(
                "Game language synchronized with launcher language. InstanceId={InstanceId} LauncherLanguage={LauncherLanguage}",
                instance.Id,
                LauncherLanguages.Normalize(settings.LauncherLanguage));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to synchronize game language before launch. InstanceId={InstanceId} LauncherLanguage={LauncherLanguage}",
                instance.Id,
                LauncherLanguages.Normalize(settings.LauncherLanguage));
        }
    }

    private async Task<JavaRuntimeInfo?> ResolveJavaRuntimeForLaunchAsync(
        GameInstance instance,
        LauncherSettings settings,
        LaunchRequestOptions? options,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (javaRuntimeSelectionService is null)
            return null;

        try
        {
            progress?.Report(new LauncherProgress(LaunchProgressStages.CheckingJava, string.Empty, 90));
            return await javaRuntimeSelectionService.SelectForLaunchAsync(instance, settings, options, cancellationToken);
        }
        catch (JavaRuntimeSelectionException exception)
            when (javaRuntimeProvisioningService is not null && IsAutomaticJavaRuntimeDiscoveryFailure(exception.Reason))
        {
            logger.LogInformation(
                exception,
                "Automatic Java runtime selection failed. Preparing bundled Java runtime before retrying. InstanceId={InstanceId} InstanceName={InstanceName} Reason={Reason} RequiredJavaMajorVersion={RequiredJavaMajorVersion}",
                instance.Id,
                instance.Name,
                exception.Reason,
                exception.RequiredMajorVersion);

            await javaRuntimeProvisioningService.EnsureForLaunchAsync(
                instance,
                settings,
                progress,
                cancellationToken);

            logger.LogInformation(
                "Retrying Java runtime selection after provisioning. InstanceId={InstanceId} InstanceName={InstanceName}",
                instance.Id,
                instance.Name);
            return await javaRuntimeSelectionService.SelectForLaunchAsync(instance, settings, options, cancellationToken);
        }
    }

    private static bool IsAutomaticJavaRuntimeDiscoveryFailure(JavaRuntimeSelectionFailureReason reason)
    {
        return reason is JavaRuntimeSelectionFailureReason.AutomaticRuntimeMissing
            or JavaRuntimeSelectionFailureReason.AutomaticRuntimeNotFound;
    }

    private async Task<LaunchFailureReport> WriteFailureDiagnosticAsync(
        LaunchDiagnosticContext context,
        string failureKind,
        string failureSummary,
        Exception exception,
        System.Diagnostics.ProcessStartInfo? startInfo,
        CancellationToken cancellationToken)
    {
        LaunchDiagnosticResult? diagnostic = null;
        try
        {
            diagnostic = await LaunchDiagnosticsWriter.WriteExceptionDiagnosticAsync(
                context,
                failureKind,
                failureSummary,
                exception,
                startInfo,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(diagnostic.DiagnosticPath))
                exception.Data["DiagnosticPath"] = diagnostic.DiagnosticPath;
        }
        catch (Exception diagnosticException)
        {
            logger.LogWarning(
                diagnosticException,
                "Failed to write launch diagnostic. FailureKind={FailureKind} InstanceId={InstanceId}",
                failureKind,
                context.InstanceId);
        }

        var report = new LaunchFailureReport(
            LaunchFailureKind.StartupFailed,
            context.InstanceName,
            context.VersionName,
            null,
            diagnostic?.DiagnosticPath,
            ResolveDiagnosticDirectory(context, diagnostic?.DiagnosticPath),
            diagnostic?.Analysis,
            diagnostic?.FailureSummary ?? failureSummary);
        LogLaunchFailureReport(
            LogLevel.Error,
            "Game launch failed.",
            report,
            context,
            exception,
            failureKind);
        return report;
    }

    private async Task<int> ResolveMemoryMbAsync(
        GameInstance instance,
        LauncherSettings settings,
        CancellationToken cancellationToken)
    {
        if (instance.LaunchSettingsMode is LaunchSettingsMode.PerInstance && instance.MemoryMb > 0)
        {
            if (instance.MemorySettingsMode is MemorySettingsMode.Manual)
                return instance.MemoryMb;

            try
            {
                return await ResolveAutomaticMemoryMbAsync(instance, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Failed to calculate automatic instance launch memory. Falling back to configured instance memory.");
                return NormalizeConfiguredMemoryMb(instance.MemoryMb);
            }
        }

        if (settings.DefaultMemorySettingsMode is MemorySettingsMode.Manual)
            return NormalizeConfiguredMemoryMb(settings.DefaultMemoryMb);

        if (systemMemoryService is null)
            return NormalizeConfiguredMemoryMb(settings.DefaultMemoryMb);

        try
        {
            return await ResolveAutomaticMemoryMbAsync(instance, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Failed to calculate automatic launch memory. Falling back to configured memory.");
            return NormalizeConfiguredMemoryMb(settings.DefaultMemoryMb);
        }
    }

    private async Task<int> ResolveAutomaticMemoryMbAsync(GameInstance instance, CancellationToken cancellationToken)
    {
        if (systemMemoryService is null)
            return NormalizeConfiguredMemoryMb(instance.MemoryMb);

        var enabledModCount = await CountEnabledModsAsync(instance, cancellationToken);
        return MemoryAllocationCalculator.CalculateAutomaticMemoryMb(
            systemMemoryService.GetSnapshot(),
            instance.Loader,
            enabledModCount);
    }

    private async Task<int> CountEnabledModsAsync(GameInstance instance, CancellationToken cancellationToken)
    {
        if (modService is null || instance.Loader is LoaderKind.Vanilla)
            return 0;

        try
        {
            var mods = await modService.GetModsAsync(instance, cancellationToken);
            return mods.Count(mod => mod.IsEnabled);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Failed to count enabled mods for automatic memory allocation. InstanceId={InstanceId}",
                instance.Id);
            return 0;
        }
    }

    private static int NormalizeConfiguredMemoryMb(int memoryMb)
    {
        return Math.Clamp(
            memoryMb,
            MemoryAllocationCalculator.MinimumMemoryMb,
            MemoryAllocationCalculator.FallbackMaximumMemoryMb);
    }

    private async Task<LaunchExitResult> LogGameExitAsync(
        Task<LaunchExitResult> exitTask,
        LaunchDiagnosticContext context)
    {
        try
        {
            var result = await exitTask.ConfigureAwait(false);
            if (result.FailureReport is null)
            {
                logger.LogInformation(
                    "Minecraft process exited normally. InstanceId={InstanceId} VersionName={VersionName} ExitCode={ExitCode} Runtime={Runtime} JavaPath={JavaPath} JavaVersion={JavaVersion} JavaSource={JavaSource}",
                    context.InstanceId,
                    context.VersionName,
                    result.ExitCode,
                    FormatRuntime(result.Runtime),
                    context.JavaPath,
                    context.JavaVersion,
                    context.JavaSource);
                return result;
            }

            LogLaunchFailureReport(
                LogLevel.Error,
                "Minecraft process crashed after startup.",
                result.FailureReport,
                context);
            return result;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed while monitoring Minecraft process exit. InstanceId={InstanceId} VersionName={VersionName} JavaPath={JavaPath} JavaVersion={JavaVersion} JavaSource={JavaSource}",
                context.InstanceId,
                context.VersionName,
                context.JavaPath,
                context.JavaVersion,
                context.JavaSource);
            throw;
        }
    }

    private void LogLaunchFailureReport(
        LogLevel level,
        string message,
        LaunchFailureReport report,
        LaunchDiagnosticContext context,
        Exception? exception = null,
        string? failureKindText = null)
    {
        logger.Log(
            level,
            exception,
            "{Message} FailureKind={FailureKind} ReportKind={ReportKind} InstanceName={InstanceName} VersionName={VersionName} ExitCode={ExitCode} FailureSummary={FailureSummary} AnalysisText={AnalysisText} DiagnosticPath={DiagnosticPath} AnalysisCategory={AnalysisCategory} JavaPath={JavaPath} JavaVersion={JavaVersion} JavaSource={JavaSource}",
            message,
            failureKindText ?? report.Kind.ToString(),
            report.Kind,
            report.InstanceName,
            report.VersionName,
            report.ExitCode,
            report.FailureSummary,
            FormatAnalysisText(report.Analysis),
            report.DiagnosticPath,
            report.Analysis?.Category,
            context.JavaPath,
            context.JavaVersion,
            context.JavaSource);
    }

    private static string? FormatAnalysisText(LaunchFailureAnalysis? analysis)
    {
        if (analysis is null)
            return null;

        return analysis.Category switch
        {
            LaunchFailureCategory.JavaVersionMismatch => FormatJavaVersionMismatch(analysis),
            LaunchFailureCategory.ModDependencyMissing => FormatModDependencyMissing(analysis),
            LaunchFailureCategory.ModVersionIncompatible => "Mod versions appear to be incompatible. Check whether the installed mods match this Minecraft and loader version.",
            LaunchFailureCategory.MissingGameFiles => FormatMissingGameFiles(analysis),
            LaunchFailureCategory.OutOfMemory => "Minecraft ran out of memory. Increase the allocated memory or reduce loaded mods/resources.",
            _ => $"{analysis.ReasonTitle}: {analysis.ReasonDetail}. {analysis.Recommendation}"
        };
    }

    private static string FormatJavaVersionMismatch(LaunchFailureAnalysis analysis)
    {
        var required = analysis.RequiredJavaMajorVersion?.ToString() ?? "unknown";
        var current = analysis.CurrentJavaMajorVersion?.ToString() ?? "unknown";
        var modText = string.IsNullOrWhiteSpace(analysis.ModName)
            ? string.Empty
            : $" Mod: {analysis.ModName}.";
        return $"Java version mismatch. Required Java: {required}; current Java: {current}.{modText} Select a compatible Java runtime.";
    }

    private static string FormatModDependencyMissing(LaunchFailureAnalysis analysis)
    {
        var modText = string.IsNullOrWhiteSpace(analysis.ModName)
            ? "A mod"
            : $"Mod '{analysis.ModName}'";
        var dependencyText = string.IsNullOrWhiteSpace(analysis.DependencyName)
            ? "a required dependency"
            : $"required dependency '{analysis.DependencyName}'";
        return $"{modText} is missing {dependencyText}. Install the missing dependency and try again.";
    }

    private static string FormatMissingGameFiles(LaunchFailureAnalysis analysis)
    {
        var pathText = string.IsNullOrWhiteSpace(analysis.MissingPath)
            ? string.Empty
            : $" Missing path: {analysis.MissingPath}.";
        return $"Required game files are missing or damaged.{pathText} Repair or reinstall this instance.";
    }

    private static string? FormatRuntime(TimeSpan? runtime)
    {
        return runtime is null
            ? null
            : runtime.Value.ToString(@"hh\:mm\:ss\.fff");
    }

    private static string RedactLaunchSettingText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var redacted = text.Trim();
        redacted = Regex.Replace(
            redacted,
            @"(?i)(--?(?:accessToken|session|token|password|secret)(?:=|\s+))(""[^""]+""|\S+)",
            "$1<redacted>");
        redacted = Regex.Replace(
            redacted,
            @"(?i)\b((?:access[_-]?token|session|token|password|secret)\s*=\s*)(""[^""]+""|\S+)",
            "$1<redacted>");
        return redacted;
    }

    private static LaunchDiagnosticContext CreateDiagnosticContext(
        GameInstance instance,
        LauncherSettings settings,
        string versionName,
        JavaRuntimeInfo? javaRuntime,
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
            javaRuntime?.ExecutablePath,
            javaRuntime?.Version,
            javaRuntime?.Source,
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
        process.Exited += (_, _) => _ = RunPostExitCommandAsync(postExitCommand, workingDirectory);
    }

    private async Task RunPostExitCommandAsync(string postExitCommand, string workingDirectory)
    {
        try
        {
            await commandRunner.RunAsync(
                postExitCommand,
                workingDirectory,
                waitForExit: false,
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Post-exit command failed. WorkingDirectory={WorkingDirectory}", workingDirectory);
        }
    }
}
