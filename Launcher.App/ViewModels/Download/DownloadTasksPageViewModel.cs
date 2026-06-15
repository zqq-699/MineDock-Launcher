using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Download;

public sealed partial class DownloadTasksPageViewModel : ObservableObject
{
    private static readonly TimeSpan DefaultCompletedTaskRetention = TimeSpan.FromSeconds(3);
    private readonly TimeSpan completedTaskRetention;
    private readonly Dictionary<string, CancellationTokenSource> removalTokens = [];
    private readonly IUiDispatcher uiDispatcher;

    public DownloadTasksPageViewModel()
        : this(ImmediateUiDispatcher.Instance, null)
    {
    }

    public DownloadTasksPageViewModel(TimeSpan? completedTaskRetention)
        : this(ImmediateUiDispatcher.Instance, completedTaskRetention)
    {
    }

    public DownloadTasksPageViewModel(IUiDispatcher uiDispatcher)
        : this(uiDispatcher, null)
    {
    }

    private DownloadTasksPageViewModel(IUiDispatcher uiDispatcher, TimeSpan? completedTaskRetention)
    {
        this.uiDispatcher = uiDispatcher;
        this.completedTaskRetention = completedTaskRetention ?? DefaultCompletedTaskRetention;
        Tasks.CollectionChanged += Tasks_CollectionChanged;
    }

    public ObservableCollection<DownloadTaskItem> Tasks { get; } = [];

    public bool HasTasks => Tasks.Count > 0;

    public event EventHandler<DownloadTaskItem>? TaskStarted;

    public DownloadTaskItem BeginTask(string title, string subtitle)
    {
        var task = new DownloadTaskItem(title, subtitle);
        task.PropertyChanged += Task_PropertyChanged;
        Tasks.Insert(0, task);
        TaskStarted?.Invoke(this, task);
        return task;
    }

    [RelayCommand]
    public void CancelTask(DownloadTaskItem? task)
    {
        if (task is null)
            return;

        task.Cancel();
        RemoveTask(task, force: true);
    }

    private void Tasks_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (DownloadTaskItem task in e.OldItems)
            {
                CancelScheduledRemoval(task);
                task.PropertyChanged -= Task_PropertyChanged;
            }
        }

        OnPropertyChanged(nameof(HasTasks));
    }

    private void Task_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not DownloadTaskItem task || e.PropertyName != nameof(DownloadTaskItem.State))
            return;

        if (task.State is DownloadTaskState.Completed)
            ScheduleRemoval(task);
        else
            CancelScheduledRemoval(task);
    }

    private void ScheduleRemoval(DownloadTaskItem task)
    {
        CancelScheduledRemoval(task);
        var cancellation = new CancellationTokenSource();
        removalTokens[task.Id] = cancellation;
        _ = RemoveCompletedTaskAfterDelayAsync(task, cancellation.Token);
    }

    private void CancelScheduledRemoval(DownloadTaskItem task)
    {
        if (!removalTokens.Remove(task.Id, out var cancellation))
            return;

        cancellation.Cancel();
        cancellation.Dispose();
    }

    private async Task RemoveCompletedTaskAfterDelayAsync(DownloadTaskItem task, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(completedTaskRetention, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
                return;

            RemoveTask(task, force: false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void RemoveTask(DownloadTaskItem task, bool force)
    {
        if (!uiDispatcher.HasAccess)
        {
            uiDispatcher.Invoke(() => RemoveTask(task, force));
            return;
        }

        if (force || task.State is DownloadTaskState.Completed)
            Tasks.Remove(task);
    }
}

public sealed partial class DownloadTaskItem : ObservableObject
{
    private readonly CancellationTokenSource cancellation = new();

    public DownloadTaskItem(string title, string subtitle)
    {
        Title = title;
        Subtitle = subtitle;
    }

    public string Id { get; } = Guid.NewGuid().ToString("N");

    public string Title { get; }

    public string Subtitle { get; }

    public CancellationToken CancellationToken => cancellation.Token;

    public bool IsCancellationRequested => cancellation.IsCancellationRequested;

    [ObservableProperty]
    private DownloadTaskState state = DownloadTaskState.Running;

    [ObservableProperty]
    private string statusMessage = Strings.DownloadTask_Preparing;

    [ObservableProperty]
    private string downloadSpeedText = string.Empty;

    [ObservableProperty]
    private double progressPercent;

    public string StateText => State switch
    {
        DownloadTaskState.Completed => Strings.DownloadTask_Completed,
        DownloadTaskState.Failed => Strings.DownloadTask_Failed,
        _ => Strings.DownloadTask_Running
    };

    public string ProgressText => $"{Math.Clamp(ProgressPercent, 0, 100):0}%";

    public bool IsRunning => State is DownloadTaskState.Running;

    public bool IsFailed => State is DownloadTaskState.Failed;

    public bool HasDownloadSpeedText => !string.IsNullOrWhiteSpace(DownloadSpeedText);

    public void Report(LauncherProgress progress)
    {
        State = DownloadTaskState.Running;
        StatusMessage = progress.Message;
        if (progress.DownloadSpeedText is not null)
            DownloadSpeedText = progress.DownloadSpeedText;

        if (progress.Percent is { } percent)
            ProgressPercent = Math.Clamp(percent, 0, 100);
    }

    public void Complete(string message)
    {
        State = DownloadTaskState.Completed;
        StatusMessage = message;
        DownloadSpeedText = string.Empty;
        ProgressPercent = 100;
    }

    public void Fail(string message)
    {
        State = DownloadTaskState.Failed;
        StatusMessage = message;
        DownloadSpeedText = string.Empty;
    }

    public void Cancel()
    {
        if (!cancellation.IsCancellationRequested)
            cancellation.Cancel();

        DownloadSpeedText = string.Empty;
    }

    partial void OnStateChanged(DownloadTaskState value)
    {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsFailed));
    }

    partial void OnProgressPercentChanged(double value)
    {
        OnPropertyChanged(nameof(ProgressText));
    }

    partial void OnDownloadSpeedTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasDownloadSpeedText));
    }
}

public enum DownloadTaskState
{
    Running,
    Completed,
    Failed
}

