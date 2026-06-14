using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Launcher.App.Controls;
using Launcher.App.Services;
using Launcher.App.ViewModels;

namespace Launcher.App;

public partial class MainWindow : Window
{
    public static readonly DependencyProperty IsMenuExpandedProperty =
        DependencyProperty.Register(nameof(IsMenuExpanded), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

    private readonly NavigationMenuAnimationService navigationMenuService;
    private readonly IAccountDialogService accountDialogService;
    private readonly PageTransitionService pageTransitionService;
    private readonly DispatcherTimer stateSyncDebounceTimer;
    private readonly MainViewModel viewModel;
    private int stateSyncDispatchQueued;
    private int downloadTaskPulseDispatchQueued;
    private bool isDownloadTaskPulseRunning;
    private DateTimeOffset lastDownloadTaskPulseAt = DateTimeOffset.MinValue;
    private FileSystemWatcher? minecraftParentWatcher;
    private FileSystemWatcher? minecraftVersionsWatcher;
    private FileSystemWatcher? dataDirectoryWatcher;

    public MainWindow(
        MainViewModel viewModel,
        IWindowService windowService,
        IAccountDialogService accountDialogService)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.accountDialogService = accountDialogService;
        navigationMenuService = new NavigationMenuAnimationService(MenuColumn);
        pageTransitionService = new PageTransitionService(Dispatcher, ResolvePageRoot, viewModel.CurrentPage);
        stateSyncDebounceTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        stateSyncDebounceTimer.Tick += StateSyncDebounceTimer_Tick;

        DataContext = viewModel;
        windowService.Attach(this);
        accountDialogService.Attach(
            viewModel.AccountPage,
            this,
            WindowContentLayer,
            AddAccountDialogHost,
            DeleteAccountDialogHost,
            RenameAccountDialogHost);

        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        viewModel.DownloadTasksPage.TaskStarted += DownloadTasksPage_TaskStarted;
        AcrylicWindow.Enable(this);
        SizeChanged += (_, _) => accountDialogService.QueueOpenDialogBlurRefresh();
        Loaded += async (_, _) =>
        {
            await viewModel.InitializeCommand.ExecuteAsync(null);
            IsMenuExpanded = viewModel.IsMenuExpanded;
            navigationMenuService.SetExpanded(IsMenuExpanded);
            ConfigureStateWatchers();
            _ = Dispatcher.BeginInvoke(PrewarmTransientUi, DispatcherPriority.ContextIdle);
        };
        Activated += (_, _) => QueueStateSync();
        Closed += (_, _) =>
        {
            viewModel.DownloadTasksPage.TaskStarted -= DownloadTasksPage_TaskStarted;
            DisposeStateWatchers();
        };
    }

    public bool IsMenuExpanded
    {
        get => (bool)GetValue(IsMenuExpandedProperty);
        set => SetValue(IsMenuExpandedProperty, value);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MainViewModel viewModel)
            return;

        if (e.PropertyName == nameof(MainViewModel.CurrentPage))
        {
            pageTransitionService.MoveTo(viewModel.CurrentPage);
            return;
        }

