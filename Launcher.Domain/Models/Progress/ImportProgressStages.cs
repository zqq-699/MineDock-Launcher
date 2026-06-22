namespace Launcher.Domain.Models;

public static class ImportProgressStages
{
    public const string PreparingArchive = "Import.PreparingArchive";
    public const string ParsingManifest = "Import.ParsingManifest";
    public const string ResolvingPackFiles = "Import.ResolvingPackFiles";
    public const string CreatingInstance = "Import.CreatingInstance";
    public const string InstallingMinecraftBase = "Import.InstallingMinecraftBase";
    public const string InstallingLoader = "Import.InstallingLoader";
    public const string DownloadingPackFiles = "Import.DownloadingPackFiles";
    public const string CopyingOverrides = "Import.CopyingOverrides";
    public const string CleaningUp = "Import.CleaningUp";
}
