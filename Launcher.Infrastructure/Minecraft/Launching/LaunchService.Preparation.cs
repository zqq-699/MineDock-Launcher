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
using Launcher.Infrastructure.Accounts.ThirdParty;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Minecraft;

public sealed partial class LaunchService
{
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
            var repairResult = await gameFileIntegrityService.ValidateAndRepairAsync(
                    new GameFileIntegrityRequest(
                        settings.MinecraftDirectory,
                        resolvedSettings.VersionName,
                        instance.InstanceDirectory,
                        settings.DownloadSourcePreference,
                        settings.DownloadSpeedLimitMbPerSecond)
                    {
                        LoaderIdentity = new GameFileLoaderIdentity(
                            instance.Loader,
                            instance.MinecraftVersion,
                            instance.LoaderVersion)
                    },
                    new GameFileRepairOptions(resolvedSettings.AutoRepairMissingFiles),
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!repairResult.LaunchAllowed)
                throw new InstanceRepairException(CreateIntegrityFailureMessage(repairResult));
        }

        var accountSession = await accountSessionService.CreateSessionAsync(account, cancellationToken)
            .ConfigureAwait(false);
        AuthlibInjectorArtifact? authlibInjector = null;
        if (accountSession.ThirdParty is not null)
        {
            if (authlibInjectorProvisioningService is null)
                throw new InvalidOperationException("The authlib-injector provisioning service is unavailable.");
            authlibInjector = authlibInjectorProvisioningService is AuthlibInjectorProvisioningService concreteProvisioner
                ? await concreteProvisioner.EnsureAvailableAsync(progress, cancellationToken).ConfigureAwait(false)
                : await authlibInjectorProvisioningService.EnsureAvailableAsync(cancellationToken).ConfigureAwait(false);
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
        var allowedAdditionalCommandFiles = FinalLaunchCommandPathReader
            .ReadAllowedUserFilePaths(resolvedSettings.JvmArguments, process.StartInfo.WorkingDirectory)
            .ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        if (preparedRuntime.AuthlibInjector is { } trustedInjector)
            allowedAdditionalCommandFiles.Add(Path.GetFullPath(trustedInjector.FilePath));
        var finalValidation = await gameFileIntegrityService.ValidateFinalLaunchCommandAsync(
                new GameFileIntegrityRequest(
                    settings.MinecraftDirectory,
                    resolvedSettings.VersionName,
                    instance.InstanceDirectory,
                    settings.DownloadSourcePreference,
                    settings.DownloadSpeedLimitMbPerSecond)
                {
                    LoaderIdentity = new GameFileLoaderIdentity(
                        instance.Loader,
                        instance.MinecraftVersion,
                        instance.LoaderVersion),
                    AllowedAdditionalCommandFilePaths = allowedAdditionalCommandFiles.ToArray()
                },
                process.StartInfo,
                cancellationToken)
            .ConfigureAwait(false);
        if (!finalValidation.LaunchAllowed)
        {
            process.Dispose();
            throw new InstanceRepairException(CreateIntegrityFailureMessage(finalValidation));
        }
        var crashMonitorSession = crashMonitor.CreateSession(
            settings.MinecraftDirectory,
            instance.InstanceDirectory,
            resolvedSettings.VersionName);
        crashMonitorSession.Configure(process);
        ConfigurePostExitCommand(process, resolvedSettings.PostExitCommand, instance.InstanceDirectory);
        progress?.Report(new LauncherProgress(LaunchProgressStages.StartingProcess, "Starting game process", 99));
        if (!process.Start())
            throw new InvalidOperationException("Minecraft process did not start.");
        crashMonitorSession.BeginMonitoring(process, preparedRuntime.DiagnosticContext);

        logger.LogInformation(
            "Minecraft process started; waiting for a visible game window. VersionName={VersionName} ProcessId={ProcessId}",
            resolvedSettings.VersionName,
            process.Id);
        return new StartedLaunchProcess(process, crashMonitorSession);
    }

    private static string CreateIntegrityFailureMessage(GameFileRepairResult result)
    {
        var first = result.Failures.FirstOrDefault();
        return first is null
            ? "Required game files are missing or damaged."
            : $"Required game file validation failed ({first.Reason}): {first.TargetPath}";
    }
}
