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

    [Fact]
    public void StalePackDownloadClearDoesNotHideActiveGameDownloadSpeed()
    {
        var reports = new List<LauncherProgress>();
        var progress = new OverallModpackImportProgress(new InlineProgress(reports));

        progress.Report(new LauncherProgress(
            ImportProgressStages.DownloadingPackFiles,
            string.Empty,
            DownloadSpeedText: "2.0 MB/s"));
        progress.Report(new LauncherProgress(
            LaunchProgressStages.DownloadSpeed,
            string.Empty,
            DownloadSpeedText: "3.0 MB/s"));
        progress.Report(new LauncherProgress(
            ImportProgressStages.DownloadingPackFiles,
            string.Empty,
            DownloadSpeedText: string.Empty));

        Assert.Equal(2, reports.Count);
        Assert.Equal("3.0 MB/s", reports[^1].DownloadSpeedText);

        progress.Report(new LauncherProgress(
            LaunchProgressStages.DownloadSpeed,
            string.Empty,
            DownloadSpeedText: string.Empty));

        Assert.Equal(string.Empty, reports[^1].DownloadSpeedText);
    }

    [Fact]
    public void StaleGameDownloadClearDoesNotHideActivePackDownloadSpeed()
    {
        var reports = new List<LauncherProgress>();
        var progress = new OverallModpackImportProgress(new InlineProgress(reports));

        progress.Report(new LauncherProgress(
            LaunchProgressStages.DownloadSpeed,
            string.Empty,
            DownloadSpeedText: "3.0 MB/s"));
        progress.Report(new LauncherProgress(
            ImportProgressStages.DownloadingPackFiles,
            string.Empty,
            DownloadSpeedText: "2.0 MB/s"));
        progress.Report(new LauncherProgress(
            LaunchProgressStages.DownloadSpeed,
            string.Empty,
            DownloadSpeedText: string.Empty));

        Assert.Equal(2, reports.Count);
        Assert.Equal("2.0 MB/s", reports[^1].DownloadSpeedText);

        progress.Report(new LauncherProgress(
            ImportProgressStages.DownloadingPackFiles,
            string.Empty,
            DownloadSpeedText: string.Empty));

        Assert.Equal(string.Empty, reports[^1].DownloadSpeedText);
    }

    [Fact]
    public void SpeedEventsDoNotChangeWeightedImportProgress()
    {
        var reports = new List<LauncherProgress>();
        var progress = new OverallModpackImportProgress(new InlineProgress(reports));

        progress.Report(new LauncherProgress(ImportProgressStages.InstallingLoader, string.Empty, 50));
        var percentBeforeSpeed = reports[^1].Percent;

        progress.Report(new LauncherProgress(
            ImportProgressStages.DownloadingPackFiles,
            string.Empty,
            DownloadSpeedText: "2.0 MB/s"));

        Assert.Equal(percentBeforeSpeed, reports[^1].Percent);
    }

    private sealed class InlineProgress(List<LauncherProgress> reports) : IProgress<LauncherProgress>
    {
        public void Report(LauncherProgress value) => reports.Add(value);
    }
}
