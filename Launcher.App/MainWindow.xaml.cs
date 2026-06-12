using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Launcher.App.Animations;
using Launcher.App.Models;
using Launcher.App.Services;
using Launcher.App.ViewModels;

namespace Launcher.App;

public partial class MainWindow : Window
{
    public static readonly DependencyProperty IsMenuExpandedProperty =
        DependencyProperty.Register(nameof(IsMenuExpanded), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

    private const double CollapsedMenuWidth = 62;
    private const double ExpandedMenuWidth = 210;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        AcrylicWindow.Enable(this);
        Loaded += async (_, _) =>
        {
            await viewModel.InitializeCommand.ExecuteAsync(null);
            IsMenuExpanded = viewModel.IsMenuExpanded;
            MenuColumn.Width = new GridLength(IsMenuExpanded ? ExpandedMenuWidth : CollapsedMenuWidth);
        };
    }

    public bool IsMenuExpanded
    {
        get => (bool)GetValue(IsMenuExpandedProperty);
        set => SetValue(IsMenuExpandedProperty, value);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleMenu_Click(object sender, RoutedEventArgs e)
    {
        var nextState = !IsMenuExpanded;
        IsMenuExpanded = nextState;

        if (DataContext is MainViewModel viewModel)
            viewModel.IsMenuExpanded = nextState;

        var animation = new GridLengthAnimation
        {
            From = new GridLength(MenuColumn.ActualWidth),
            To = new GridLength(nextState ? ExpandedMenuWidth : CollapsedMenuWidth),
            Duration = TimeSpan.FromMilliseconds(360),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };

        animation.Completed += (_, _) =>
        {
            MenuColumn.Width = new GridLength(nextState ? ExpandedMenuWidth : CollapsedMenuWidth);
        };

        MenuColumn.BeginAnimation(ColumnDefinition.WidthProperty, animation);
    }

    private void Navigation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NavigationItem item }
            && DataContext is MainViewModel viewModel)
            viewModel.SelectNavigationItem(item);
    }

    private void Account_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LauncherAccount account }
            && DataContext is MainViewModel viewModel)
            viewModel.SelectAccount(account);
    }
}
