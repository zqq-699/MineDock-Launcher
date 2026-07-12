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
using Launcher.Application.Services;

namespace Launcher.Infrastructure.Minecraft;

/// <summary>
/// 汇集启动异常、进程输出、日志与崩溃报告，生成经过脱敏和限长处理的诊断文件。
/// </summary>
internal static class LaunchDiagnosticsWriter
{
    // 诊断文件面向排障而非完整归档，所有外部文本进入前都必须脱敏并限制体积。
    private const int MaxDiagnosticLogFiles = 50;
    private const int MaxMatchedErrorLineLength = 800;
    private const string DiagnosticFilePattern = "launch-diagnostics-*.log";
    private const string CapturedOutputFilePattern = "launch-output-*.log";

    public static async Task<LaunchDiagnosticResult> WriteQuickExitDiagnosticAsync(
        LaunchDiagnosticContext context,
        string failureKind,
        int exitCode,
        TimeSpan runtime,
        DateTimeOffset createdAt,
        ProcessStartInfo? startInfo,
        IReadOnlyList<LaunchDiagnosticReference> diagnosticCandidates,
        string stdout,
        string stderr,
        bool capturedOutputTruncated,
        CancellationToken cancellationToken)
    {
        // 快速退出通常没有异常对象，主要依靠退出码、最新日志和崩溃文件推断原因。
        var latestLogPath = diagnosticCandidates
            .FirstOrDefault(candidate => candidate.Type == LaunchDiagnosticType.MinecraftLatestLog)
            ?.Path;
        var latestLogTail = await BoundedDiagnosticFileReader
            .ReadTailAsync(latestLogPath, cancellationToken)
            .ConfigureAwait(false);
        var crashFiles = diagnosticCandidates
            .Where(candidate => candidate.Type is LaunchDiagnosticType.MinecraftCrashReport
                or LaunchDiagnosticType.JvmCrashReport)
            .Select(candidate => candidate.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var crashPreviews = await ReadCrashPreviewsAsync(crashFiles, cancellationToken).ConfigureAwait(false);
        var matchedErrorLines = FindMatchedErrorLines(stdout, stderr, latestLogTail, crashPreviews);
        var failureSummary = matchedErrorLines.FirstOrDefault() ?? $"Minecraft exited with code {exitCode}.";
        var analysis = LaunchFailureAnalyzer.Analyze(
            context,
            stdout,
            stderr,
            latestLogTail,
            crashPreviews.Select(preview => preview.Text).ToArray());

        var diagnosticWrite = await WriteDiagnosticAsync(
            context,
            failureKind,
            failureSummary,
            analysis,
            createdAt,
            diagnosticCandidates,
            cancellationToken,
            builder =>
            {
                builder.AppendLine($"ExitCode: {exitCode}");
                builder.AppendLine($"Runtime: {runtime}");
                builder.AppendLine($"CapturedOutputTruncated: {capturedOutputTruncated}");
                AppendProcessSection(builder, startInfo, context.SensitiveValues);
                AppendFileSection(builder, "NewCrashFiles", crashFiles);
                AppendCrashPreviewSection(builder, crashPreviews, context.SensitiveValues);
                AppendTextSection(builder, "MatchedErrorLines", RedactSensitiveLines(matchedErrorLines, context.SensitiveValues));
                AppendTextSection(builder, "LatestLogTail", RedactSensitiveText(LimitTail(latestLogTail, 120), context.SensitiveValues));
                AppendTextSection(builder, "StdOut", RedactSensitiveText(LimitTail(stdout, 120), context.SensitiveValues));
                AppendTextSection(builder, "StdErr", RedactSensitiveText(LimitTail(stderr, 120), context.SensitiveValues));
            });

        return new LaunchDiagnosticResult(
            diagnosticWrite.Path,
            analysis,
            LaunchDiagnosticRedactor.Redact(failureSummary, context.SensitiveValues),
            diagnosticWrite.Candidates);
    }

    public static async Task<LaunchDiagnosticResult> WriteExceptionDiagnosticAsync(
        LaunchDiagnosticContext context,
        string failureKind,
        string failureSummary,
        Exception exception,
        ProcessStartInfo? startInfo,
        DateTimeOffset createdAt,
        IReadOnlyList<LaunchDiagnosticReference> diagnosticCandidates,
        CancellationToken cancellationToken)
    {
        // 异常链和下载诊断是结构化证据，仍与进程日志合并到同一份用户可分享文件中。
        var latestLogPath = diagnosticCandidates
            .FirstOrDefault(candidate => candidate.Type == LaunchDiagnosticType.MinecraftLatestLog)
            ?.Path;
        var latestLogTail = await BoundedDiagnosticFileReader
            .ReadTailAsync(latestLogPath, cancellationToken)
            .ConfigureAwait(false);
        var crashFiles = diagnosticCandidates
            .Where(candidate => candidate.Type is LaunchDiagnosticType.MinecraftCrashReport
                or LaunchDiagnosticType.JvmCrashReport)
            .Select(candidate => candidate.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var crashPreviews = await ReadCrashPreviewsAsync(crashFiles, cancellationToken).ConfigureAwait(false);
        var matchedErrorLines = FindMatchedErrorLines(string.Empty, string.Empty, latestLogTail, crashPreviews);
        var exceptionText = FormatExceptionChain(exception);
        var analysis = LaunchFailureAnalyzer.Analyze(
            context,
            string.Empty,
            exceptionText,
            latestLogTail,
            crashPreviews.Select(preview => preview.Text).ToArray());

        var diagnosticWrite = await WriteDiagnosticAsync(
            context,
            failureKind,
            failureSummary,
            analysis,
            createdAt,
            diagnosticCandidates,
            cancellationToken,
            builder =>
            {
                AppendProcessSection(builder, startInfo, context.SensitiveValues);
                AppendDownloadSection(builder, FindDownloadDiagnostic(exception), context.SensitiveValues);
                AppendTextSection(builder, "ExceptionChain", RedactSensitiveText(exceptionText, context.SensitiveValues));
                AppendFileSection(builder, "CrashFiles", crashFiles);
                AppendCrashPreviewSection(builder, crashPreviews, context.SensitiveValues);
                AppendTextSection(builder, "MatchedErrorLines", RedactSensitiveLines(matchedErrorLines, context.SensitiveValues));
                AppendTextSection(builder, "LatestLogTail", RedactSensitiveText(LimitTail(latestLogTail, 120), context.SensitiveValues));
            });

        return new LaunchDiagnosticResult(
            diagnosticWrite.Path,
            analysis,
            LaunchDiagnosticRedactor.Redact(failureSummary, context.SensitiveValues),
            diagnosticWrite.Candidates);
    }

    private static async Task<DiagnosticWriteResult> WriteDiagnosticAsync(
        LaunchDiagnosticContext context,
        string failureKind,
        string failureSummary,
        Launcher.Application.Services.LaunchFailureAnalysis? analysis,
        DateTimeOffset createdAt,
        IReadOnlyList<LaunchDiagnosticReference> diagnosticCandidates,
        CancellationToken cancellationToken,
        Action<StringBuilder> appendSections)
    {
        // 写入使用单一入口以统一目录创建、命名、保留数量和失败降级行为。
        var logsDirectory = Path.Combine(context.InstanceDirectory, "logs", "launcher");
        Directory.CreateDirectory(logsDirectory);
        var diagnosticPath = Path.Combine(
            logsDirectory,
            $"launch-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.log");
        var allCandidates = diagnosticCandidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Path))
            .Concat([new LaunchDiagnosticReference(LaunchDiagnosticType.LauncherDiagnostic, diagnosticPath)])
            .ToArray();

        var builder = new StringBuilder();
        builder.AppendLine($"CreatedAtUtc: {createdAt:O}");
        builder.AppendLine($"FailureKind: {failureKind}");
        builder.AppendLine($"FailureSummary: {LaunchDiagnosticRedactor.Redact(failureSummary, context.SensitiveValues)}");
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

        AppendDiagnosticReferences(builder, allCandidates);
        AppendAnalysisSection(builder, analysis, context.SensitiveValues);
        appendSections(builder);

        await File.WriteAllTextAsync(diagnosticPath, builder.ToString(), cancellationToken);
        PruneOldDiagnostics(logsDirectory);
        return new DiagnosticWriteResult(diagnosticPath, allCandidates);
    }

