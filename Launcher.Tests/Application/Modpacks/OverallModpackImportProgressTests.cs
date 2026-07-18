using System.Collections.Concurrent;
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
        progress.Report(new LauncherProgress(ImportProgressStages.ProcessingPackFiles, string.Empty, 100));

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

    [Fact]
    public void JavaCheckBetweenInstallerDownloadAndExecutionNeverRegressesOverallProgress()
    {
        var reports = new List<LauncherProgress>();
        var progress = new OverallModpackImportProgress(new InlineProgress(reports));

        progress.Report(new LauncherProgress(InstallProgressStages.DownloadingLoaderInstaller, string.Empty, 100));
        progress.Report(new LauncherProgress(InstallProgressStages.CheckingJava, string.Empty));
        progress.Report(new LauncherProgress(InstallProgressStages.DownloadingJava, string.Empty, 50));
        progress.Report(new LauncherProgress(InstallProgressStages.RunningLoaderInstaller, string.Empty));

        Assert.Equal(reports.Select(report => report.Percent).Order(), reports.Select(report => report.Percent));
    }

    [Fact]
    public void CompletedPackBranchRestoresActiveLoaderCheckingStatus()
    {
        var reports = new List<LauncherProgress>();
        var progress = new OverallModpackImportProgress(new InlineProgress(reports));
        var packFiles = progress.CreateParallelBranch(ModpackImportProgressBranch.PackFiles);
        var loader = progress.CreateParallelBranch(ModpackImportProgressBranch.LoaderInstall);

        loader.Report(new LauncherProgress(LaunchProgressStages.CheckingFiles, "checking", 40));
        packFiles.Report(new LauncherProgress(ImportProgressStages.DownloadingPackFiles, "mod.jar", 90));
        packFiles.Report(new LauncherProgress(ImportProgressStages.ProcessingPackFiles, string.Empty, 100));
        progress.CompleteParallelBranch(ModpackImportProgressBranch.PackFiles);

        Assert.Equal(LaunchProgressStages.CheckingFiles, reports[^1].Stage);
        Assert.Equal("checking", reports[^1].Message);
        Assert.Equal(reports.Select(report => report.Percent).Order(), reports.Select(report => report.Percent));
    }

    [Fact]
    public void CompletedLoaderBranchRestoresActivePackStatus()
    {
        var reports = new List<LauncherProgress>();
        var progress = new OverallModpackImportProgress(new InlineProgress(reports));
        var packFiles = progress.CreateParallelBranch(ModpackImportProgressBranch.PackFiles);
        var loader = progress.CreateParallelBranch(ModpackImportProgressBranch.LoaderInstall);

        packFiles.Report(new LauncherProgress(ImportProgressStages.ResolvingPackFiles, "3/10", 30));
        loader.Report(new LauncherProgress(InstallProgressStages.CompletingFiles, string.Empty, 96));
        progress.CompleteParallelBranch(ModpackImportProgressBranch.LoaderInstall);

        Assert.Equal(ImportProgressStages.ResolvingPackFiles, reports[^1].Stage);
        Assert.Equal("3/10", reports[^1].Message);
        Assert.Equal(reports.Select(report => report.Percent).Order(), reports.Select(report => report.Percent));
    }

    [Fact]
    public void ConcurrentParallelBranchReportsKeepOverallPercentMonotonic()
    {
        var reports = new ConcurrentQueue<LauncherProgress>();
        var progress = new OverallModpackImportProgress(new ConcurrentProgress(reports));
        var packFiles = progress.CreateParallelBranch(ModpackImportProgressBranch.PackFiles);
        var loader = progress.CreateParallelBranch(ModpackImportProgressBranch.LoaderInstall);

        Parallel.Invoke(
            () =>
            {
                for (var percent = 0; percent <= 100; percent++)
                {
                    packFiles.Report(new LauncherProgress(
                        ImportProgressStages.ProcessingPackFiles,
                        string.Empty,
                        percent));
                }
            },
            () =>
            {
                for (var percent = 0; percent <= 100; percent++)
                {
                    loader.Report(new LauncherProgress(
                        LaunchProgressStages.CheckingFiles,
                        string.Empty,
                        percent));
                }
            });

        var percentages = reports.Select(report => report.Percent).ToArray();
        Assert.Equal(percentages.Order(), percentages);
    }

    private sealed class InlineProgress(List<LauncherProgress> reports) : IProgress<LauncherProgress>
    {
        public void Report(LauncherProgress value) => reports.Add(value);
    }

    private sealed class ConcurrentProgress(ConcurrentQueue<LauncherProgress> reports) : IProgress<LauncherProgress>
    {
        public void Report(LauncherProgress value) => reports.Enqueue(value);
    }
}
