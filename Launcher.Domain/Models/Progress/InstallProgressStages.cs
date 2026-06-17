namespace Launcher.Domain.Models;

public static class InstallProgressStages
{
    public const string Queue = "Install.Queue";
    public const string Preparing = "Install.Preparing";
    public const string DownloadingLoaderInstaller = "Install.DownloadingLoaderInstaller";
    public const string RunningLoaderInstaller = "Install.RunningLoaderInstaller";
    public const string FinalizingVersion = "Install.FinalizingVersion";
    public const string CompletingFiles = "Install.CompletingFiles";
}
