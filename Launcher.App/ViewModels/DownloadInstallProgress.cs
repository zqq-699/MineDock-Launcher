using System.Windows.Threading;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels;

internal sealed class DownloadInstallProgress : IProgress<LauncherProgress>, IDisposable
{
    private static readonly TimeSpan UiUpdateInterval = TimeSpan.FromMilliseconds(120);

    private readonly object syncRoot = new();
    private readonly DownloadTaskItem installTask;
    private readonly long installSequence;
    private readonly Action<DownloadTaskItem, LauncherProgress, long> reportProgress;
    private LauncherProgress? pendingProgress;
    private DateTimeOffset lastFlushedAt = DateTimeOffset.MinValue;
    private bool isFlushQueued;
    private bool isDisposed;

    public DownloadInstallProgress(
        DownloadTaskItem installTask,
        long installSequence,
        Action<DownloadTaskItem, LauncherProgress, long> reportProgress)
    {
        this.installTask = installTask;
        this.installSequence = installSequence;
        this.reportProgress = reportProgress;
    }

    public void Report(LauncherProgress value)
    {
        TimeSpan delay;
        lock (syncRoot)
        {
            if (isDisposed)
                return;

            pendingProgress = value;
            if (isFlushQueued)
                return;

            var elapsed = DateTimeOffset.UtcNow - lastFlushedAt;
            delay = elapsed >= UiUpdateInterval
                ? TimeSpan.Zero
                : UiUpdateInterval - elapsed;
            isFlushQueued = true;
        }

        QueueFlush(delay);
    }

    public void Dispose()
    {
        lock (syncRoot)
        {
            isDisposed = true;
            pendingProgress = null;
            isFlushQueued = false;
        }
    }

    private void QueueFlush(TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero)
        {
            PostFlush();
            return;
        }

        _ = FlushAfterDelayAsync(delay);
    }

    private async Task FlushAfterDelayAsync(TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay);
            PostFlush();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void PostFlush()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(Flush, DispatcherPriority.Background);
            return;
        }

        Flush();
    }

    private void Flush()
    {
        LauncherProgress? progress;
        lock (syncRoot)
        {
            if (isDisposed)
                return;

            progress = pendingProgress;
            pendingProgress = null;
            lastFlushedAt = DateTimeOffset.UtcNow;
            isFlushQueued = false;
        }

        if (progress is not null)
            reportProgress(installTask, progress, installSequence);
    }
}