    private static void AppendDiagnosticReferences(
        StringBuilder builder,
        IReadOnlyList<LaunchDiagnosticReference> candidates)
    {
        var primary = candidates.FirstOrDefault();
        builder.AppendLine();
        builder.AppendLine("[PrimaryDiagnostic]");
        builder.AppendLine($"Type: {primary?.Type.ToString() ?? "none"}");
        builder.AppendLine($"Path: {primary?.Path ?? "none"}");

        builder.AppendLine();
        builder.AppendLine("[RelatedDiagnostics]");
        foreach (var type in Enum.GetValues<LaunchDiagnosticType>())
        {
            var matches = candidates.Where(candidate => candidate.Type == type).ToArray();
            if (matches.Length == 0)
            {
                builder.AppendLine($"{type}: none");
                continue;
            }

            foreach (var match in matches)
                builder.AppendLine($"{type}: {match.Path}");
        }
    }

    private static void PruneOldDiagnostics(string logsDirectory)
    {
        PruneOldFiles(logsDirectory, DiagnosticFilePattern);
        PruneOldFiles(logsDirectory, CapturedOutputFilePattern);
    }

    private static void PruneOldFiles(string logsDirectory, string pattern)
    {
        // 只清理启动器自己命名的诊断文件，绝不触碰 Minecraft 或 Mod 生成的其他日志。
        try
        {
            var files = Directory.GetFiles(logsDirectory, pattern, SearchOption.TopDirectoryOnly)
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
        Launcher.Application.Services.LaunchFailureAnalysis? analysis,
        IReadOnlyList<string> sensitiveValues)
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

        for (var index = 0; index < analysis.Details.Count; index++)
        {
            var detail = analysis.Details[index];
            builder.AppendLine();
            builder.AppendLine($"[Analysis.Detail.{index + 1}]");
            builder.AppendLine($"Kind: {detail.Kind}");
            builder.AppendLine($"ModName: {detail.ModName ?? string.Empty}");
            builder.AppendLine($"ModVersion: {detail.ModVersion ?? string.Empty}");
            builder.AppendLine($"DependencyName: {detail.DependencyName ?? string.Empty}");
            builder.AppendLine($"RequiredVersion: {detail.RequiredVersion ?? string.Empty}");
            builder.AppendLine($"CurrentVersion: {detail.CurrentVersion ?? string.Empty}");
            builder.AppendLine($"OriginalReason: {RedactSensitiveText(detail.OriginalReason ?? string.Empty, sensitiveValues)}");
            builder.AppendLine($"OriginalSuggestion: {RedactSensitiveText(detail.OriginalSuggestion ?? string.Empty, sensitiveValues)}");
        }

        for (var index = 0; index < analysis.Evidence.Count; index++)
        {
            var evidence = analysis.Evidence[index];
            builder.AppendLine(
                $"Evidence.{index + 1}: {evidence.Kind}: {RedactSensitiveText(evidence.Text, sensitiveValues)}");
        }
    }

