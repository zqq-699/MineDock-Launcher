using System.Diagnostics;
using CmlLib.Core.ProcessBuilder;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Minecraft;

internal interface ILaunchGameLauncher
{
    ValueTask<Process> BuildProcessAsync(string versionName, MLaunchOption launchOption, CancellationToken cancellationToken);
}

internal interface ILaunchGameLauncherFactory
{
    ILaunchGameLauncher Create(
        string minecraftDirectory,
        IProgress<LauncherProgress>? progress,
        int downloadSpeedLimitMbPerSecond = 0);
}

internal sealed class LaunchGameLauncherFactory : ILaunchGameLauncherFactory
{
    private readonly IDownloadSpeedLimitState? downloadSpeedLimitState;

    public LaunchGameLauncherFactory(IDownloadSpeedLimitState? downloadSpeedLimitState = null)
    {
        this.downloadSpeedLimitState = downloadSpeedLimitState;
    }

    public ILaunchGameLauncher Create(
        string minecraftDirectory,
        IProgress<LauncherProgress>? progress,
        int downloadSpeedLimitMbPerSecond = 0)
    {
        var launcher = VanillaLoaderProvider.CreateLauncher(
            minecraftDirectory,
            progress,
            downloadSpeedLimitMbPerSecond: downloadSpeedLimitMbPerSecond,
            downloadSpeedLimitState: downloadSpeedLimitState);
        VanillaLoaderProvider.AttachProgress(launcher, progress);
        return new CmlLaunchGameLauncher(launcher);
    }

    private sealed class CmlLaunchGameLauncher : ILaunchGameLauncher
    {
        private readonly CmlLib.Core.MinecraftLauncher launcher;

        public CmlLaunchGameLauncher(CmlLib.Core.MinecraftLauncher launcher)
        {
            this.launcher = launcher;
        }

        public ValueTask<Process> BuildProcessAsync(
            string versionName,
            MLaunchOption launchOption,
            CancellationToken cancellationToken)
        {
            return launcher.BuildProcessAsync(versionName, launchOption, cancellationToken);
        }
    }
}
