/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Launcher.App.Controls;
using Launcher.App.Models;
using Launcher.App.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.Views.Shell;

public partial class MainWindow : Window
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);

    public static readonly DependencyProperty IsMenuExpandedProperty =
        DependencyProperty.Register(nameof(IsMenuExpanded), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

    private readonly NavigationMenuAnimationService navigationMenuService;
    private readonly IAccountDialogService accountDialogService;
    private readonly LauncherStateSyncService stateSyncService;
    private readonly LauncherShutdownService shutdownService;
    private readonly PageTransitionService pageTransitionService;
    private readonly MainViewModel viewModel;
    private readonly ILogger<MainWindow> logger;
    private bool isShutdownInProgress;
    private bool isShutdownComplete;

    public MainWindow(
        MainViewModel viewModel,
        IWindowService windowService,
        IAccountDialogService accountDialogService,
        LauncherStateSyncService stateSyncService,
        LauncherShutdownService shutdownService,
        IThemeService themeService,
        ILogger<MainWindow>? logger = null)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.accountDialogService = accountDialogService;
        this.stateSyncService = stateSyncService;
        this.shutdownService = shutdownService;
        this.logger = logger ?? NullLogger<MainWindow>.Instance;
        navigationMenuService = new NavigationMenuAnimationService(MenuColumn);
        pageTransitionService = new PageTransitionService(Dispatcher, ResolvePageRoot, viewModel.CurrentPage);

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
        AcrylicWindow.Enable(this, themeService);
        NativeCaptionButtons.Hide(this);
        Loaded += MainWindow_Loaded;
        Activated += (_, _) => stateSyncService.RequestSync();
        Closing += Window_OnClosing;
        Closed += (_, _) => stateSyncService.Stop();
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

    private void TitleBarDragArea_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            ToggleWindowMaximizedState();
            e.Handled = true;
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        DragMove();
        e.Handled = true;
    }

    private void ToggleWindowMaximizedState()
    {
        if (ResizeMode is ResizeMode.NoResize or ResizeMode.CanMinimize)
            return;

        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
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

        if (string.Equals(page, "Resources", StringComparison.OrdinalIgnoreCase))
            return ResourcesPageView.RootElement;

        if (string.Equals(page, "Settings", StringComparison.OrdinalIgnoreCase))
            return SettingsPageView.RootElement;

        return GeneralPageView.RootElement;
    }

    private void PrewarmTransientUi()
    {
        accountDialogService.Prewarm();

        foreach (var comboBox in FindVisualChildren<AnimatedComboBox>(this))
            comboBox.ApplyTemplate();
    }

    private async void MainWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        try
        {
            await viewModel.InitializeCommand.ExecuteAsync(null);
            IsMenuExpanded = viewModel.IsMenuExpanded;
            navigationMenuService.SetExpanded(IsMenuExpanded);
            stateSyncService.Start(() => viewModel.Settings, viewModel.SyncCurrentStateAsync);
            _ = Dispatcher.BeginInvoke(PrewarmTransientUi, DispatcherPriority.ContextIdle);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to initialize the main window.");
        }
    }

    private async void Window_OnClosing(object? sender, CancelEventArgs e)
    {
        if (isShutdownComplete)
            return;

        if (!viewModel.CanCloseWindow())
        {
            e.Cancel = true;
            return;
        }

        e.Cancel = true;
        if (isShutdownInProgress)
            return;

        isShutdownInProgress = true;
        try
        {
            await shutdownService.PrepareForExitAsync(ShutdownTimeout);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Unexpected failure while preparing the launcher to exit.");
        }
        finally
        {
            isShutdownInProgress = false;
            isShutdownComplete = true;
            Close();
        }
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

    private void Window_OnPreviewDragEnter(object sender, DragEventArgs e)
    {
        if (HandleDownloadLocalImportPreview(e))
            return;

        if (HandleLocalImportPagePreview(e))
            return;

        HandleFileDropPreview(e);
    }

    private void Window_OnPreviewDragOver(object sender, DragEventArgs e)
    {
        if (HandleDownloadLocalImportPreview(e))
            return;

        if (HandleLocalImportPagePreview(e))
            return;

        HandleFileDropPreview(e);
    }

    private void Window_OnPreviewDragLeave(object sender, DragEventArgs e)
    {
        if (viewModel.DownloadPage.LocalImportDialog.IsOpen)
        {
            if (!IsPointWithinWindow(e.GetPosition(this)))
                viewModel.DownloadPage.LocalImportDialog.ClearDropState();

            return;
        }

        if (IsPointWithinWindow(e.GetPosition(this)))
            return;

        if (IsLocalImportDropPage())
            viewModel.DownloadPage.ClearLocalImportDropState();
        else
            viewModel.GameSettingsPage.ClearImportDropState();
    }

    private async void Window_OnPreviewDrop(object sender, DragEventArgs e)
    {
        try
        {
            if (HandleDownloadLocalImportDrop(e))
                return;

            if (await HandleLocalImportPageDropAsync(e))
                return;

            var paths = TryGetDroppedPaths(e);
            if (paths is null)
                return;

            e.Handled = true;
            e.Effects = DragDropEffects.None;
            await viewModel.GameSettingsPage.HandleImportDropAsync(paths);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to handle files dropped onto the main window.");
        }
    }

    private void HandleFileDropPreview(DragEventArgs e)
    {
        var paths = TryGetDroppedPaths(e);
        if (paths is null)
        {
            viewModel.GameSettingsPage.ClearImportDropState();
            return;
        }

        var canAccept = viewModel.GameSettingsPage.UpdateImportDropState(paths);
        e.Effects = canAccept ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private bool HandleLocalImportPagePreview(DragEventArgs e)
    {
        if (!IsLocalImportDropPage())
            return false;

        var paths = TryGetDroppedPaths(e);
        var canAccept = false;
        if (paths is null)
            viewModel.DownloadPage.ClearLocalImportDropState();
        else
            canAccept = viewModel.DownloadPage.UpdateLocalImportDropState(paths);

        e.Effects = canAccept ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        return true;
    }

    private async Task<bool> HandleLocalImportPageDropAsync(DragEventArgs e)
    {
        if (!IsLocalImportDropPage())
            return false;

        var paths = TryGetDroppedPaths(e);
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        try
        {
            if (paths is not null)
                await viewModel.DownloadPage.HandleLocalImportDropAsync(paths);
        }
        finally
        {
            viewModel.DownloadPage.ClearLocalImportDropState();
        }

        return true;
    }

    private bool HandleDownloadLocalImportPreview(DragEventArgs e)
    {
        if (!viewModel.DownloadPage.LocalImportDialog.IsOpen)
            return false;

        var paths = TryGetDroppedPaths(e);
        if (paths is null)
        {
            viewModel.DownloadPage.LocalImportDialog.ClearDropState();
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return true;
        }

        var canAccept = viewModel.DownloadPage.LocalImportDialog.PreviewDroppedFiles(paths);
        e.Effects = canAccept ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        return true;
    }

    private bool HandleDownloadLocalImportDrop(DragEventArgs e)
    {
        if (!viewModel.DownloadPage.LocalImportDialog.IsOpen)
            return false;

        var paths = TryGetDroppedPaths(e);
        if (paths is not null)
            viewModel.DownloadPage.LocalImportDialog.ApplyDroppedFiles(paths);
        else
            viewModel.DownloadPage.LocalImportDialog.ClearDropState();

        e.Handled = true;
        e.Effects = DragDropEffects.None;
        return true;
    }

    private static string[]? TryGetDroppedPaths(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return null;

        return e.Data.GetData(DataFormats.FileDrop) as string[];
    }

    private bool IsLocalImportDropPage()
    {
        return NavigationCatalog.IsPage(viewModel.CurrentPage, NavigationCatalog.HomePage)
            || NavigationCatalog.IsPage(viewModel.CurrentPage, NavigationCatalog.DownloadPage);
    }

    private bool IsPointWithinWindow(Point point)
    {
        return point.X >= 0
               && point.Y >= 0
               && point.X <= ActualWidth
               && point.Y <= ActualHeight;
    }

}

