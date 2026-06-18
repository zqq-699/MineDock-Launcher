using System.Diagnostics;
using System.IO;

namespace Launcher.Infrastructure.Minecraft;

internal interface ILaunchCommandRunner
{
    Task RunAsync(string command, string workingDirectory, bool waitForExit, CancellationToken cancellationToken);
}

internal sealed class LaunchCommandRunner : ILaunchCommandRunner
{
    public async Task RunAsync(string command, string workingDirectory, bool waitForExit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command))
            return;

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                Arguments = "/d /s /c " + command,
                WorkingDirectory = ResolveWorkingDirectory(workingDirectory),
                CreateNoWindow = true,
                UseShellExecute = false
            }
        };

        process.Start();
        if (!waitForExit)
            return;

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Launch command exited with code {process.ExitCode}.");
    }

    private static string ResolveWorkingDirectory(string workingDirectory)
    {
        return !string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory)
            ? workingDirectory
            : Environment.CurrentDirectory;
    }
}