        if (e.PropertyName == nameof(MainViewModel.IsMenuExpanded))
        {
            IsMenuExpanded = viewModel.IsMenuExpanded;
            navigationMenuService.AnimateExpanded(IsMenuExpanded);
        }
    }

    private FrameworkElement? ResolvePageRoot(string page)
    {
        if (string.Equals(page, "Home", StringComparison.OrdinalIgnoreCase))
            return HomePageView.RootElement;

        if (string.Equals(page, "Account", StringComparison.OrdinalIgnoreCase))
            return AccountPageView.RootElement;

        if (string.Equals(page, "Download", StringComparison.OrdinalIgnoreCase))
            return DownloadPageView.RootElement;

        if (string.Equals(page, "Install", StringComparison.OrdinalIgnoreCase))
            return InstallPageView.RootElement;

        return GeneralPageView.RootElement;
    }

    private void PrewarmTransientUi()
    {
        BlurEffectWarmup.EnsureWarmed();
        accountDialogService.Prewarm();

        foreach (var comboBox in FindVisualChildren<AnimatedComboBox>(this))
            comboBox.ApplyTemplate();
    }

    private async void StateSyncDebounceTimer_Tick(object? sender, EventArgs e)
    {
        stateSyncDebounceTimer.Stop();
        Interlocked.Exchange(ref stateSyncDispatchQueued, 0);
        await viewModel.SyncCurrentStateAsync();
        ConfigureStateWatchers();
    }

    private void QueueStateSync()
    {
        if (!IsLoaded)
            return;

        stateSyncDebounceTimer.Stop();
        stateSyncDebounceTimer.Start();
    }

    private void WatcherStateChanged(object sender, FileSystemEventArgs e)
    {
        QueueStateSyncFromWatcher();
    }

    private void WatcherStateRenamed(object sender, RenamedEventArgs e)
    {
        QueueStateSyncFromWatcher();
    }

    private void QueueStateSyncFromWatcher()
    {
        if (Interlocked.Exchange(ref stateSyncDispatchQueued, 1) == 1)
            return;

        Dispatcher.BeginInvoke(QueueStateSync, DispatcherPriority.Background);
    }

    private void DownloadTasksPage_TaskStarted(object? sender, DownloadTaskItem e)
    {
        if (Interlocked.Exchange(ref downloadTaskPulseDispatchQueued, 1) == 1)
            return;

        Dispatcher.BeginInvoke(TryRunDownloadTaskPulse, DispatcherPriority.Render);
    }

    private void TryRunDownloadTaskPulse()
    {
        Interlocked.Exchange(ref downloadTaskPulseDispatchQueued, 0);

        if (isDownloadTaskPulseRunning)
            return;

        var now = DateTimeOffset.UtcNow;
        if (now - lastDownloadTaskPulseAt < TimeSpan.FromMilliseconds(950))
            return;

        lastDownloadTaskPulseAt = now;
        RunDownloadTaskPulse();
    }

    private void RunDownloadTaskPulse()
    {
        const double initialOpacity = 1;
        isDownloadTaskPulseRunning = true;
        var duration = TimeSpan.FromMilliseconds(850);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

        DownloadTaskPulseCircle.BeginAnimation(UIElement.OpacityProperty, null);
        DownloadTaskPulseScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        DownloadTaskPulseScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        DownloadTaskPulseCircle.Opacity = initialOpacity;
        DownloadTaskPulseScale.ScaleX = 0.9;
        DownloadTaskPulseScale.ScaleY = 0.9;

        var opacityAnimation = new DoubleAnimation(initialOpacity, 0, duration)
        {
            EasingFunction = easing,
            FillBehavior = FillBehavior.Stop
        };
        opacityAnimation.Completed += (_, _) =>
        {
            DownloadTaskPulseCircle.BeginAnimation(UIElement.OpacityProperty, null);
            DownloadTaskPulseScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            DownloadTaskPulseScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            DownloadTaskPulseCircle.Opacity = 0;
            DownloadTaskPulseScale.ScaleX = 1;
            DownloadTaskPulseScale.ScaleY = 1;
            isDownloadTaskPulseRunning = false;
        };

        DownloadTaskPulseCircle.BeginAnimation(
            UIElement.OpacityProperty,
            opacityAnimation,
            HandoffBehavior.SnapshotAndReplace);

        var scaleAnimation = new DoubleAnimation(0.9, 1, duration)
        {
            EasingFunction = easing,
            FillBehavior = FillBehavior.Stop
        };
        DownloadTaskPulseScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation, HandoffBehavior.SnapshotAndReplace);
        DownloadTaskPulseScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation.Clone(), HandoffBehavior.SnapshotAndReplace);
    }

    private void ConfigureStateWatchers()
    {
        DisposeStateWatchers();

        var minecraftDirectory = viewModel.Settings.MinecraftDirectory;
        var minecraftParent = Path.GetDirectoryName(minecraftDirectory);
        var minecraftFolderName = Path.GetFileName(minecraftDirectory);
        if (!string.IsNullOrWhiteSpace(minecraftParent) && Directory.Exists(minecraftParent))
            minecraftParentWatcher = CreateWatcher(minecraftParent, minecraftFolderName, includeSubdirectories: false);

        var versionsDirectory = Path.Combine(minecraftDirectory, "versions");
        if (Directory.Exists(versionsDirectory))
            minecraftVersionsWatcher = CreateWatcher(versionsDirectory, "*", includeSubdirectories: true);

        var dataDirectory = viewModel.Settings.DataDirectory;
        if (Directory.Exists(dataDirectory))
            dataDirectoryWatcher = CreateWatcher(dataDirectory, "instances.json", includeSubdirectories: false);
    }

    private FileSystemWatcher CreateWatcher(string path, string filter, bool includeSubdirectories)
    {
        var watcher = new FileSystemWatcher(path, filter)
        {
            IncludeSubdirectories = includeSubdirectories,
            NotifyFilter = NotifyFilters.DirectoryName
                | NotifyFilters.FileName
                | NotifyFilters.LastWrite
                | NotifyFilters.Size
        };

        watcher.Changed += WatcherStateChanged;
        watcher.Created += WatcherStateChanged;
        watcher.Deleted += WatcherStateChanged;
        watcher.Renamed += WatcherStateRenamed;
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    private void DisposeStateWatchers()
    {
        minecraftParentWatcher?.Dispose();
        minecraftVersionsWatcher?.Dispose();
        dataDirectoryWatcher?.Dispose();
        minecraftParentWatcher = null;
        minecraftVersionsWatcher = null;
        dataDirectoryWatcher = null;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typedChild)
                yield return typedChild;

            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }
}
