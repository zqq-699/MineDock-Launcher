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
