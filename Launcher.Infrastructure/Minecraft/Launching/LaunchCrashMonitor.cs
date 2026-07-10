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

using System.Diagnostics;
using System.IO;
using Launcher.Application.Services;

namespace Launcher.Infrastructure.Minecraft;

internal interface ILaunchCrashMonitor
{
    ILaunchCrashMonitorSession CreateSession(string minecraftDirectory, string instanceDirectory, string versionName);
}

internal interface ILaunchCrashMonitorSession
{
    void Configure(Process process);

    Task<LaunchCrashMonitorResult?> WaitForQuickExitAsync(
        Process process,
        LaunchDiagnosticContext context,
        CancellationToken cancellationToken);

    GameLaunchSession CreateGameLaunchSession(Process process, LaunchDiagnosticContext context);
}

internal sealed record LaunchCrashMonitorResult(LaunchFailureReport Report);

internal sealed class LaunchCrashMonitor : ILaunchCrashMonitor
{
    private static readonly TimeSpan DefaultQuickExitThreshold = TimeSpan.FromSeconds(8);
    private readonly TimeSpan quickExitThreshold;

    public LaunchCrashMonitor(TimeSpan? quickExitThreshold = null)
    {
        this.quickExitThreshold = quickExitThreshold ?? DefaultQuickExitThreshold;
    }

    public ILaunchCrashMonitorSession CreateSession(string minecraftDirectory, string instanceDirectory, string versionName)
    {
        return new Session(minecraftDirectory, instanceDirectory, versionName, quickExitThreshold);
    }

    private sealed class Session : ILaunchCrashMonitorSession
    {
        private readonly string minecraftDirectory;
        private readonly string instanceDirectory;
        private readonly string versionName;
        private readonly TimeSpan quickExitThreshold;
        private readonly DateTimeOffset createdAt = DateTimeOffset.UtcNow;
        private readonly HashSet<string> existingCrashFiles;
        private Task<string>? stdoutTask;
        private Task<string>? stderrTask;

