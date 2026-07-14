using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests.Application.Modpacks;

public sealed class OverallModpackImportProgressTests
{
    [Fact]
    public void ParallelInstallAndPackDownloadsReachNinetyFourBeforeFinalizeStages()
    {
        var reports = new List<LauncherProgress>();
        var progress = new OverallModpackImportProgress(new InlineProgress(reports));

        progress.Report(new LauncherProgress(ImportProgressStages.PreparingArchive, string.Empty));
        progress.Report(new LauncherProgress(ImportProgressStages.ParsingManifest, string.Empty));
        progress.Report(new LauncherProgress(ImportProgressStages.CreatingInstance, string.Empty));
        progress.Report(new LauncherProgress(ImportProgressStages.ResolvingPackFiles, string.Empty, 100));
        progress.Report(new LauncherProgress(ImportProgressStages.InstallingLoader, string.Empty, 100));
        progress.Report(new LauncherProgress(ImportProgressStages.DownloadingPackFiles, string.Empty, 100));

        Assert.Equal(94, reports[^1].Percent);

        progress.Report(new LauncherProgress(ImportProgressStages.CopyingOverrides, string.Empty, 100));
        progress.Report(new LauncherProgress(ImportProgressStages.CleaningUp, string.Empty));

        Assert.Equal(98, reports[^2].Percent);
        Assert.Equal(99, reports[^1].Percent);
    }

    private sealed class InlineProgress(List<LauncherProgress> reports) : IProgress<LauncherProgress>
    {
        public void Report(LauncherProgress value) => reports.Add(value);
    }
}
