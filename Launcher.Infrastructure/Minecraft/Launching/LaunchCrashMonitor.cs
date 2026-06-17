using System.Diagnostics;
using System.IO;

namespace Launcher.Infrastructure.Minecraft;

internal interface ILaunchCrashMonitor
{
    ILaunchCrashMonitorSession CreateSession(string minecraftDirectory, string instanceDirectory, string versionName);
}

internal interface ILaunchCrashMonitorSession
{
    void Configure(Process process);

    Task<LaunchCrashMonitorResult?> WaitForQuickExitAsync(Process process, CancellationToken cancellationToken);
}

internal sealed record LaunchCrashMonitorResult(int ExitCode, string? DiagnosticPath);

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

        public async Task<LaunchCrashMonitorResult?> WaitForQuickExitAsync(Process process, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            var stdoutTask = ReadStreamSafelyAsync(
                () => process.StartInfo.RedirectStandardOutput ? process.StandardOutput : null);
            var stderrTask = ReadStreamSafelyAsync(
                () => process.StartInfo.RedirectStandardError ? process.StandardError : null);
            var exitTask = process.WaitForExitAsync(cancellationToken);
            var delayTask = Task.Delay(quickExitThreshold, cancellationToken);

            if (await Task.WhenAny(exitTask, delayTask) != exitTask)
                return null;

            await exitTask;
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var newCrashFiles = FindNewCrashFiles().ToList();
            var diagnosticPath = await LaunchDiagnosticsWriter.WriteQuickExitDiagnosticAsync(
                minecraftDirectory,
                instanceDirectory,
                versionName,
                process.ExitCode,
                stopwatch.Elapsed,
                createdAt,
                process.StartInfo,
                newCrashFiles,
                stdout,
                stderr,
                cancellationToken);

            return new LaunchCrashMonitorResult(process.ExitCode, diagnosticPath);
        }

        private IEnumerable<string> FindNewCrashFiles()
        {
            return EnumerateCandidateCrashFiles()
                .Select(Path.GetFullPath)
                .Where(path => !existingCrashFiles.Contains(path))
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

    }
}
