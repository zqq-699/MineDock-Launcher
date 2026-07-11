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
using System.Text;
using System.Text.RegularExpressions;
using Launcher.Application.Services;

namespace Launcher.Infrastructure.Minecraft;

/// <summary>
/// 汇集启动异常、进程输出、日志与崩溃报告，生成经过脱敏和限长处理的诊断文件。
/// </summary>
internal static class LaunchDiagnosticsWriter
{
    // 诊断文件面向排障而非完整归档，所有外部文本进入前都必须脱敏并限制体积。
    private const int MaxDiagnosticLogFiles = 50;
    private const string DiagnosticFilePattern = "launch-diagnostics-*.log";

    public static async Task<LaunchDiagnosticResult> WriteQuickExitDiagnosticAsync(
        LaunchDiagnosticContext context,
        string failureKind,
        int exitCode,
        TimeSpan runtime,
        DateTimeOffset createdAt,
        ProcessStartInfo? startInfo,
        IEnumerable<string> newCrashFiles,
        string stdout,
        string stderr,
        CancellationToken cancellationToken)
    {
        // 快速退出通常没有异常对象，主要依靠退出码、最新日志和崩溃文件推断原因。
        var latestLog = ReadLatestLogTail(context.MinecraftDirectory, context.InstanceDirectory, createdAt);
        var latestLogTail = latestLog.Text;
        var crashFiles = newCrashFiles
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var matchedErrorLines = FindMatchedErrorLines(stdout, stderr, latestLogTail, crashFiles);
        var failureSummary = matchedErrorLines.FirstOrDefault() ?? $"Minecraft exited with code {exitCode}.";
        var analysis = LaunchFailureAnalyzer.Analyze(
            context,
            stdout,
            stderr,
            latestLog.IsFresh ? latestLogTail : string.Empty,
            crashFiles);

        var diagnosticPath = await WriteDiagnosticAsync(
            context,
            failureKind,
            failureSummary,
            analysis,
            createdAt,
            cancellationToken,
            builder =>
            {
                builder.AppendLine($"ExitCode: {exitCode}");
                builder.AppendLine($"Runtime: {runtime}");
                AppendProcessSection(builder, startInfo, context.SensitiveValues);
                AppendFileSection(builder, "NewCrashFiles", crashFiles);
                AppendCrashPreviewSection(builder, crashFiles, context.SensitiveValues);
                AppendTextSection(builder, "MatchedErrorLines", RedactSensitiveLines(matchedErrorLines, context.SensitiveValues));
                AppendTextSection(builder, "LatestLogTail", RedactSensitiveText(LimitTail(latestLogTail, 120), context.SensitiveValues));
                AppendTextSection(builder, "StdOut", RedactSensitiveText(LimitTail(stdout, 120), context.SensitiveValues));
                AppendTextSection(builder, "StdErr", RedactSensitiveText(LimitTail(stderr, 120), context.SensitiveValues));
            });

        return new LaunchDiagnosticResult(diagnosticPath, analysis, failureSummary);
    }

    public static async Task<LaunchDiagnosticResult> WriteExceptionDiagnosticAsync(
        LaunchDiagnosticContext context,
        string failureKind,
        string failureSummary,
        Exception exception,
        ProcessStartInfo? startInfo,
        CancellationToken cancellationToken)
    {
        // 异常链和下载诊断是结构化证据，仍与进程日志合并到同一份用户可分享文件中。
        var createdAt = DateTimeOffset.UtcNow;
        var latestLog = ReadLatestLogTail(context.MinecraftDirectory, context.InstanceDirectory, createdAt);
        var latestLogTail = latestLog.Text;
        var crashFiles = EnumerateCandidateCrashFiles(context.MinecraftDirectory, context.InstanceDirectory)
            .Select(Path.GetFullPath)
            .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
            .ToList();
        var matchedErrorLines = FindMatchedErrorLines(string.Empty, string.Empty, latestLogTail, crashFiles);
        var exceptionText = FormatExceptionChain(exception);
        var analysis = LaunchFailureAnalyzer.Analyze(
            context,
            string.Empty,
            exceptionText,
            latestLog.IsFresh ? latestLogTail : string.Empty,
            crashFiles);

        var diagnosticPath = await WriteDiagnosticAsync(
            context,
            failureKind,
            failureSummary,
            analysis,
            createdAt,
            cancellationToken,
            builder =>
            {
                AppendProcessSection(builder, startInfo, context.SensitiveValues);
                AppendDownloadSection(builder, FindDownloadDiagnostic(exception), context.SensitiveValues);
                AppendTextSection(builder, "ExceptionChain", RedactSensitiveText(exceptionText, context.SensitiveValues));
                AppendFileSection(builder, "CrashFiles", crashFiles);
                AppendCrashPreviewSection(builder, crashFiles, context.SensitiveValues);
                AppendTextSection(builder, "MatchedErrorLines", RedactSensitiveLines(matchedErrorLines, context.SensitiveValues));
                AppendTextSection(builder, "LatestLogTail", RedactSensitiveText(LimitTail(latestLogTail, 120), context.SensitiveValues));
            });

        return new LaunchDiagnosticResult(diagnosticPath, analysis, failureSummary);
    }

