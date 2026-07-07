using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Launcher.Infrastructure.Updates;

public sealed class LauncherUpdateApplyRunner
{
    private readonly Action<ProcessStartInfo> startProcess;

    public LauncherUpdateApplyRunner()
        : this(startInfo => Process.Start(startInfo)?.Dispose())
    {
    }

    public LauncherUpdateApplyRunner(Action<ProcessStartInfo> startProcess)
    {
        this.startProcess = startProcess;
    }

    public int Run(LauncherUpdateApplyOptions options)
    {
        var logger = new LauncherUpdateApplyLogger(options.LogDirectory);
        try
        {
            logger.Info("Launcher update apply mode started.");
            logger.Info($"Source={options.SourcePath}");
            logger.Info($"Target={options.TargetPath}");

            ValidateSource(options.SourcePath);
            WaitForLauncherExit(options.ProcessId, logger);
            CopyWithRetry(options.SourcePath, options.TargetPath, logger);

            if (options.Restart)
                RestartLauncher(options.TargetPath, logger);

            logger.Info("Launcher update apply mode completed.");
            return 0;
        }
        catch (Exception ex)
        {
            logger.Error("Launcher update apply mode failed.", ex);
            return 1;
        }
    }

    private static void ValidateSource(string sourcePath)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Downloaded update executable was not found.", sourcePath);

        var sourceInfo = new FileInfo(sourcePath);
        if (sourceInfo.Length <= 0)
            throw new InvalidOperationException("Downloaded update executable is empty.");

        if (!Path.GetExtension(sourcePath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Downloaded update file is not an executable.");
    }

    private static void WaitForLauncherExit(int processId, LauncherUpdateApplyLogger logger)
    {
        if (processId <= 0)
            return;

        try
        {
            using var process = Process.GetProcessById(processId);
            logger.Info($"Waiting for launcher process to exit. ProcessId={processId}");
            if (!process.WaitForExit(60_000))
                throw new TimeoutException("Timed out waiting for launcher process to exit.");
        }
        catch (ArgumentException)
        {
            logger.Info("Launcher process has already exited.");
        }
    }

    private static void CopyWithRetry(string sourcePath, string targetPath, LauncherUpdateApplyLogger logger)
    {
        var targetDirectory = Path.GetDirectoryName(targetPath);
        if (string.IsNullOrWhiteSpace(targetDirectory))
            throw new InvalidOperationException("Target executable directory is unavailable.");

        Directory.CreateDirectory(targetDirectory);
        for (var attempt = 1; attempt <= 20; attempt++)
        {
            try
            {
                File.Copy(sourcePath, targetPath, overwrite: true);
                logger.Info($"Launcher executable replaced. Attempt={attempt}");
                return;
            }
            catch (IOException) when (attempt < 20)
            {
                Thread.Sleep(500);
            }
            catch (UnauthorizedAccessException) when (attempt < 20)
            {
                Thread.Sleep(500);
            }
        }

        File.Copy(sourcePath, targetPath, overwrite: true);
    }

    private void RestartLauncher(string targetPath, LauncherUpdateApplyLogger logger)
    {
        logger.Info("Restarting launcher.");
        startProcess(new ProcessStartInfo
        {
            FileName = targetPath,
            UseShellExecute = true
        });
    }
}

public sealed record LauncherUpdateApplyOptions(
    int ProcessId,
    string SourcePath,
    string TargetPath,
    string LogDirectory,
    bool Restart)
{
    public static LauncherUpdateApplyOptions? Parse(string[] args)
    {
        if (!args.Contains("--apply-update", StringComparer.OrdinalIgnoreCase))
            return null;

        int processId = 0;
        string? sourcePath = null;
        string? targetPath = null;
        string? logDirectory = null;
        var restart = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pid" when i + 1 < args.Length && int.TryParse(args[i + 1], out var parsedProcessId):
                    processId = parsedProcessId;
                    i++;
                    break;
                case "--source" when i + 1 < args.Length:
                    sourcePath = args[++i];
                    break;
                case "--target" when i + 1 < args.Length:
                    targetPath = args[++i];
                    break;
                case "--log-dir" when i + 1 < args.Length:
                    logDirectory = args[++i];
                    break;
                case "--restart":
                    restart = true;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(sourcePath)
            || string.IsNullOrWhiteSpace(targetPath)
            || string.IsNullOrWhiteSpace(logDirectory))
        {
            return null;
        }

        return new LauncherUpdateApplyOptions(
            processId,
            Path.GetFullPath(sourcePath),
            Path.GetFullPath(targetPath),
            Path.GetFullPath(logDirectory),
            restart);
    }
}

internal sealed class LauncherUpdateApplyLogger
{
    private readonly string logPath;

    public LauncherUpdateApplyLogger(string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);
        logPath = Path.Combine(logDirectory, $"updater-{DateTime.Now:yyyyMMdd-HHmmss}.log");
    }

    public void Info(string message)
    {
        Write("INF", message);
    }

    public void Error(string message, Exception? exception = null)
    {
        Write("ERR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");
    }

    private void Write(string level, string message)
    {
        File.AppendAllText(
            logPath,
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}{Environment.NewLine}");
    }
}