    private static List<string> FindMatchedErrorLines(
        string stdout,
        string stderr,
        string latestLogTail,
        IReadOnlyList<CrashPreview> crashPreviews)
    {
        // 从尾部提取少量高信号错误行，完整日志路径仍写入诊断供进一步查看。
        var candidates = new List<string>();
        AddMatches(stdout);
        AddMatches(stderr);
        AddMatches(latestLogTail);

        foreach (var crashPreview in crashPreviews.Take(2))
            AddMatches(crashPreview.Text);

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList();

        void AddMatches(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (LooksLikeFailureLine(line))
                {
                    candidates.Add(line.Length <= MaxMatchedErrorLineLength
                        ? line
                        : line[..MaxMatchedErrorLineLength] + "…");
                }
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

    private static string RedactSensitiveText(string text, IReadOnlyList<string> sensitiveValues) =>
        LaunchDiagnosticRedactor.Redact(text, sensitiveValues);

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
        IReadOnlyList<CrashPreview> crashPreviews,
        IReadOnlyList<string> sensitiveValues)
    {
        builder.AppendLine();
        builder.AppendLine("[CrashPreview]");
        if (crashPreviews.Count == 0)
        {
            builder.AppendLine("(none)");
            return;
        }

        foreach (var crashPreview in crashPreviews.Take(2))
        {
            builder.AppendLine($"> {crashPreview.Path}");
            var preview = RedactSensitiveText(crashPreview.Text, sensitiveValues);
            builder.AppendLine(string.IsNullOrWhiteSpace(preview) ? "(empty)" : preview);
        }
    }

    private static async Task<IReadOnlyList<CrashPreview>> ReadCrashPreviewsAsync(
        IReadOnlyList<string> crashFiles,
        CancellationToken cancellationToken)
    {
        var previews = new List<CrashPreview>();
        foreach (var path in crashFiles.Take(2))
        {
            var text = await BoundedDiagnosticFileReader
                .ReadHeadAsync(path, cancellationToken)
                .ConfigureAwait(false);
            previews.Add(new CrashPreview(path, text));
        }

        return previews;
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

    private sealed record DiagnosticWriteResult(
        string Path,
        IReadOnlyList<LaunchDiagnosticReference> Candidates);

    private sealed record CrashPreview(string Path, string Text);
}
