using System.Diagnostics;
using System.IO;
using System.Text;

namespace Launcher.Infrastructure.Minecraft;

internal static class LaunchDiagnosticsWriter
{
    public static async Task<string?> WriteQuickExitDiagnosticAsync(
        string minecraftDirectory,
        string instanceDirectory,
        string versionName,
        int exitCode,
        TimeSpan runtime,
        DateTimeOffset createdAt,
        ProcessStartInfo? startInfo,
        IEnumerable<string> newCrashFiles,
        string stdout,
        string stderr,
        CancellationToken cancellationToken)
    {
        var latestLogTail = ReadLatestLogTail(minecraftDirectory, instanceDirectory);
        var crashFiles = newCrashFiles
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var matchedErrorLines = FindMatchedErrorLines(stdout, stderr, latestLogTail, crashFiles);
        var failureSummary = matchedErrorLines.FirstOrDefault() ?? $"Minecraft exited with code {exitCode}.";

        return await WriteDiagnosticAsync(
            minecraftDirectory,
            instanceDirectory,
            versionName,
            "quick_exit",
            failureSummary,
            createdAt,
            cancellationToken,
            builder =>
            {
                builder.AppendLine($"ExitCode: {exitCode}");
                builder.AppendLine($"Runtime: {runtime}");
                AppendProcessSection(builder, startInfo);
                AppendFileSection(builder, "NewCrashFiles", crashFiles);
                AppendCrashPreviewSection(builder, crashFiles);
                AppendTextSection(builder, "MatchedErrorLines", matchedErrorLines);
                AppendTextSection(builder, "LatestLogTail", latestLogTail);
                AppendTextSection(builder, "StdOut", stdout);
                AppendTextSection(builder, "StdErr", stderr);
            });
    }

    public static Task<string?> WriteExceptionDiagnosticAsync(
        string minecraftDirectory,
        string instanceDirectory,
        string versionName,
        string failureKind,
        string failureSummary,
        string? javaPath,
        Exception exception,
        ProcessStartInfo? startInfo,
        CancellationToken cancellationToken)
    {
        var latestLogTail = ReadLatestLogTail(minecraftDirectory, instanceDirectory);
        var crashFiles = EnumerateCandidateCrashFiles(minecraftDirectory, instanceDirectory)
            .Select(Path.GetFullPath)
            .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
            .ToList();
        var matchedErrorLines = FindMatchedErrorLines(string.Empty, string.Empty, latestLogTail, crashFiles);

        return WriteDiagnosticAsync(
            minecraftDirectory,
            instanceDirectory,
            versionName,
            failureKind,
            failureSummary,
            DateTimeOffset.UtcNow,
            cancellationToken,
            builder =>
            {
                builder.AppendLine($"JavaPath: {javaPath ?? string.Empty}");
                AppendProcessSection(builder, startInfo);
                AppendTextSection(builder, "ExceptionChain", FormatExceptionChain(exception));
                AppendFileSection(builder, "CrashFiles", crashFiles);
                AppendCrashPreviewSection(builder, crashFiles);
                AppendTextSection(builder, "MatchedErrorLines", matchedErrorLines);
                AppendTextSection(builder, "LatestLogTail", latestLogTail);
            });
    }

    private static async Task<string?> WriteDiagnosticAsync(
        string minecraftDirectory,
        string instanceDirectory,
        string versionName,
        string failureKind,
        string failureSummary,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken,
        Action<StringBuilder> appendSections)
    {
        var logsDirectory = Path.Combine(instanceDirectory, "logs", "launcher");
        Directory.CreateDirectory(logsDirectory);
        var diagnosticPath = Path.Combine(
            logsDirectory,
            $"launch-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.log");

        var builder = new StringBuilder();
        builder.AppendLine($"Version: {versionName}");
        builder.AppendLine($"CreatedAtUtc: {createdAt:O}");
        builder.AppendLine($"FailureKind: {failureKind}");
        builder.AppendLine($"FailureSummary: {failureSummary}");
        builder.AppendLine($"InstanceDirectory: {Path.GetFullPath(instanceDirectory)}");
        builder.AppendLine($"MinecraftDirectory: {Path.GetFullPath(minecraftDirectory)}");

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

    private static void AppendProcessSection(StringBuilder builder, ProcessStartInfo? startInfo)
    {
        builder.AppendLine();
        builder.AppendLine("[Process]");
        if (startInfo is null)
        {
            builder.AppendLine("(none)");
            return;
        }

        builder.AppendLine($"FileName: {startInfo.FileName}");
        builder.AppendLine($"Arguments: {startInfo.Arguments}");
        builder.AppendLine($"WorkingDirectory: {startInfo.WorkingDirectory}");
    }

    private static void AppendCrashPreviewSection(StringBuilder builder, IReadOnlyList<string> crashFiles)
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
            var preview = ReadCrashFilePreview(crashFile);
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
