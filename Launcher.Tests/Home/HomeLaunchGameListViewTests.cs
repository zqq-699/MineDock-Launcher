using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Launcher.App.Behaviors;
using Launcher.App.Controls;
using Launcher.App.Converters;
using Launcher.App.Services;
using Launcher.App.ViewModels.Home;
using Launcher.App.Views.Home;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Tests.Helpers;

namespace Launcher.Tests.Home;

[Collection(WpfTestCollection.Name)]
public sealed class HomeLaunchGameListViewTests
{
    [Fact]
    public void HomeLaunchGameListViewStartsCollapsedAndKeepsSelectedItemVisible()
    {
        RunOnSta(() =>
        {
            Window? window = null;
            try
            {
                var application = WpfApplicationTestHelper.GetOrCreateApplication();
                EnsureApplicationResources(application);
                var viewModel = CreateViewModelWithInstances();
                var view = new HomeLaunchGameListView
                {
                    DataContext = viewModel,
                    Width = 900,
                    Height = 700
                };
                window = CreateHiddenWindow(view);

                window.Show();
                PumpDispatcher(DispatcherPriority.ApplicationIdle);

                Assert.False(view.IsMenuExpanded);
                Assert.Equal(view.CollapsedMenuHeight, view.MenuPanelShadowElement.Height, 1);
                Assert.Equal(VerticalAlignment.Bottom, view.MenuPanelShadowElement.VerticalAlignment);
                Assert.Equal(24d, view.MenuPanelShadowElement.Margin.Left);
                Assert.True(view.ListTranslateTransform.Y < -10);
                Assert.Equal(ScrollBarVisibility.Auto, ScrollViewer.GetVerticalScrollBarVisibility(view.LaunchInstanceListBox));
                Assert.True(VirtualizingPanel.GetIsVirtualizing(view.LaunchInstanceListBox));
                Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(view.LaunchInstanceListBox));
                Assert.True(view.IsSelectedItemBackgroundSuppressed);

                var selectedButton = FindVisualDescendant<Button>(
                    view,
                    button => ReferenceEquals(button.DataContext, viewModel.SelectedLaunchInstanceItem));
                Assert.NotNull(selectedButton);
                Assert.True(SecondaryMenuButtonBehavior.GetSuppressSelectedBackground(selectedButton));
                var selectedBackground = Assert.IsType<Border>(
                    selectedButton.Template.FindName("SelectedBackground", selectedButton));
                Assert.Equal(0d, selectedBackground.Opacity);
                var selectedTop = selectedButton
                    .TransformToAncestor(view.MenuPanelShadowElement)
                    .Transform(new Point(0, 0))
                    .Y;
                var expectedSlotTop = (view.CollapsedMenuHeight - selectedButton.ActualHeight) / 2;
                Assert.InRange(selectedTop, expectedSlotTop - 3, expectedSlotTop + 3);
            }
            finally
            {
                window?.Close();
            }
        });
    }

    [Fact]
    public void HomeLaunchGameListViewExpandsOnHoverAndCollapsesOnLeave()
    {
        RunOnSta(() =>
        {
            Window? window = null;
            try
            {
                var application = WpfApplicationTestHelper.GetOrCreateApplication();
                EnsureApplicationResources(application);
                var viewModel = CreateViewModelWithInstances();
                var view = new HomeLaunchGameListView
                {
                    DataContext = viewModel,
                    Width = 900,
                    Height = 700
                };
                window = CreateHiddenWindow(view);

                window.Show();
                PumpDispatcher(DispatcherPriority.ApplicationIdle);

                view.SetPointerExpandedForTest(true);
                PumpAnimation();

                Assert.True(view.IsMenuExpanded);
                Assert.True(view.MenuPanelShadowElement.Height > view.CollapsedMenuHeight);
                Assert.Equal(0d, view.ListTranslateTransform.Y, 1);
                Assert.Equal(1d, view.HeaderOverlayElement.Opacity, 1);
                Assert.False(view.IsSelectedItemBackgroundSuppressed);

                view.SetPointerExpandedForTest(false);
                PumpAnimation();

                Assert.False(view.IsMenuExpanded);
                Assert.Equal(view.CollapsedMenuHeight, view.MenuPanelShadowElement.Height, 1);
                Assert.True(view.ListTranslateTransform.Y < -10);
                Assert.Equal(0d, view.HeaderOverlayElement.Opacity, 1);
                Assert.False(view.HeaderOverlayElement.IsHitTestVisible);
                Assert.True(view.IsSelectedItemBackgroundSuppressed);
            }
            finally
            {
                window?.Close();
            }
        });
    }

    [Fact]
    public void HomeLaunchGameListViewPinToggleKeepsMenuExpandedUntilUnpinned()
    {
        RunOnSta(() =>
        {
            Window? window = null;
            try
            {
                var application = WpfApplicationTestHelper.GetOrCreateApplication();
                EnsureApplicationResources(application);
                var viewModel = CreateViewModelWithInstances();
                var view = new HomeLaunchGameListView
                {
                    DataContext = viewModel,
                    Width = 900,
                    Height = 700
                };
                window = CreateHiddenWindow(view);

                window.Show();
                PumpDispatcher(DispatcherPriority.ApplicationIdle);

                Assert.False(view.PinButtonElement.IsChecked.GetValueOrDefault());
                var pinIcon = Assert.IsType<SvgIcon>(FindVisualDescendant<SvgIcon>(view.PinButtonElement));
                Assert.False(pinIcon.ForceFill);
                Assert.False(view.HeaderOverlayElement.IsHitTestVisible);

                view.SetPointerExpandedForTest(true);
                PumpAnimation();

                Assert.True(view.HeaderOverlayElement.IsHitTestVisible);
                view.PinButtonElement.Command.Execute(null);
                PumpAnimation();

                Assert.True(viewModel.IsLaunchMenuPinned);
                Assert.True(view.PinButtonElement.IsChecked.GetValueOrDefault());
                Assert.True(pinIcon.ForceFill);

                view.SetPointerExpandedForTest(false);
                PumpAnimation();

                Assert.True(view.IsMenuExpanded);
                Assert.True(view.MenuPanelShadowElement.Height > view.CollapsedMenuHeight);
                Assert.False(view.IsSelectedItemBackgroundSuppressed);

                view.PinButtonElement.Command.Execute(null);
                PumpAnimation();

                Assert.False(viewModel.IsLaunchMenuPinned);
                Assert.False(view.PinButtonElement.IsChecked.GetValueOrDefault());
                Assert.False(pinIcon.ForceFill);
                Assert.False(view.IsMenuExpanded);
                Assert.Equal(view.CollapsedMenuHeight, view.MenuPanelShadowElement.Height, 1);
                Assert.True(view.IsSelectedItemBackgroundSuppressed);
            }
            finally
            {
                window?.Close();
            }
        });
    }

    [Fact]
    public void HomeLaunchGameListViewReversesCollapseAnimationFromCurrentAnimatedValue()
    {
        RunOnSta(() =>
        {
            Window? window = null;
            try
            {
                var application = WpfApplicationTestHelper.GetOrCreateApplication();
                EnsureApplicationResources(application);
                var viewModel = CreateViewModelWithInstances();
                var view = new HomeLaunchGameListView
                {
                    DataContext = viewModel,
                    Width = 900,
                    Height = 700
                };
                view.Resources["HomeLaunchMenuAnimationDurationMilliseconds"] = 1000d;
                window = CreateHiddenWindow(view);

                window.Show();
                PumpDispatcher(DispatcherPriority.ApplicationIdle);

                var collapsedHeight = view.MenuPanelShadowElement.Height;
                var collapsedTranslate = view.ListTranslateTransform.Y;

                view.SetPointerExpandedForTest(true);
                PumpDispatcher(DispatcherPriority.Background);
                Thread.Sleep(180);
                PumpDispatcher(DispatcherPriority.Background);

                var midExpandHeight = view.MenuPanelShadowElement.Height;
                var midExpandTranslate = view.ListTranslateTransform.Y;
                Assert.InRange(midExpandHeight, collapsedHeight + 1, view.MenuViewportElement.Height - 1);
                Assert.InRange(midExpandTranslate, collapsedTranslate + 1, -1);

                view.SetPointerExpandedForTest(false);
                PumpDispatcher(DispatcherPriority.Background);

                Assert.InRange(view.MenuPanelShadowElement.Height, collapsedHeight + 1, view.MenuViewportElement.Height - 1);
                Assert.InRange(view.ListTranslateTransform.Y, collapsedTranslate + 1, -1);
                Assert.True(Math.Abs(view.MenuPanelShadowElement.Height - midExpandHeight) < 32);
                Assert.True(Math.Abs(view.ListTranslateTransform.Y - midExpandTranslate) < 32);
            }
            finally
            {
                window?.Close();
            }
        });
    }

    [Fact]
    public void HomeLaunchGameListViewKeepsEmptyStateCollapsedWhenThereAreNoInstancesAndMenuIsUnpinned()
    {
        RunOnSta(() =>
        {
            Window? window = null;
            try
            {
                var application = WpfApplicationTestHelper.GetOrCreateApplication();
                EnsureApplicationResources(application);
                var viewModel = CreateViewModel();
                viewModel.SetLaunchInstances([]);
                var view = new HomeLaunchGameListView
                {
                    DataContext = viewModel,
                    Width = 900,
                    Height = 700
                };
                window = CreateHiddenWindow(view);

                window.Show();
                PumpDispatcher(DispatcherPriority.ApplicationIdle);

                Assert.False(view.IsMenuExpanded);
                Assert.Equal(view.CollapsedMenuHeight, view.MenuPanelShadowElement.Height, 1);
                Assert.Equal(0d, view.ListTranslateTransform.Y, 1);
                Assert.Equal(Visibility.Visible, view.EmptyStateTextElement.Visibility);
                Assert.Equal(0d, view.HeaderOverlayElement.Opacity, 1);
                Assert.False(view.HeaderOverlayElement.IsHitTestVisible);

                var emptyStateTop = view.EmptyStateTextElement
                    .TransformToAncestor(view.MenuPanelShadowElement)
                    .Transform(new Point(0, 0))
                    .Y;
                var expectedSlotTop = (view.CollapsedMenuHeight - view.EmptyStateTextElement.ActualHeight) / 2;
                Assert.InRange(emptyStateTop, expectedSlotTop - 3, expectedSlotTop + 3);

                view.SetPointerExpandedForTest(true);
                PumpAnimation();

                Assert.False(view.IsMenuExpanded);
                Assert.Equal(view.CollapsedMenuHeight, view.MenuPanelShadowElement.Height, 1);
                Assert.Equal(0d, view.ListTranslateTransform.Y, 1);
                Assert.Equal(0d, view.HeaderOverlayElement.Opacity, 1);
            }
            finally
            {
                window?.Close();
            }
        });
    }

    [Fact]
    public void HomeLaunchGameListViewKeepsEmptyStateExpandedWhenMenuIsPinned()
    {
        RunOnSta(() =>
        {
            Window? window = null;
            try
            {
                var application = WpfApplicationTestHelper.GetOrCreateApplication();
                EnsureApplicationResources(application);
                var viewModel = CreateViewModel();
                viewModel.SetLaunchInstances([]);
                viewModel.SetLaunchMenuPinned(true);
                var view = new HomeLaunchGameListView
                {
                    DataContext = viewModel,
                    Width = 900,
                    Height = 700
                };
                window = CreateHiddenWindow(view);

                window.Show();
                PumpDispatcher(DispatcherPriority.ApplicationIdle);

                Assert.True(view.IsMenuExpanded);
                Assert.True(view.MenuPanelShadowElement.Height > view.CollapsedMenuHeight);
                Assert.Equal(0d, view.ListTranslateTransform.Y, 1);
                Assert.Equal(Visibility.Visible, view.EmptyStateTextElement.Visibility);
                Assert.True(view.EmptyStateTranslateTransform.Y > view.CollapsedMenuHeight);
                Assert.Equal(1d, view.HeaderOverlayElement.Opacity, 1);
                Assert.True(view.HeaderOverlayElement.IsHitTestVisible);
                Assert.True(view.PinButtonElement.IsChecked.GetValueOrDefault());
            }
            finally
            {
                window?.Close();
            }
        });
    }

    [Fact]
    public void HomePageViewPlacesLaunchMenuAsFloatingOverlay()
    {
        RunOnSta(() =>
        {
            Window? window = null;
            try
            {
                var application = WpfApplicationTestHelper.GetOrCreateApplication();
                EnsureApplicationResources(application);
                var viewModel = CreateViewModelWithInstances();
                var view = new HomePageView
                {
                    DataContext = new HomePageTestContext(viewModel),
                    Width = 900,
                    Height = 700
                };
                window = CreateHiddenWindow(view);

                window.Show();
                PumpDispatcher(DispatcherPriority.ApplicationIdle);

                Assert.Empty(Assert.IsType<Grid>(view.RootElement).ColumnDefinitions);
                var menuView = FindVisualDescendant<HomeLaunchGameListView>(view);
                var panelView = FindVisualDescendant<HomeLaunchPanelView>(view);
                Assert.NotNull(menuView);
                Assert.NotNull(panelView);
                Assert.True(Panel.GetZIndex(menuView) > Panel.GetZIndex(panelView));
            }
            finally
            {
                window?.Close();
            }
        });
    }

    [Fact]
    public void HomePageViewMovesLaunchPanelWhenMenuIsPinned()
    {
        RunOnSta(() =>
        {
            Window? window = null;
            try
            {
                var application = WpfApplicationTestHelper.GetOrCreateApplication();
                EnsureApplicationResources(application);
                var viewModel = CreateViewModelWithInstances();
                var context = new HomePageTestContext(viewModel);
                var view = new HomePageView
                {
                    DataContext = context,
                    Width = 900,
                    Height = 700
                };
                window = CreateHiddenWindow(view);

                window.Show();
                PumpDispatcher(DispatcherPriority.ApplicationIdle);

                Assert.Equal(0d, view.ContentTranslateTransform.X, 1);

                context.IsLaunchMenuPinned = true;
                PumpAnimation();

                Assert.Equal(view.PinnedContentOffsetX, view.ContentTranslateTransform.X, 1);

                context.IsLaunchMenuPinned = false;
                PumpAnimation();

                Assert.Equal(0d, view.ContentTranslateTransform.X, 1);
            }
            finally
            {
                window?.Close();
            }
        });
    }

    [Fact]
    public void HomePageViewAppliesPinnedLaunchPanelPositionOnInitialLoad()
    {
        RunOnSta(() =>
        {
            Window? window = null;
            try
            {
                var application = WpfApplicationTestHelper.GetOrCreateApplication();
                EnsureApplicationResources(application);
                var viewModel = CreateViewModelWithInstances();
                var context = new HomePageTestContext(viewModel)
                {
                    IsLaunchMenuPinned = true
                };
                var view = new HomePageView
                {
                    DataContext = context,
                    Width = 900,
                    Height = 700
                };
                view.Resources["HomeLaunchMenuAnimationDurationMilliseconds"] = 1000d;
                window = CreateHiddenWindow(view);

                window.Show();
                PumpDispatcher(DispatcherPriority.ApplicationIdle);

                Assert.Equal(view.PinnedContentOffsetX, view.ContentTranslateTransform.X, 1);
            }
            finally
            {
                window?.Close();
            }
        });
    }

    [Fact]
    public void HomePageViewKeepsLaunchPanelCenteredWhenMenuExpandsFromHover()
    {
        RunOnSta(() =>
        {
            Window? window = null;
            try
            {
                var application = WpfApplicationTestHelper.GetOrCreateApplication();
                EnsureApplicationResources(application);
                var viewModel = CreateViewModelWithInstances();
                var view = new HomePageView
                {
                    DataContext = new HomePageTestContext(viewModel),
                    Width = 900,
                    Height = 700
                };
                window = CreateHiddenWindow(view);

                window.Show();
                PumpDispatcher(DispatcherPriority.ApplicationIdle);

                var menuView = Assert.IsType<HomeLaunchGameListView>(FindVisualDescendant<HomeLaunchGameListView>(view));
                menuView.SetPointerExpandedForTest(true);
                PumpAnimation();

                Assert.True(menuView.IsMenuExpanded);
                Assert.Equal(0d, view.ContentTranslateTransform.X, 1);
            }
            finally
            {
                window?.Close();
            }
        });
    }

    [Fact]
    public void HomePageViewReversesPinnedContentAnimationFromCurrentAnimatedValue()
    {
        RunOnSta(() =>
        {
            Window? window = null;
            try
            {
                var application = WpfApplicationTestHelper.GetOrCreateApplication();
                EnsureApplicationResources(application);
                var viewModel = CreateViewModelWithInstances();
                var context = new HomePageTestContext(viewModel);
                var view = new HomePageView
                {
                    DataContext = context,
                    Width = 900,
                    Height = 700
                };
                view.Resources["HomeLaunchMenuAnimationDurationMilliseconds"] = 1000d;
                window = CreateHiddenWindow(view);

                window.Show();
                PumpDispatcher(DispatcherPriority.ApplicationIdle);

                var targetOffset = view.PinnedContentOffsetX;
                context.IsLaunchMenuPinned = true;
                PumpDispatcher(DispatcherPriority.Background);
                Thread.Sleep(180);
                PumpDispatcher(DispatcherPriority.Background);

                var midPinnedOffset = view.ContentTranslateTransform.X;
                Assert.InRange(midPinnedOffset, 1, targetOffset - 1);

                context.IsLaunchMenuPinned = false;
                PumpDispatcher(DispatcherPriority.Background);

                Assert.InRange(view.ContentTranslateTransform.X, 1, targetOffset - 1);
                Assert.True(Math.Abs(view.ContentTranslateTransform.X - midPinnedOffset) < 32);
            }
            finally
            {
                window?.Close();
            }
        });
    }

    private static HomeLaunchGameListViewModel CreateViewModelWithInstances()
    {
        var viewModel = CreateViewModel();
        var older = CreateInstance("older", "Older World", "1.20.1", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var selected = CreateInstance("selected", "Selected Pack", "1.21.1", new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));
        var newest = CreateInstance("newest", "Newest World", "1.21.4", new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero));

        viewModel.SetLaunchInstances([older, selected, newest]);
        viewModel.SetSelectedInstance(selected);
        return viewModel;
    }

    private static HomeLaunchGameListViewModel CreateViewModel()
    {
        return new HomeLaunchGameListViewModel(
            new StubGameVersionService(),
            new StubStatusService(),
            _ => Task.FromResult(true));
    }

    private static GameInstance CreateInstance(
        string id,
        string name,
        string minecraftVersion,
        DateTimeOffset createdAt)
    {
        return new GameInstance
        {
            Id = id,
            Name = name,
            MinecraftVersion = minecraftVersion,
            VersionName = minecraftVersion,
            Loader = LoaderKind.Vanilla,
            CreatedAt = createdAt
        };
    }

    private static Window CreateHiddenWindow(FrameworkElement content)
    {
        return new Window
        {
            Width = 900,
            Height = 700,
            Content = content,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Opacity = 0
        };
    }

    private static void EnsureApplicationResources(System.Windows.Application application)
    {
        application.Resources.MergedDictionaries.Add(LoadDictionary("Resources/Themes/Shared.xaml"));
        application.Resources.MergedDictionaries.Add(LoadDictionary("Resources/Themes/Dark.xaml"));
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/MineDock_Launcher_x64;component/Styles/ControlStyles.xaml",
                UriKind.Absolute)
        });
        application.Resources["BooleanToMenuTextVisibilityConverter"] = new BooleanToMenuTextVisibilityConverter();
        application.Resources["HomeLaunchMenuAnimationDurationMilliseconds"] = 1d;
    }

    private static ResourceDictionary LoadDictionary(string relativePath)
    {
        return new ResourceDictionary
        {
            Source = new Uri(
                $"pack://application:,,,/MineDock_Launcher_x64;component/{relativePath}",
                UriKind.Absolute)
        };
    }

    private static void RunOnSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            throw exception;
    }

    private static void PumpAnimation()
    {
        PumpDispatcher(DispatcherPriority.Background);
        Thread.Sleep(420);
        PumpDispatcher(DispatcherPriority.ApplicationIdle);
    }

    private static void PumpDispatcher(DispatcherPriority priority)
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            priority,
            new DispatcherOperationCallback(_ =>
            {
                frame.Continue = false;
                return null;
            }),
            null);
        Dispatcher.PushFrame(frame);
    }

    private static T? FindVisualDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        if (root is T typedRoot)
            return typedRoot;

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var result = FindVisualDescendant<T>(VisualTreeHelper.GetChild(root, index));
            if (result is not null)
                return result;
        }

        return null;
    }

    private static T? FindVisualDescendant<T>(DependencyObject root, Func<T, bool> predicate)
        where T : DependencyObject
    {
        if (root is T typedRoot && predicate(typedRoot))
            return typedRoot;

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var result = FindVisualDescendant(VisualTreeHelper.GetChild(root, index), predicate);
            if (result is not null)
                return result;
        }

        return null;
    }

    private sealed class HomePageTestContext(HomeLaunchGameListViewModel launchGames) : INotifyPropertyChanged
    {
        private bool isLaunchMenuPinned;

        public event PropertyChangedEventHandler? PropertyChanged;

        public HomeLaunchGameListViewModel LaunchGames { get; } = launchGames;

        public bool IsLaunchMenuPinned
        {
            get => isLaunchMenuPinned;
            set
            {
                if (isLaunchMenuPinned == value)
                    return;

                isLaunchMenuPinned = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLaunchMenuPinned)));
            }
        }
    }

    private sealed class StubGameVersionService : IGameVersionService
    {
        public Task<IReadOnlyList<MinecraftVersionInfo>> GetVersionsAsync(
            DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
            CancellationToken cancellationToken = default,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            return Task.FromResult<IReadOnlyList<MinecraftVersionInfo>>([]);
        }
    }

    private sealed class StubStatusService : IStatusService
    {
        public event Action<string>? MessageReported;

        public void Report(string message)
        {
            MessageReported?.Invoke(message);
        }
    }
}
