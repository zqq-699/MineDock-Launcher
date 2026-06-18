using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Launcher.Infrastructure.Minecraft;

internal static class LaunchDiagnosticsWriter
{
    public static async Task<string?> WriteQuickExitDiagnosticAsync(
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
        var latestLogTail = ReadLatestLogTail(context.MinecraftDirectory, context.InstanceDirectory);
        var crashFiles = newCrashFiles
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var matchedErrorLines = FindMatchedErrorLines(stdout, stderr, latestLogTail, crashFiles);
        var failureSummary = matchedErrorLines.FirstOrDefault() ?? $"Minecraft exited with code {exitCode}.";

        return await WriteDiagnosticAsync(
            context,
            failureKind,
            failureSummary,
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
    }

    public static Task<string?> WriteExceptionDiagnosticAsync(
        LaunchDiagnosticContext context,
        string failureKind,
        string failureSummary,
        Exception exception,
        ProcessStartInfo? startInfo,
        CancellationToken cancellationToken)
    {
        var latestLogTail = ReadLatestLogTail(context.MinecraftDirectory, context.InstanceDirectory);
        var crashFiles = EnumerateCandidateCrashFiles(context.MinecraftDirectory, context.InstanceDirectory)
            .Select(Path.GetFullPath)
            .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
            .ToList();
        var matchedErrorLines = FindMatchedErrorLines(string.Empty, string.Empty, latestLogTail, crashFiles);

        return WriteDiagnosticAsync(
            context,
            failureKind,
            failureSummary,
            DateTimeOffset.UtcNow,
            cancellationToken,
            builder =>
            {
                AppendProcessSection(builder, startInfo, context.SensitiveValues);
                AppendTextSection(builder, "ExceptionChain", RedactSensitiveText(FormatExceptionChain(exception), context.SensitiveValues));
                AppendFileSection(builder, "CrashFiles", crashFiles);
                AppendCrashPreviewSection(builder, crashFiles, context.SensitiveValues);
                AppendTextSection(builder, "MatchedErrorLines", RedactSensitiveLines(matchedErrorLines, context.SensitiveValues));
                AppendTextSection(builder, "LatestLogTail", RedactSensitiveText(LimitTail(latestLogTail, 120), context.SensitiveValues));
            });
    }

    private static async Task<string?> WriteDiagnosticAsync(
        LaunchDiagnosticContext context,
        string failureKind,
        string failureSummary,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken,
        Action<StringBuilder> appendSections)
    {
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
        builder.AppendLine($"MemoryMb: {context.MemoryMb}");
        builder.AppendLine($"InstanceDirectory: {Path.GetFullPath(context.InstanceDirectory)}");
        builder.AppendLine($"MinecraftDirectory: {Path.GetFullPath(context.MinecraftDirectory)}");

        appendSections(builder);

        await File.WriteAllTextAsync(diagnosticPath, builder.ToString(), cancellationToken);
        return diagnosticPath;
    }

    private static string ReadLatestLogTail(string minecraftDirectory, string instanceDirectory)
    {
        foreach (var root in EnumerateRoots(minecraftDirectory, instanceDirectory))
        {
            var latestLogPath = Path.Combine(root, "logs", "latest.log");
            if (!File.Exists(latestLogPath))
                continue;

            try
            {
                var lines = File.ReadAllLines(latestLogPath);
                return string.Join(Environment.NewLine, lines.TakeLast(120));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return string.Empty;
    }

    private static IEnumerable<string> EnumerateCandidateCrashFiles(string minecraftDirectory, string instanceDirectory)
    {
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
