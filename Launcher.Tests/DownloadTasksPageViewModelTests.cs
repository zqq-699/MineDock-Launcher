using Launcher.App.ViewModels;
using Launcher.Domain.Models;

namespace Launcher.Tests;

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

}
