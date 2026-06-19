using System.IO;
using Serilog;
using Serilog.Events;

namespace Launcher.App.Logging;

internal static class LauncherLogConfiguration
{
    public const int RetainedDays = 30;
    public const long FileSizeLimitBytes = 20 * 1024 * 1024;
    public const bool RollOnFileSizeLimit = true;
    public const string LogFileNamePattern = "launcher-.log";

    private const string FallbackLauncherName = "Launcher";
    private const string LogDirectoryName = "log";
    private const string LogFileSearchPattern = "launcher*.log";

    public static Serilog.ILogger CreateLogger()
    {
        var logDirectory = ResolveLogDirectory();
        Directory.CreateDirectory(logDirectory);
        PruneOldLogFiles(logDirectory, DateTimeOffset.Now);

        var logPath = Path.Combine(logDirectory, LogFileNamePattern);
        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: null,
                fileSizeLimitBytes: FileSizeLimitBytes,
                rollOnFileSizeLimit: RollOnFileSizeLimit,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    public static string ResolveLogDirectory(string? processPath = null)
    {
        var resolvedProcessPath = string.IsNullOrWhiteSpace(processPath)
            ? Environment.ProcessPath
            : processPath;
        var executableDirectory = Path.GetDirectoryName(resolvedProcessPath);
        if (string.IsNullOrWhiteSpace(executableDirectory))
            executableDirectory = AppContext.BaseDirectory;

        var launcherName = SanitizeLauncherName(Path.GetFileNameWithoutExtension(resolvedProcessPath));
        return Path.Combine(executableDirectory, launcherName, LogDirectoryName);
    }

    public static string SanitizeLauncherName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return FallbackLauncherName;

        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(value
            .Select(character => invalidCharacters.Contains(character) ? '-' : character)
            .ToArray())
            .Trim();

        return string.IsNullOrWhiteSpace(sanitized) ? FallbackLauncherName : sanitized;
    }

    public static void PruneOldLogFiles(string logDirectory, DateTimeOffset now)
    {
        if (!Directory.Exists(logDirectory))
            return;

        var cutoff = now.AddDays(-RetainedDays).UtcDateTime;
        foreach (var path in Directory.EnumerateFiles(logDirectory, LogFileSearchPattern, SearchOption.TopDirectoryOnly))
            DeleteIfExpired(path, cutoff);
    }

    private static void DeleteIfExpired(string path, DateTime cutoff)
    {
        try
        {
            var lastWriteTimeUtc = File.GetLastWriteTimeUtc(path);
            if (lastWriteTimeUtc < cutoff)
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
