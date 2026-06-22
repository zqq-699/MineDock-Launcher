using Launcher.Domain.Models;

namespace Launcher.Tests.Download;

public sealed class DownloadTasksPageViewModelTests
{
    [Fact]
    public void DownloadTasksPageStartsEmpty()
    {
        var viewModel = new DownloadTasksPageViewModel();

        Assert.False(viewModel.HasTasks);
        Assert.Empty(viewModel.Tasks);
    }

    [Fact]
    public void DownloadTasksPageRaisesTaskStartedWhenTaskBegins()
    {
        var viewModel = new DownloadTasksPageViewModel();
        DownloadTaskItem? startedTask = null;

        viewModel.TaskStarted += (_, task) => startedTask = task;

        var task = viewModel.BeginTask("Vanilla 1.21.5", "1.21.5");

        Assert.Same(task, startedTask);
    }

    [Fact]
    public void DownloadTasksPageCancelTaskCancelsAndRemovesTask()
    {
        var viewModel = new DownloadTasksPageViewModel();
        var task = viewModel.BeginTask("Vanilla 1.21.5", "1.21.5");

        viewModel.CancelTaskCommand.Execute(task);

        Assert.True(task.IsCancellationRequested);
        Assert.Empty(viewModel.Tasks);
        Assert.False(viewModel.HasTasks);
    }

    [Fact]
    public async Task DownloadTasksPageRemovesCompletedTasksAfterRetention()
    {
        var viewModel = new DownloadTasksPageViewModel(TimeSpan.FromMilliseconds(10));
        var task = viewModel.BeginTask("鍘熺増 1.21.5", "1.21.5");

        task.Complete("瀹夎瀹屾垚");

        await TestAsync.WaitForAsync(() => viewModel.Tasks.Count == 0);
        Assert.False(viewModel.HasTasks);
    }

    [Fact]
    public async Task DownloadTasksPageKeepsFailedTasks()
    {
        var viewModel = new DownloadTasksPageViewModel(TimeSpan.FromMilliseconds(10));
        var task = viewModel.BeginTask("鍘熺増 1.21.5", "1.21.5");

        task.Fail("瀹夎澶辫触");
        await Task.Delay(50);

        Assert.Single(viewModel.Tasks);
        Assert.True(viewModel.HasTasks);
    }

    [Fact]
    public void DownloadTaskShowsAndClearsDownloadSpeed()
    {
        var task = new DownloadTaskItem("\u539f\u7248 1.21.5", "1.21.5");

        task.Report(new LauncherProgress("Bytes", "\u6b63\u5728\u4e0b\u8f7d\u6e38\u620f\u6587\u4ef6", 42, "1.2 MB/s"));

        Assert.True(task.HasDownloadSpeedText);
        Assert.Equal("1.2 MB/s", task.DownloadSpeedText);

        task.Complete("\u5b89\u88c5\u5b8c\u6210");

        Assert.False(task.HasDownloadSpeedText);
        Assert.Empty(task.DownloadSpeedText);
    }

    [Fact]
    public void DownloadTaskDoesNotRevertFromFailedStateWhenLateProgressArrives()
    {
        var task = new DownloadTaskItem("CurseForge Pack", "pack.zip");

        task.Report(new LauncherProgress(ImportProgressStages.DownloadingPackFiles, "downloading", 42));
        task.Fail("missing key");
        task.Report(new LauncherProgress(ImportProgressStages.CleaningUp, "cleaning", 99));

        Assert.Equal(DownloadTaskState.Failed, task.State);
        Assert.Equal("missing key", task.StatusMessage);
        Assert.Equal(42, task.ProgressPercent);
    }

    [Fact]
    public void DownloadTaskDoesNotRevertFromCompletedStateWhenLateProgressArrives()
    {
        var task = new DownloadTaskItem("Modrinth Pack", "pack.mrpack");

        task.Report(new LauncherProgress(ImportProgressStages.DownloadingPackFiles, "downloading", 64));
        task.Complete("done");
        task.Report(new LauncherProgress(ImportProgressStages.CleaningUp, "cleaning", 10));

        Assert.Equal(DownloadTaskState.Completed, task.State);
        Assert.Equal("done", task.StatusMessage);
        Assert.Equal(100, task.ProgressPercent);
    }

}


