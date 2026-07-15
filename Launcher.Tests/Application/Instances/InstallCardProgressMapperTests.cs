using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests.Application.Instances;

public sealed class InstallCardProgressMapperTests
{
    [Fact]
    public void VanillaFilesOccupyThePrimaryInstallRangeAndCommitStopsAtNinetyNine()
    {
        var reports = new List<LauncherProgress>();
        var mapper = new InstallCardProgressMapper(new InlineProgress(reports), LoaderKind.Vanilla, hasOptionalContent: false);

        mapper.Report(new LauncherProgress(InstallProgressStages.Preparing, string.Empty));
        mapper.Report(new LauncherProgress(LaunchProgressStages.DownloadingFiles, string.Empty, 50));
        mapper.ReportBaseInstallCompleted();
        mapper.ReportCommitCompleted();

        Assert.Equal(4, reports[0].Percent);
        Assert.Equal(50, reports[1].Percent);
        Assert.Equal(96, reports[2].Percent);
        Assert.Equal(99, reports[3].Percent);
    }

    [Fact]
    public void ForgeReservesEarlyStagesForInstallerAndNeverRegresses()
    {
        var reports = new List<LauncherProgress>();
        var mapper = new InstallCardProgressMapper(new InlineProgress(reports), LoaderKind.Forge, hasOptionalContent: false);

        mapper.Report(new LauncherProgress(InstallProgressStages.DownloadingLoaderInstaller, string.Empty));
        mapper.Report(new LauncherProgress(InstallProgressStages.CheckingJava, string.Empty));
        mapper.Report(new LauncherProgress(InstallProgressStages.RunningLoaderInstaller, string.Empty));
        mapper.Report(new LauncherProgress(InstallProgressStages.FinalizingVersion, string.Empty));
        mapper.Report(new LauncherProgress(LaunchProgressStages.CheckingFiles, string.Empty, 50));
        mapper.Report(new LauncherProgress(LaunchProgressStages.CheckingFiles, string.Empty, 10));

        Assert.Equal(12, reports[0].Percent);
        Assert.Equal(26, reports[1].Percent);
        Assert.Equal(30, reports[2].Percent);
        Assert.Equal(38, reports[3].Percent);
        Assert.Equal(67, reports[4].Percent);
        Assert.Equal(67, reports[5].Percent);
    }

    [Fact]
    public void OptionalContentKeepsSpaceBeforeCommit()
    {
        var reports = new List<LauncherProgress>();
        var mapper = new InstallCardProgressMapper(new InlineProgress(reports), LoaderKind.Fabric, hasOptionalContent: true);

        mapper.Report(new LauncherProgress(LaunchProgressStages.DownloadingFiles, string.Empty, 100));
        mapper.ReportBaseInstallCompleted();
        mapper.Report(new LauncherProgress(ModProgressStages.DownloadingFile, "Fabric API"));
        mapper.ReportOptionalContentCompleted();

        Assert.Equal(86, reports[0].Percent);
        Assert.Equal(86, reports[1].Percent);
        Assert.Equal(86, reports[2].Percent);
        Assert.Equal(96, reports[3].Percent);
    }

    private sealed class InlineProgress(List<LauncherProgress> reports) : IProgress<LauncherProgress>
    {
        public void Report(LauncherProgress value) => reports.Add(value);
    }
}
