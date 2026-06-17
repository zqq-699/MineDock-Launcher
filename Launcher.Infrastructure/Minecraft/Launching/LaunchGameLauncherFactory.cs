using System.Diagnostics;
using CmlLib.Core.ProcessBuilder;
using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Minecraft;

internal interface ILaunchGameLauncher
{
    ValueTask<Process> BuildProcessAsync(string versionName, MLaunchOption launchOption, CancellationToken cancellationToken);
}

internal interface ILaunchGameLauncherFactory
{
    ILaunchGameLauncher Create(string minecraftDirectory, IProgress<LauncherProgress>? progress);
}

internal sealed class LaunchGameLauncherFactory : ILaunchGameLauncherFactory
{
    public ILaunchGameLauncher Create(string minecraftDirectory, IProgress<LauncherProgress>? progress)
    {
        var launcher = VanillaLoaderProvider.CreateLauncher(minecraftDirectory, progress);
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
