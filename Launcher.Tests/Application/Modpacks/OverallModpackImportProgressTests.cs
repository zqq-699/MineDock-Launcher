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
    }

    [Fact]
    public void TelemetryPassesThroughWithoutChangingWeightedImportProgress()
    {
        var reports = new List<LauncherProgress>();
        var progress = new OverallModpackImportProgress(new InlineProgress(reports));
        progress.Report(new LauncherProgress(ImportProgressStages.InstallingLoader, string.Empty, 50));
        var percentBeforeTelemetry = reports[^1].Percent;

        progress.Report(new LauncherProgress(
            string.Empty,
            string.Empty,
            DownloadSpeedTelemetry: new DownloadSpeedTelemetry(2 * 1024 * 1024)));

        Assert.Equal(percentBeforeTelemetry, reports[^2].Percent);
        Assert.Equal(2 * 1024 * 1024, reports[^1].DownloadSpeedTelemetry!.BytesPerSecond);
    }

    private sealed class InlineProgress(List<LauncherProgress> reports) : IProgress<LauncherProgress>
    {
        public void Report(LauncherProgress value) => reports.Add(value);
    }
}