    private static async Task<string?> WriteDiagnosticAsync(
        LaunchDiagnosticContext context,
        string failureKind,
        string failureSummary,
        Launcher.Application.Services.LaunchFailureAnalysis? analysis,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken,
        Action<StringBuilder> appendSections)
    {
        // 写入使用单一入口以统一目录创建、命名、保留数量和失败降级行为。
        var logsDirectory = Path.Combine(context.InstanceDirectory, "logs", "launcher");
        Directory.CreateDirectory(logsDirectory);
        var diagnosticPath = Path.Combine(
            logsDirectory,
            $"launch-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.log");

        var builder = new StringBuilder();
        builder.AppendLine($"CreatedAtUtc: {createdAt:O}");
        builder.AppendLine($"FailureKind: {failureKind}");
        builder.AppendLine($"FailureSummary: {failureSummary}");
        builder.AppendLine($"InstanceName: {context.InstanceName}");
        builder.AppendLine($"VersionName: {context.VersionName}");
        builder.AppendLine($"MinecraftVersion: {context.MinecraftVersion}");
        builder.AppendLine($"Loader: {context.Loader}");
        builder.AppendLine($"LoaderVersion: {context.LoaderVersion ?? string.Empty}");
        builder.AppendLine($"JavaPath: {context.JavaPath ?? string.Empty}");
        builder.AppendLine($"JavaVersion: {context.JavaVersion ?? string.Empty}");
        builder.AppendLine($"JavaSource: {context.JavaSource ?? string.Empty}");
        builder.AppendLine($"MemoryMb: {context.MemoryMb}");
        builder.AppendLine($"InstanceDirectory: {Path.GetFullPath(context.InstanceDirectory)}");
        builder.AppendLine($"MinecraftDirectory: {Path.GetFullPath(context.MinecraftDirectory)}");

        AppendAnalysisSection(builder, analysis);
        appendSections(builder);

        await File.WriteAllTextAsync(diagnosticPath, builder.ToString(), cancellationToken);
        PruneOldDiagnostics(logsDirectory);
        return diagnosticPath;
    }