        public Session(
            string minecraftDirectory,
            string instanceDirectory,
            string versionName,
            TimeSpan quickExitThreshold)
        {
            this.minecraftDirectory = minecraftDirectory;
            this.instanceDirectory = instanceDirectory;
            this.versionName = versionName;
            this.quickExitThreshold = quickExitThreshold;
            existingCrashFiles = EnumerateCandidateCrashFiles()
                .Select(Path.GetFullPath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        public void Configure(Process process)
        {
            if (process.StartInfo.UseShellExecute)
                return;

            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
        }

        public async Task<LaunchCrashMonitorResult?> WaitForQuickExitAsync(
            Process process,
            LaunchDiagnosticContext context,
            CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            EnsureOutputReadersStarted(process);
            var exitTask = process.WaitForExitAsync(cancellationToken);
            var delayTask = Task.Delay(quickExitThreshold, cancellationToken);

            if (await Task.WhenAny(exitTask, delayTask) != exitTask)
                return null;

            await exitTask;
            var stdout = await stdoutTask!;
            var stderr = await stderrTask!;
            var newCrashFiles = FindNewCrashFiles().ToList();
            var failureKind = IsAbnormalExit(process.ExitCode, newCrashFiles)
                ? LaunchFailureKind.StartupAbnormalExit
                : LaunchFailureKind.StartupProcessExited;
            var diagnostic = await LaunchDiagnosticsWriter.WriteQuickExitDiagnosticAsync(
                context,
                GetFailureKindText(failureKind),
                process.ExitCode,
                stopwatch.Elapsed,
                createdAt,
                process.StartInfo,
                newCrashFiles,
                stdout,
                stderr,
                cancellationToken);

            return new LaunchCrashMonitorResult(CreateReport(
                context,
                failureKind,
                process.ExitCode,
                diagnostic.DiagnosticPath,
                diagnostic.Analysis,
                diagnostic.FailureSummary));
        }

        public GameLaunchSession CreateGameLaunchSession(Process process, LaunchDiagnosticContext context)
        {
            EnsureOutputReadersStarted(process);
            var exitTask = MonitorProcessExitAsync(process, context);
            return new GameLaunchSession(context.InstanceId, context.InstanceName, exitTask);
        }

        private async Task<LaunchExitResult> MonitorProcessExitAsync(Process process, LaunchDiagnosticContext context)
        {
            await process.WaitForExitAsync(CancellationToken.None);
            var stdout = await stdoutTask!;
            var stderr = await stderrTask!;
            var newCrashFiles = FindNewCrashFiles().ToList();
            var runtime = DateTimeOffset.UtcNow - createdAt;

            if (!IsAbnormalExit(process.ExitCode, newCrashFiles))
                return new LaunchExitResult(null, process.ExitCode, runtime);

            var diagnostic = await LaunchDiagnosticsWriter.WriteQuickExitDiagnosticAsync(
                context,
                "runtime_abnormal_exit",
                process.ExitCode,
                runtime,
                createdAt,
                process.StartInfo,
                newCrashFiles,
                stdout,
                stderr,
                CancellationToken.None);

            var report = CreateReport(
                context,
                LaunchFailureKind.RuntimeAbnormalExit,
                process.ExitCode,
                diagnostic.DiagnosticPath,
                diagnostic.Analysis,
                diagnostic.FailureSummary);
            return new LaunchExitResult(report, process.ExitCode, runtime);
        }

        private IEnumerable<string> FindNewCrashFiles()
        {
            return EnumerateCandidateCrashFiles()
                .Select(Path.GetFullPath)
                .Where(path => !existingCrashFiles.Contains(path)
                    && File.GetLastWriteTimeUtc(path) >= createdAt.UtcDateTime)
                .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
                .ToList();
        }

        private IEnumerable<string> EnumerateCandidateCrashFiles()
        {
            foreach (var root in EnumerateRoots())
            {
                var crashReportsDirectory = Path.Combine(root, "crash-reports");
                if (Directory.Exists(crashReportsDirectory))
                {
                    foreach (var file in Directory.GetFiles(crashReportsDirectory, "*.txt", SearchOption.TopDirectoryOnly))
                        yield return file;
                }

                if (Directory.Exists(root))
                {
                    foreach (var file in Directory.GetFiles(root, "hs_err_pid*.log", SearchOption.TopDirectoryOnly))
                        yield return file;
                }
            }
        }

        private IEnumerable<string> EnumerateRoots()
        {
            var roots = new[] { instanceDirectory, minecraftDirectory }
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var root in roots)
                yield return root;
        }

        private static async Task<string> ReadStreamSafelyAsync(Func<System.IO.StreamReader?> streamFactory)
        {
            try
            {
                var reader = streamFactory();
                if (reader is null)
                    return string.Empty;

                return await reader.ReadToEndAsync();
            }
            catch (InvalidOperationException)
            {
                return string.Empty;
            }
        }

        private void EnsureOutputReadersStarted(Process process)
        {
            stdoutTask ??= ReadStreamSafelyAsync(
                () => process.StartInfo.RedirectStandardOutput ? process.StandardOutput : null);
            stderrTask ??= ReadStreamSafelyAsync(
                () => process.StartInfo.RedirectStandardError ? process.StandardError : null);
        }

        private static bool IsAbnormalExit(int exitCode, IReadOnlyCollection<string> newCrashFiles)
        {
            return exitCode != 0 || newCrashFiles.Count > 0;
        }

        private static LaunchFailureReport CreateReport(
            LaunchDiagnosticContext context,
            LaunchFailureKind kind,
            int? exitCode,
            string? diagnosticPath,
            LaunchFailureAnalysis? analysis,
            string? failureSummary)
        {
            return new LaunchFailureReport(
                kind,
                context.InstanceName,
                context.VersionName,
                exitCode,
                diagnosticPath,
                ResolveDiagnosticDirectory(context, diagnosticPath),
                analysis,
                failureSummary);
        }

        private static string ResolveDiagnosticDirectory(LaunchDiagnosticContext context, string? diagnosticPath)
        {
            if (!string.IsNullOrWhiteSpace(diagnosticPath))
                return Path.GetDirectoryName(diagnosticPath) ?? Path.Combine(context.InstanceDirectory, "logs", "launcher");

            return Path.Combine(context.InstanceDirectory, "logs", "launcher");
        }

        private static string GetFailureKindText(LaunchFailureKind failureKind)
        {
            return failureKind switch
            {
                LaunchFailureKind.StartupProcessExited => "startup_process_exited",
                LaunchFailureKind.StartupAbnormalExit => "startup_abnormal_exit",
                LaunchFailureKind.RuntimeAbnormalExit => "runtime_abnormal_exit",
                _ => "startup_failed"
            };
        }
    }
}
