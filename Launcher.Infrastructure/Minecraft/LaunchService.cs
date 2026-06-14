using System.IO;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Minecraft;

public sealed class LaunchService : ILaunchService
{
    public async Task LaunchAsync(GameInstance instance, LauncherSettings settings, IProgress<LauncherProgress>? progress, CancellationToken cancellationToken = default)
    {
        progress?.Report(new LauncherProgress("Launch", $"正在检查 {instance.Name}"));
        var launcher = VanillaLoaderProvider.CreateLauncher(settings.MinecraftDirectory, progress);
        VanillaLoaderProvider.AttachProgress(launcher, progress);

        var versionName = string.IsNullOrWhiteSpace(instance.VersionName) ? instance.MinecraftVersion : instance.VersionName;
        await launcher.InstallAsync(versionName, cancellationToken);
        var isolatedPath = CreateIsolatedLaunchPath(settings.MinecraftDirectory, versionName);

        var javaPath = string.IsNullOrWhiteSpace(instance.JavaPath) ? settings.DefaultJavaPath : instance.JavaPath;
        var launchOption = new MLaunchOption
        {
            Path = isolatedPath,
            Session = MSession.CreateOfflineSession(settings.OfflineUsername),
            MaximumRamMb = instance.MemoryMb > 0 ? instance.MemoryMb : settings.DefaultMemoryMb,
            ScreenWidth = instance.WindowWidth,
            ScreenHeight = instance.WindowHeight,
            GameLauncherName = "Launcher",
            GameLauncherVersion = "0.1"
        };

        if (!string.IsNullOrWhiteSpace(javaPath))
            launchOption.JavaPath = javaPath;

        if (!string.IsNullOrWhiteSpace(instance.JvmArguments))
            launchOption.ExtraJvmArguments = [MArgument.FromCommandLine(instance.JvmArguments)];

        var process = await launcher.BuildProcessAsync(versionName, launchOption, cancellationToken);
        progress?.Report(new LauncherProgress("Launch", "游戏进程已启动"));
        process.Start();
    }

    private static MinecraftPath CreateIsolatedLaunchPath(string minecraftDirectory, string versionName)
    {
        var sharedPath = new MinecraftPath(minecraftDirectory);
        var versionDirectory = Path.Combine(sharedPath.Versions, versionName);
        var isolatedPath = new MinecraftPath(versionDirectory)
        {
            Versions = sharedPath.Versions,
            Library = sharedPath.Library,
            Assets = sharedPath.Assets,
            Runtime = sharedPath.Runtime
        };

        return isolatedPath;
    }
}
