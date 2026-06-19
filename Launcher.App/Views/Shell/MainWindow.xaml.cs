using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Launcher.App.Controls;
using Launcher.App.Services;
using Launcher.Application.Services;

namespace Launcher.App.Views.Shell;

public partial class MainWindow : Window
{
    public static readonly DependencyProperty IsMenuExpandedProperty =
        DependencyProperty.Register(nameof(IsMenuExpanded), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

    private readonly NavigationMenuAnimationService navigationMenuService;
    private readonly IAccountDialogService accountDialogService;
    private readonly ILauncherStateMonitor launcherStateMonitor;
    private readonly PageTransitionService pageTransitionService;
    private readonly DispatcherTimer stateSyncDebounceTimer;
    private readonly MainViewModel viewModel;
    private int stateSyncDispatchQueued;

    public MainWindow(
        MainViewModel viewModel,
        IWindowService windowService,
        IAccountDialogService accountDialogService,
        ILauncherStateMonitor launcherStateMonitor)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.accountDialogService = accountDialogService;
        this.launcherStateMonitor = launcherStateMonitor;
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
            AddAccountDialogHost,
            DeleteAccountDialogHost,
            RenameAccountDialogHost,
            SkinModelDialogHost,
            SkinManagerDialogHost);

        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        launcherStateMonitor.StateChanged += LauncherStateMonitor_StateChanged;
        AcrylicWindow.Enable(this);
        SizeChanged += (_, _) => accountDialogService.QueueOpenDialogBlurRefresh();
        Loaded += async (_, _) =>
        {
            await viewModel.InitializeCommand.ExecuteAsync(null);
            IsMenuExpanded = viewModel.IsMenuExpanded;
            navigationMenuService.SetExpanded(IsMenuExpanded);
            launcherStateMonitor.Watch(viewModel.Settings);
            _ = Dispatcher.BeginInvoke(PrewarmTransientUi, DispatcherPriority.ContextIdle);
        };
        Activated += (_, _) => QueueStateSync();
        Closed += (_, _) =>
        {
            launcherStateMonitor.StateChanged -= LauncherStateMonitor_StateChanged;
            launcherStateMonitor.Stop();
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

        if (string.Equals(page, "GameSettings", StringComparison.OrdinalIgnoreCase))
            return GameSettingsPageView.RootElement;

        if (string.Equals(page, "Settings", StringComparison.OrdinalIgnoreCase))
            return SettingsPageView.RootElement;

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
        launcherStateMonitor.Watch(viewModel.Settings);
    }

    private void QueueStateSync()
    {
        if (!IsLoaded)
            return;

        stateSyncDebounceTimer.Stop();
        stateSyncDebounceTimer.Start();
    }

    private void LauncherStateMonitor_StateChanged(object? sender, EventArgs e)
    {
        QueueStateSyncFromWatcher();
    }

    private void QueueStateSyncFromWatcher()
    {
        if (Interlocked.Exchange(ref stateSyncDispatchQueued, 1) == 1)
            return;

        Dispatcher.BeginInvoke(QueueStateSync, DispatcherPriority.Background);
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

