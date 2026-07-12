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
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;
using Launcher.Application;
using Launcher.Application.Accounts;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Minecraft;

/// <summary>
/// 编排启动设置、文件检查、账户会话、Java 选择、进程监控和脱敏失败诊断。
/// </summary>
public sealed class LaunchService : ILaunchService
{
    private readonly ILaunchAccountSessionService accountSessionService;
    private readonly IManagedVersionRepairService versionRepairService;
    private readonly ILaunchGameLauncherFactory launcherFactory;
    private readonly ILaunchCrashMonitor crashMonitor;
    private readonly ILaunchCommandRunner commandRunner;
    private readonly IJavaRuntimeSelectionService? javaRuntimeSelectionService;
    private readonly IJavaRuntimeProvisioningService? javaRuntimeProvisioningService;
    private readonly IGameLanguageService? gameLanguageService;
    private readonly IAuthlibInjectorProvisioningService? authlibInjectorProvisioningService;
    private readonly LaunchSettingsResolver launchSettingsResolver;
    private readonly ILogger<LaunchService> logger;

    public LaunchService(
        ILaunchAccountSessionService accountSessionService,
        IJavaRuntimeSelectionService javaRuntimeSelectionService,
        IJavaRuntimeProvisioningService? javaRuntimeProvisioningService = null,
        IDownloadSpeedLimitState? downloadSpeedLimitState = null,
        ISystemMemoryService? systemMemoryService = null,
        IModService? modService = null,
        IGameLanguageService? gameLanguageService = null,
        ILogger<LaunchService>? logger = null,
        IAuthlibInjectorProvisioningService? authlibInjectorProvisioningService = null)
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
            logger,
            authlibInjectorProvisioningService)
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
        ILogger<LaunchService>? logger = null,
        IAuthlibInjectorProvisioningService? authlibInjectorProvisioningService = null)
    {
        this.accountSessionService = accountSessionService;
        this.versionRepairService = versionRepairService;
        this.launcherFactory = launcherFactory;
        this.crashMonitor = crashMonitor;
        this.commandRunner = commandRunner ?? new LaunchCommandRunner();
        this.javaRuntimeSelectionService = javaRuntimeSelectionService;
        this.javaRuntimeProvisioningService = javaRuntimeProvisioningService;
        this.gameLanguageService = gameLanguageService;
        this.authlibInjectorProvisioningService = authlibInjectorProvisioningService;
        this.logger = logger ?? NullLogger<LaunchService>.Instance;
        launchSettingsResolver = new LaunchSettingsResolver(systemMemoryService, modService, this.logger);
    }

    /// <summary>
    /// 执行一次完整启动并返回可观察退出结果的会话；失败统一转换为带诊断报告的异常。
    /// </summary>
    public async Task<GameLaunchSession> LaunchAsync(
        GameInstance instance,
        LauncherAccount account,
        LauncherSettings settings,
        IProgress<LauncherProgress>? progress,
        LaunchRequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedSettings = await launchSettingsResolver
            .ResolveAsync(instance, settings, cancellationToken)
            .ConfigureAwait(false);
        var versionName = resolvedSettings.VersionName;
        var memoryMb = resolvedSettings.MemoryMb;
        System.Diagnostics.Process? process = null;
        JavaRuntimeInfo? selectedJavaRuntime = null;
        LaunchDiagnosticContext diagnosticContext = CreateDiagnosticContext(
            instance,
            settings,
            versionName,
            javaRuntime: null,
            memoryMb,
            sensitiveValues: []);
        var launchAttemptStartedAt = DateTimeOffset.UtcNow;
        LaunchSessionDiagnosticCollector? failureDiagnosticCollector = null;
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
            RedactLaunchSettingText(resolvedSettings.PreLaunchCommand),
            resolvedSettings.WaitForPreLaunchCommand,
            RedactLaunchSettingText(resolvedSettings.PostExitCommand),
            RedactLaunchSettingText(resolvedSettings.JvmArguments),
            RedactLaunchSettingText(resolvedSettings.GameArguments));

        try
        {
            failureDiagnosticCollector = new LaunchSessionDiagnosticCollector(
                settings.MinecraftDirectory,
                instance.InstanceDirectory);
            var preparedRuntime = await PrepareRuntimeAsync(
                    instance,
                    account,
                    settings,
                    resolvedSettings,
                    options,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);
            selectedJavaRuntime = preparedRuntime.JavaRuntime;
            diagnosticContext = preparedRuntime.DiagnosticContext;

            var startedProcess = await BuildAndStartProcessAsync(
                    instance,
                    settings,
                    resolvedSettings,
                    preparedRuntime,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);
            process = startedProcess.Process;

            var quickExitResult = await startedProcess.CrashMonitorSession.WaitForQuickExitAsync(
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
            var session = startedProcess.CrashMonitorSession.CreateGameLaunchSession(process, diagnosticContext);
            return new GameLaunchSession(
                session.InstanceId,
                session.InstanceName,
                LogGameExitAsync(session.ExitTask, diagnosticContext));
        }
        catch (LaunchProcessExitedException)
        {
            // 快速退出已经生成完整报告，保留原异常可避免再次写入重复诊断。
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
            if (exception.Reason == LaunchAccountSessionFailureReason.ReauthenticationRequired)
                throw;

            var report = await WriteFailureDiagnosticAsync(
                diagnosticContext,
                "account_session_failed",
                "Failed to create a valid Minecraft account session.",
                exception,
                process?.StartInfo,
                failureDiagnosticCollector,
                launchAttemptStartedAt,
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
                failureDiagnosticCollector,
                launchAttemptStartedAt,
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
                failureDiagnosticCollector,
                launchAttemptStartedAt,
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
                failureDiagnosticCollector,
                launchAttemptStartedAt,
                cancellationToken);
            throw new LaunchFailedException(report, exception);
        }
    }

    /// <summary>
    /// 依次执行前置命令、文件检查、账户会话、语言同步和 Java 选择。
    /// </summary>
    private async Task<PreparedLaunchRuntime> PrepareRuntimeAsync(
        GameInstance instance,
        LauncherAccount account,
        LauncherSettings settings,
        ResolvedLaunchSettings resolvedSettings,
        LaunchRequestOptions? options,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken)
    {
        // 准备步骤严格早于进程构建，确保诊断上下文包含最终账户、Java 和修复结果。
        if (!string.IsNullOrWhiteSpace(resolvedSettings.PreLaunchCommand))
        {
            logger.LogInformation(
                "Running pre-launch command. InstanceId={InstanceId} WaitForExit={WaitForExit}",
                instance.Id,
                resolvedSettings.WaitForPreLaunchCommand);
            progress?.Report(new LauncherProgress(
                LaunchProgressStages.RunningPreLaunchCommand,
                "Running pre-launch command",
                4));
            await commandRunner.RunAsync(
                    resolvedSettings.PreLaunchCommand,
                    instance.InstanceDirectory,
                    resolvedSettings.WaitForPreLaunchCommand,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (resolvedSettings.CheckFilesBeforeLaunch)
        {
            logger.LogInformation(
                "Checking game files before launch. VersionName={VersionName} AutoRepair={AutoRepair}",
                resolvedSettings.VersionName,
                resolvedSettings.AutoRepairMissingFiles);
            await versionRepairService.RepairAsync(
                    settings.MinecraftDirectory,
                    resolvedSettings.VersionName,
                    instance.InstanceDirectory,
                    progress,
                    resolvedSettings.AutoRepairMissingFiles,
                    cancellationToken,
                    downloadSourcePreference: settings.DownloadSourcePreference,
                    downloadSpeedLimitMbPerSecond: settings.DownloadSpeedLimitMbPerSecond)
                .ConfigureAwait(false);
        }

        var accountSession = await accountSessionService.CreateSessionAsync(account, cancellationToken)
            .ConfigureAwait(false);
        AuthlibInjectorArtifact? authlibInjector = null;
        if (accountSession.ThirdParty is not null)
        {
            if (authlibInjectorProvisioningService is null)
                throw new InvalidOperationException("The authlib-injector provisioning service is unavailable.");
            authlibInjector = await authlibInjectorProvisioningService
                .EnsureAvailableAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        await ApplyGameLanguageAsync(instance, settings, cancellationToken).ConfigureAwait(false);
        var javaRuntime = await ResolveJavaRuntimeForLaunchAsync(
                instance,
                settings,
                options,
                progress,
                cancellationToken)
            .ConfigureAwait(false);
        var diagnosticContext = CreateDiagnosticContext(
            instance,
            settings,
            resolvedSettings.VersionName,
            javaRuntime,
            resolvedSettings.MemoryMb,
            // token 只作为诊断写入器的敏感值表传入，用于替换日志内容，不会被直接记录。
            [accountSession.AccessToken]);
        logger.LogInformation(
            "Launch account session and Java runtime prepared. InstanceId={InstanceId} JavaSelected={JavaSelected} JavaPath={JavaPath} JavaVersion={JavaVersion} JavaSource={JavaSource}",
            instance.Id,
            javaRuntime is not null,
            javaRuntime?.ExecutablePath,
            javaRuntime?.Version,
            javaRuntime?.Source);
        progress?.Report(new LauncherProgress(
            LaunchProgressStages.PreparingProcess,
            "Preparing launch process",
            94));
        return new PreparedLaunchRuntime(accountSession, authlibInjector, javaRuntime, diagnosticContext);
    }

    /// <summary>
    /// 构建隔离路径与 CmlLib 启动参数，挂接崩溃监控和退出命令后启动进程。
    /// </summary>
    private async Task<StartedLaunchProcess> BuildAndStartProcessAsync(
        GameInstance instance,
        LauncherSettings settings,
        ResolvedLaunchSettings resolvedSettings,
        PreparedLaunchRuntime preparedRuntime,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken)
    {
        var launcher = launcherFactory.Create(
            settings.MinecraftDirectory,
            progress,
            settings.DownloadSpeedLimitMbPerSecond);
        var launchOption = new MLaunchOption
        {
            Path = CreateIsolatedLaunchPath(settings.MinecraftDirectory, resolvedSettings.VersionName),
            Session = new MSession(
                preparedRuntime.AccountSession.Username,
                preparedRuntime.AccountSession.AccessToken,
                preparedRuntime.AccountSession.Uuid),
            MaximumRamMb = resolvedSettings.MemoryMb,
            ScreenWidth = instance.WindowWidth,
            ScreenHeight = instance.WindowHeight,
            FullScreen = resolvedSettings.LaunchFullScreen,
            GameLauncherName = "Launcher",
            GameLauncherVersion = "0.1",
            JavaPath = ResolveWindowlessJavaPath(preparedRuntime.JavaRuntime?.ExecutablePath)
        };
        var extraJvmArguments = new List<MArgument>();
        if (preparedRuntime.AccountSession.ThirdParty is { } thirdParty
            && preparedRuntime.AuthlibInjector is { } injector)
        {
            extraJvmArguments.Add(new MArgument(
                $"-javaagent:{injector.FilePath}={thirdParty.AuthenticationServerUrl}"));
            extraJvmArguments.Add(new MArgument(
                $"-Dauthlibinjector.yggdrasil.prefetched={thirdParty.PrefetchedMetadata}"));
            launchOption.UserProperties = "{}";
            launchOption.ArgumentDictionary = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["user_type"] = "mojang"
            };
        }
        if (!string.IsNullOrWhiteSpace(resolvedSettings.JvmArguments))
            extraJvmArguments.Add(MArgument.FromCommandLine(resolvedSettings.JvmArguments));
        if (extraJvmArguments.Count > 0)
            launchOption.ExtraJvmArguments = extraJvmArguments;
        if (!string.IsNullOrWhiteSpace(resolvedSettings.GameArguments))
            launchOption.ExtraGameArguments = [MArgument.FromCommandLine(resolvedSettings.GameArguments)];

        var process = await launcher.BuildProcessAsync(
                resolvedSettings.VersionName,
                launchOption,
                cancellationToken)
            .ConfigureAwait(false);
        var crashMonitorSession = crashMonitor.CreateSession(
            settings.MinecraftDirectory,
            instance.InstanceDirectory,
            resolvedSettings.VersionName);
        crashMonitorSession.Configure(process);
        ConfigurePostExitCommand(process, resolvedSettings.PostExitCommand, instance.InstanceDirectory);
        progress?.Report(new LauncherProgress(LaunchProgressStages.StartingProcess, "Starting game process", 100));
        if (!process.Start())
            throw new InvalidOperationException("Minecraft process did not start.");

        logger.LogInformation(
            "Minecraft process started. VersionName={VersionName} ProcessId={ProcessId}",
            resolvedSettings.VersionName,
            process.Id);
        return new StartedLaunchProcess(process, crashMonitorSession);
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
            // 语言同步属于启动增强项，失败不应阻止游戏启动。
            logger.LogWarning(
                exception,
                "Failed to synchronize game language before launch. InstanceId={InstanceId} LauncherLanguage={LauncherLanguage}",
                instance.Id,
                LauncherLanguages.Normalize(settings.LauncherLanguage));
        }
    }

    /// <summary>
    /// 选择兼容 Java；仅在自动运行时缺失时安装内置运行时并重试一次。
    /// </summary>
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
            // 仅自动发现缺失时允许安装内置 Java 后重试；用户手动配置错误仍应直接反馈。
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

    /// <summary>
    /// 尽力写入脱敏诊断文件；诊断写入失败时仍返回可展示的最小失败报告。
    /// </summary>
    private async Task<LaunchFailureReport> WriteFailureDiagnosticAsync(
        LaunchDiagnosticContext context,
        string failureKind,
        string failureSummary,
        Exception exception,
        System.Diagnostics.ProcessStartInfo? startInfo,
        LaunchSessionDiagnosticCollector? diagnosticCollector,
        DateTimeOffset launchAttemptStartedAt,
        CancellationToken cancellationToken)
    {
        LaunchDiagnosticResult? diagnostic = null;
        try
        {
            IReadOnlyList<LaunchDiagnosticReference> diagnosticCandidates = diagnosticCollector is null
                ? []
                : await diagnosticCollector
                    .CollectAsync(
                        launchAttemptStartedAt,
                        capturedOutputPath: null,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            diagnostic = await LaunchDiagnosticsWriter.WriteExceptionDiagnosticAsync(
                context,
                failureKind,
                failureSummary,
                exception,
                startInfo,
                launchAttemptStartedAt,
                diagnosticCandidates,
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
            diagnostic?.FailureSummary ?? LaunchDiagnosticRedactor.Redact(failureSummary, context.SensitiveValues))
        {
            DiagnosticCandidates = diagnostic?.DiagnosticCandidates ?? [],
            ExportSensitiveValues = context.SensitiveValues.ToArray()
        };
        LogLaunchFailureReport(
            LogLevel.Error,
            "Game launch failed.",
            report,
            context,
            exception,
            failureKind);
        return report;
    }

    /// <summary>
    /// 等待进程退出分析结果并记录成功退出或崩溃诊断摘要。
    /// </summary>
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
            LaunchFailureCategory.ModVersionIncompatible => FormatModVersionIncompatible(analysis),
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

    private static string FormatModVersionIncompatible(LaunchFailureAnalysis analysis)
    {
        var detail = analysis.Details.FirstOrDefault();
        if (detail is null)
            return "Mod versions appear to be incompatible. Check whether the installed mods match this Minecraft and loader version.";

        var modText = string.IsNullOrWhiteSpace(detail.ModName) ? "A mod" : $"Mod '{detail.ModName}'";
        var dependencyText = string.IsNullOrWhiteSpace(detail.DependencyName)
            ? "a dependency"
            : $"dependency '{detail.DependencyName}'";
        var requiredText = string.IsNullOrWhiteSpace(detail.RequiredVersion)
            ? string.Empty
            : $" Required: {detail.RequiredVersion}.";
        var currentText = string.IsNullOrWhiteSpace(detail.CurrentVersion)
            ? string.Empty
            : $" Current: {detail.CurrentVersion}.";
        return $"{modText} has an incompatible {dependencyText}.{requiredText}{currentText}";
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

    /// <summary>
    /// 脱敏自由格式启动参数中的 token、session、password 和 secret 值。
    /// </summary>
    private static string RedactLaunchSettingText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        return LaunchDiagnosticRedactor.Redact(text.Trim(), []);
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
            return Path.GetDirectoryName(diagnosticPath) ?? Path.Combine(
                context.InstanceDirectory,
                LauncherApplicationIdentity.StorageDirectoryName,
                "logs");

        return Path.Combine(
            context.InstanceDirectory,
            LauncherApplicationIdentity.StorageDirectoryName,
            "logs");
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

    private sealed record PreparedLaunchRuntime(
        LaunchAccountSession AccountSession,
        AuthlibInjectorArtifact? AuthlibInjector,
        JavaRuntimeInfo? JavaRuntime,
        LaunchDiagnosticContext DiagnosticContext);

    private sealed record StartedLaunchProcess(
        System.Diagnostics.Process Process,
        ILaunchCrashMonitorSession CrashMonitorSession);
}
