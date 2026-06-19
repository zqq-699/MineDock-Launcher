using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Launcher.App.ViewModels.Settings;

namespace Launcher.App.Views.Settings;

public partial class SettingsPageView : UserControl
{
    private readonly DispatcherTimer memoryRefreshTimer = new()
    {
        Interval = TimeSpan.FromSeconds(1)
    };

    public SettingsPageView()
    {
        InitializeComponent();
        memoryRefreshTimer.Tick += MemoryRefreshTimer_Tick;
        Loaded += SettingsPageView_Loaded;
        Unloaded += SettingsPageView_Unloaded;
    }

    public FrameworkElement RootElement => PageRoot;

    private void SettingsPageView_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshMemorySnapshot();
        memoryRefreshTimer.Start();
    }

    private void SettingsPageView_Unloaded(object sender, RoutedEventArgs e)
    {
        memoryRefreshTimer.Stop();
    }

    private void MemoryRefreshTimer_Tick(object? sender, EventArgs e)
    {
        RefreshMemorySnapshot();
    }

    private void RefreshMemorySnapshot()
    {
        if (DataContext is SettingsPageViewModel viewModel)
            viewModel.RefreshSystemMemorySnapshot();
    }
}