    private static void PruneOldDiagnostics(string logsDirectory)
    {
        // 只清理启动器自己命名的诊断文件，绝不触碰 Minecraft 或 Mod 生成的其他日志。
        try
        {
            var files = Directory.GetFiles(logsDirectory, DiagnosticFilePattern, SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenByDescending(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .Skip(MaxDiagnosticLogFiles)
                .ToList();

            foreach (var file in files)
                DeleteDiagnosticSafely(file);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void DeleteDiagnosticSafely(FileInfo file)
    {
        try
        {
            file.Delete();
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void AppendAnalysisSection(
        StringBuilder builder,
        Launcher.Application.Services.LaunchFailureAnalysis? analysis)
    {
        builder.AppendLine();
        builder.AppendLine("[Analysis]");
        if (analysis is null)
        {
            builder.AppendLine("(none)");
            return;
        }

        builder.AppendLine($"Category: {analysis.Category}");
        builder.AppendLine($"ReasonTitle: {analysis.ReasonTitle}");
        builder.AppendLine($"ReasonDetail: {analysis.ReasonDetail}");
        builder.AppendLine($"Recommendation: {analysis.Recommendation}");
        if (analysis.RequiredJavaMajorVersion is int requiredJava)
            builder.AppendLine($"RequiredJavaMajorVersion: {requiredJava}");
        if (analysis.CurrentJavaMajorVersion is int currentJava)
            builder.AppendLine($"CurrentJavaMajorVersion: {currentJava}");
        if (!string.IsNullOrWhiteSpace(analysis.ModName))
            builder.AppendLine($"ModName: {analysis.ModName}");
        if (!string.IsNullOrWhiteSpace(analysis.DependencyName))
            builder.AppendLine($"DependencyName: {analysis.DependencyName}");
        if (!string.IsNullOrWhiteSpace(analysis.MissingPath))
            builder.AppendLine($"MissingPath: {analysis.MissingPath}");
    }

    private sealed record LatestLogTail(string Text, bool IsFresh);

    private static LatestLogTail ReadLatestLogTail(
        string minecraftDirectory,
        string instanceDirectory,
        DateTimeOffset createdAt)
    {
        // 仅采用启动时间附近更新的 latest.log，避免把上一次游戏会话的错误误判为本次原因。
        foreach (var root in EnumerateRoots(minecraftDirectory, instanceDirectory))
        {
            var latestLogPath = Path.Combine(root, "logs", "latest.log");
            if (!File.Exists(latestLogPath))
                continue;

            try
            {
                var lines = File.ReadAllLines(latestLogPath);
                var lastWriteTime = File.GetLastWriteTimeUtc(latestLogPath);
                return new LatestLogTail(
                    string.Join(Environment.NewLine, lines.TakeLast(120)),
                    lastWriteTime >= createdAt.UtcDateTime);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return new LatestLogTail(string.Empty, false);
    }

    private static IEnumerable<string> EnumerateCandidateCrashFiles(string minecraftDirectory, string instanceDirectory)
    {
        // 隔离实例和全局目录都可能产生 crash-reports，枚举根目录时去重但保留最近文件优先级。
        foreach (var root in EnumerateRoots(minecraftDirectory, instanceDirectory))
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

    private static IEnumerable<string> EnumerateRoots(string minecraftDirectory, string instanceDirectory)
    {
        return new[] { instanceDirectory, minecraftDirectory }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static List<string> FindMatchedErrorLines(
        string stdout,
        string stderr,
        string latestLogTail,
        IReadOnlyList<string> crashFiles)
    {
        // 从尾部提取少量高信号错误行，完整日志路径仍写入诊断供进一步查看。
        var candidates = new List<string>();
        AddMatches(stdout);
        AddMatches(stderr);
        AddMatches(latestLogTail);

        foreach (var crashFile in crashFiles.Take(2))
            AddMatches(ReadCrashFilePreview(crashFile));

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList();

        void AddMatches(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (LooksLikeFailureLine(line))
                    candidates.Add(line);
            }
        }
    }

    private static bool LooksLikeFailureLine(string line)
    {
        return line.Contains("error", StringComparison.OrdinalIgnoreCase)
               || line.Contains("exception", StringComparison.OrdinalIgnoreCase)
               || line.Contains("caused by", StringComparison.OrdinalIgnoreCase)
               || line.Contains("missing", StringComparison.OrdinalIgnoreCase)
               || line.Contains("failed", StringComparison.OrdinalIgnoreCase)
               || line.Contains("fatal", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatExceptionChain(Exception exception)
    {
        var builder = new StringBuilder();
        var depth = 0;
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (depth > 0)
                builder.AppendLine();

            builder.AppendLine($"[{depth}] {current.GetType().FullName}");
            builder.AppendLine(current.Message);
            if (!string.IsNullOrWhiteSpace(current.StackTrace))
                builder.AppendLine(current.StackTrace.Trim());
            depth++;
        }

        return builder.ToString();
    }

    private static LaunchDownloadDiagnostic? FindDownloadDiagnostic(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is InstanceRepairException { DownloadDiagnostic: { } diagnostic })
                return diagnostic;
        }

        return null;
    }

    private static void AppendDownloadSection(
        StringBuilder builder,
        LaunchDownloadDiagnostic? diagnostic,
        IReadOnlyList<string> sensitiveValues)
    {
        builder.AppendLine();
        builder.AppendLine("[Download]");
        if (diagnostic is null)
        {
            builder.AppendLine("(none)");
            return;
        }

        builder.AppendLine($"OriginalUrl: {RedactSensitiveText(diagnostic.OriginalUrl, sensitiveValues)}");
        builder.AppendLine($"ActualUrl: {RedactSensitiveText(diagnostic.ActualUrl, sensitiveValues)}");
        builder.AppendLine($"DestinationPath: {diagnostic.DestinationPath}");
        builder.AppendLine($"HttpStatusCode: {diagnostic.HttpStatusCode?.ToString() ?? string.Empty}");
        builder.AppendLine($"LibraryName: {diagnostic.LibraryName ?? string.Empty}");
        builder.AppendLine($"ArtifactPath: {diagnostic.ArtifactPath ?? string.Empty}");
        builder.AppendLine($"RequestedSourcePreference: {diagnostic.RequestedSourcePreference}");
        builder.AppendLine($"ResolvedSourceKind: {diagnostic.ResolvedSourceKind}");
        builder.AppendLine($"ResourceCategory: {diagnostic.ResourceCategory}");
    }

    private static void AppendProcessSection(
        StringBuilder builder,
        ProcessStartInfo? startInfo,
        IReadOnlyList<string> sensitiveValues)
    {
        builder.AppendLine();
        builder.AppendLine("[Process]");
        if (startInfo is null)
        {
            builder.AppendLine("(none)");
            return;
        }

        builder.AppendLine($"FileName: {startInfo.FileName}");
        builder.AppendLine($"Arguments: {RedactSensitiveText(startInfo.Arguments, sensitiveValues)}");
        builder.AppendLine($"WorkingDirectory: {startInfo.WorkingDirectory}");
    }

    private static string RedactSensitiveText(string text, IReadOnlyList<string> sensitiveValues)
    {
        // 先替换已知 token，再处理常见命令行键值，避免访问令牌出现在可分享诊断中。
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var redacted = Regex.Replace(
            text,
            @"(?i)(--?accessToken(?:=|\s+))(""[^""]+""|\S+)",
            "$1<redacted>");
        redacted = Regex.Replace(
            redacted,
            @"(?i)(--?session(?:=|\s+))(""[^""]+""|\S+)",
            "$1<redacted>");
        redacted = Regex.Replace(
            redacted,
            @"(?i)(--?token(?:=|\s+))(""[^""]+""|\S+)",
            "$1<redacted>");

        foreach (var sensitiveValue in sensitiveValues)
        {
            if (string.IsNullOrWhiteSpace(sensitiveValue) || sensitiveValue.Length < 8)
                continue;

            redacted = redacted.Replace(sensitiveValue, "<redacted>", StringComparison.Ordinal);
        }

        return redacted;
    }

    private static IEnumerable<string> RedactSensitiveLines(
        IEnumerable<string> lines,
        IReadOnlyList<string> sensitiveValues)
    {
        foreach (var line in lines)
            yield return RedactSensitiveText(line, sensitiveValues);
    }

    private static string LimitTail(string content, int maxLines)
    {
        // 保留尾部是因为 Java/Minecraft 通常在退出前写出最终异常和根因。
        if (string.IsNullOrWhiteSpace(content))
            return content;

        var lines = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        return string.Join(Environment.NewLine, lines.TakeLast(maxLines));
    }

    private static void AppendCrashPreviewSection(
        StringBuilder builder,
        IReadOnlyList<string> crashFiles,
        IReadOnlyList<string> sensitiveValues)
    {
        builder.AppendLine();
        builder.AppendLine("[CrashPreview]");
        if (crashFiles.Count == 0)
        {
            builder.AppendLine("(none)");
            return;
        }

        foreach (var crashFile in crashFiles.Take(2))
        {
            builder.AppendLine($"> {crashFile}");
            var preview = RedactSensitiveText(ReadCrashFilePreview(crashFile), sensitiveValues);
            builder.AppendLine(string.IsNullOrWhiteSpace(preview) ? "(empty)" : preview);
        }
    }

    private static string ReadCrashFilePreview(string crashFile)
    {
        try
        {
            var lines = File.ReadAllLines(crashFile);
            return string.Join(Environment.NewLine, lines.Take(120));
        }
        catch (IOException)
        {
            return string.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static void AppendFileSection(StringBuilder builder, string title, IEnumerable<string> paths)
    {
        builder.AppendLine();
        builder.AppendLine($"[{title}]");
        var any = false;
        foreach (var path in paths)
        {
            builder.AppendLine(path);
            any = true;
        }

        if (!any)
            builder.AppendLine("(none)");
    }

    private static void AppendTextSection(StringBuilder builder, string title, IEnumerable<string> lines)
    {
        builder.AppendLine();
        builder.AppendLine($"[{title}]");
        var any = false;
        foreach (var line in lines)
        {
            builder.AppendLine(line);
            any = true;
        }

        if (!any)
            builder.AppendLine("(none)");
    }

    private static void AppendTextSection(StringBuilder builder, string title, string content)
    {
        builder.AppendLine();
        builder.AppendLine($"[{title}]");
        builder.AppendLine(string.IsNullOrWhiteSpace(content) ? "(empty)" : content.Trim());
    }
}
