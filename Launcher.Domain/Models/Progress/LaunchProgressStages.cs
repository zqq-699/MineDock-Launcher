namespace Launcher.Domain.Models;

public static class LaunchProgressStages
{
    public const string CheckingInstance = "Launch.CheckingInstance";
    public const string RepairingMetadata = "Launch.RepairingMetadata";
    public const string RepairingJar = "Launch.RepairingJar";
    public const string RepairingLibraries = "Launch.RepairingLibraries";
    public const string RepairingAssets = "Launch.RepairingAssets";
    public const string RepairingLogging = "Launch.RepairingLogging";
    public const string CheckingJava = "Launch.CheckingJava";
    public const string RunningPreLaunchCommand = "Launch.RunningPreLaunchCommand";
    public const string PreparingProcess = "Launch.PreparingProcess";
    public const string StartingProcess = "Launch.StartingProcess";
    public const string CheckingFiles = "Files";
    public const string DownloadingFiles = "Bytes";
    public const string DownloadSpeed = "DownloadSpeed";
}
