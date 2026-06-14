using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Launcher.App.Controls;
using Launcher.App.Utilities;
using Launcher.App.ViewModels;

namespace Launcher.App.Views;

public partial class DownloadVersionListView : UserControl
{
    private const double VersionItemHeight = 58d;
    private const double ContentTopOffset = 132d;
    private const double ContentBottomOffset = 64d;
    private const int EntranceAnimationPassCount = 8;
    private static readonly TimeSpan EntranceAnimationPassInterval = TimeSpan.FromMilliseconds(45);

    private readonly HashSet<object> animatedEntranceItems = new(ReferenceEqualityComparer.Instance);
    private readonly DispatcherTimer entranceAnimationTimer;
    private INotifyPropertyChanged? currentViewModelNotifier;
    private ScrollViewer? scrollViewer;
    private bool isStateRefreshQueued;
    private bool isEntranceAnimationPending;
    private int entranceAnimationPassesRemaining;
    private int observedEntranceAnimationToken;
    private object? hoveredItem;

    public DownloadVersionListView()
    {
        InitializeComponent();

        entranceAnimationTimer = new DispatcherTimer(DispatcherPriority.Loaded)
        {
            Interval = EntranceAnimationPassInterval
        };
        entranceAnimationTimer.Tick += EntranceAnimationTimer_OnTick;

        Loaded += (_, _) =>
        {
            AttachScrollViewer();
            QueueRenderedItemStateRefresh();
            QueueEntranceAnimationIfRequested();
        };
        Unloaded += (_, _) =>
        {
            entranceAnimationTimer.Stop();
            DetachScrollViewer();
        };
        DataContextChanged += DownloadVersionListView_OnDataContextChanged;
        DownloadVersionListBox.ItemContainerGenerator.StatusChanged += DownloadVersionItemContainerGenerator_OnStatusChanged;
        DownloadVersionListBox.SelectionChanged += (_, _) => QueueRenderedItemStateRefresh();
    }

    public ScrollViewer ScrollViewer
    {
        get
        {
            AttachScrollViewer();
            return scrollViewer
                ?? throw new InvalidOperationException("Download version list scroll viewer is not available.");
        }
    }

    public double EstimatedVersionItemHeight => VersionItemHeight;

    public Button? FindVersionButton(DownloadMinecraftVersionItem selectedVersion)
    {
        if (DownloadVersionListBox.ItemContainerGenerator.ContainerFromItem(selectedVersion) is not ListBoxItem container)
            return null;

        return VisualTreeSearch.FindDescendant<ListPageItemButton>(container, _ => true)?.InnerButton;
    }

    public bool ContainsVersion(DownloadMinecraftVersionItem selectedVersion)
    {
        return DownloadVersionListBox.Items.Contains(selectedVersion);
    }

    public bool IsVersionRendered(DownloadMinecraftVersionItem selectedVersion)
    {
        return DownloadVersionListBox.ItemContainerGenerator.ContainerFromItem(selectedVersion) is ListBoxItem;
    }

    public bool RealizeVersion(DownloadMinecraftVersionItem selectedVersion)
    {
        if (!ContainsVersion(selectedVersion))
            return false;

        DownloadVersionListBox.ScrollIntoView(selectedVersion);
        DownloadVersionListBox.UpdateLayout();
        return IsVersionRendered(selectedVersion);
    }

    public double GetVersionTopOffset(DownloadMinecraftVersionItem selectedVersion)
    {
        var index = DownloadVersionListBox.Items.IndexOf(selectedVersion);
        return index >= 0 ? index * VersionItemHeight : 0;
    }

    public void RefreshViewport()
    {
        DownloadVersionListBox.UpdateLayout();
        QueueRenderedItemStateRefresh();
    }

    private void DownloadVersionListView_OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (currentViewModelNotifier is not null)
            currentViewModelNotifier.PropertyChanged -= DownloadPageViewModel_OnPropertyChanged;

        currentViewModelNotifier = e.NewValue as INotifyPropertyChanged;
        if (currentViewModelNotifier is not null)
            currentViewModelNotifier.PropertyChanged += DownloadPageViewModel_OnPropertyChanged;

        observedEntranceAnimationToken = 0;
        QueueEntranceAnimationIfRequested();
        QueueRenderedItemStateRefresh();
    }

    private void DownloadPageViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DownloadPageViewModel.VisibleVersions))
        {
            hoveredItem = null;
        }

        if (e.PropertyName is nameof(DownloadPageViewModel.ListEntranceAnimationToken))
        {
            QueueEntranceAnimationIfRequested();
        }

        if (e.PropertyName is nameof(DownloadPageViewModel.SelectedMinecraftVersion)
            or nameof(DownloadPageViewModel.VisibleVersions))
        {
            QueueRenderedItemStateRefresh();
        }
    }

    private void DownloadVersionItemContainerGenerator_OnStatusChanged(object? sender, EventArgs e)
    {
        if (DownloadVersionListBox.ItemContainerGenerator.Status == GeneratorStatus.ContainersGenerated)
            PreparePendingEntranceAnimationStates();

        QueueRenderedItemStateRefresh();
    }

    private void AttachScrollViewer()
    {
        DownloadVersionListBox.ApplyTemplate();

        var nextScrollViewer = VisualTreeSearch.FindDescendant<ScrollViewer>(DownloadVersionListBox, _ => true);
        if (ReferenceEquals(scrollViewer, nextScrollViewer))
            return;

        DetachScrollViewer();
        scrollViewer = nextScrollViewer;
        if (scrollViewer is not null)
            scrollViewer.ScrollChanged += ScrollViewer_OnScrollChanged;
    }

    private void DetachScrollViewer()
    {
        if (scrollViewer is null)
            return;

        scrollViewer.ScrollChanged -= ScrollViewer_OnScrollChanged;
        scrollViewer = null;
    }

    private void ScrollViewer_OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        QueueRenderedItemStateRefresh();
    }

    private void QueueRenderedItemStateRefresh()
    {
        if (isStateRefreshQueued)
            return;

        isStateRefreshQueued = true;
        Dispatcher.BeginInvoke(
            () =>
            {
                isStateRefreshQueued = false;
                RefreshRenderedItemState();
                PlayPendingEntranceAnimations();
            },
            DispatcherPriority.Loaded);
    }

    private void QueueEntranceAnimation()
    {
        isEntranceAnimationPending = true;
        entranceAnimationPassesRemaining = EntranceAnimationPassCount;
        animatedEntranceItems.Clear();
        entranceAnimationTimer.Start();
        QueueRenderedItemStateRefresh();
    }

    private void QueueEntranceAnimationIfRequested()
    {
        if (DataContext is not DownloadPageViewModel viewModel)
            return;

        if (viewModel.ListEntranceAnimationToken <= 0
            || viewModel.ListEntranceAnimationToken == observedEntranceAnimationToken)
        {
            return;
        }

        observedEntranceAnimationToken = viewModel.ListEntranceAnimationToken;
        QueueEntranceAnimation();
    }

    private void EntranceAnimationTimer_OnTick(object? sender, EventArgs e)
    {
        if (!isEntranceAnimationPending)
        {
            entranceAnimationTimer.Stop();
            return;
        }

        QueueRenderedItemStateRefresh();
        entranceAnimationPassesRemaining--;
        if (entranceAnimationPassesRemaining > 0)
            return;

        isEntranceAnimationPending = false;
        entranceAnimationTimer.Stop();
    }

    private void RefreshRenderedItemState()
    {
        var containers = FindRealizedContainers();
        for (var i = 0; i < containers.Count; i++)
        {
            var container = containers[i];
            var state = EnsureState(container);
            state.IsFirstVisible = i == 0;
            state.IsLastVisible = i == containers.Count - 1;
            state.IsPreviousItemHighlighted = i > 0 && IsHighlighted(containers[i - 1].DataContext);

            if (VisualTreeSearch.FindDescendant<ListPageItemButton>(container, _ => true) is { } button)
                HookItemButton(button);
        }
    }

    private void PlayPendingEntranceAnimations()
    {
        if (!isEntranceAnimationPending)
            return;

        if (DownloadVersionListBox.Items.Count == 0)
        {
            isEntranceAnimationPending = false;
            entranceAnimationTimer.Stop();
            return;
        }

        PreparePendingEntranceAnimationStates();
    }

    private void PreparePendingEntranceAnimationStates()
    {
        if (!isEntranceAnimationPending)
            return;

        var containers = FindRealizedContainers();
        foreach (var container in containers)
        {
            var index = DownloadVersionListBox.ItemContainerGenerator.IndexFromContainer(container);
            if (index < 0)
                continue;

            if (container.DataContext is not { } item || animatedEntranceItems.Contains(item))
                continue;

            if (!IsContainerInUsableViewport(container))
                continue;

            var state = EnsureState(container);
            state.EnterAnimationIndex = index;
            state.ShouldPlayEnterAnimation = true;
            animatedEntranceItems.Add(item);
        }
    }

    private bool IsContainerInUsableViewport(ListBoxItem container)
    {
        if (scrollViewer is null || scrollViewer.ActualHeight <= 0)
            return true;

        try
        {
            var bounds = container
                .TransformToAncestor(scrollViewer)
                .TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight));
            var visibleTop = ContentTopOffset;
            var visibleBottom = Math.Max(visibleTop, scrollViewer.ActualHeight);
            return bounds.Bottom > visibleTop && bounds.Top < visibleBottom;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private IReadOnlyList<ListBoxItem> FindRealizedContainers()
    {
        var containers = new List<ListBoxItem>();
        AddRealizedContainers(DownloadVersionListBox, containers);
        containers.Sort((left, right) =>
            DownloadVersionListBox.ItemContainerGenerator.IndexFromContainer(left)
                .CompareTo(DownloadVersionListBox.ItemContainerGenerator.IndexFromContainer(right)));
        return containers;
    }

    private static void AddRealizedContainers(DependencyObject root, ICollection<ListBoxItem> containers)
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ListBoxItem item)
                containers.Add(item);

            AddRealizedContainers(child, containers);
        }
    }

    private static DownloadVersionListItemState EnsureState(ListBoxItem container)
    {
        if (container.Tag is DownloadVersionListItemState state)
            return state;

        state = new DownloadVersionListItemState();
        container.Tag = state;
        return state;
    }

    private void HookItemButton(ListPageItemButton button)
    {
        button.MouseEnter -= ItemButton_OnMouseEnter;
        button.MouseLeave -= ItemButton_OnMouseLeave;
        button.MouseEnter += ItemButton_OnMouseEnter;
        button.MouseLeave += ItemButton_OnMouseLeave;
    }

    private void ItemButton_OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DownloadMinecraftVersionItem item })
            hoveredItem = item;

        QueueRenderedItemStateRefresh();
    }

    private void ItemButton_OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        hoveredItem = null;
        QueueRenderedItemStateRefresh();
    }

    private bool IsHighlighted(object? item)
    {
        return item is DownloadMinecraftVersionItem version
            && (ReferenceEquals(version, hoveredItem) || version.IsSelected);
    }
}

public sealed class DownloadVersionListItemState : INotifyPropertyChanged
{
    private bool isFirstVisible;
    private bool isLastVisible;
    private bool isPreviousItemHighlighted;
    private bool shouldPlayEnterAnimation;
    private int enterAnimationIndex;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsFirstVisible
    {
        get => isFirstVisible;
        set => SetProperty(ref isFirstVisible, value);
    }

    public bool IsLastVisible
    {
        get => isLastVisible;
        set => SetProperty(ref isLastVisible, value);
    }

    public bool IsPreviousItemHighlighted
    {
        get => isPreviousItemHighlighted;
        set => SetProperty(ref isPreviousItemHighlighted, value);
    }

    public bool ShouldPlayEnterAnimation
    {
        get => shouldPlayEnterAnimation;
        set => SetProperty(ref shouldPlayEnterAnimation, value);
    }

    public int EnterAnimationIndex
    {
        get => enterAnimationIndex;
        set => SetProperty(ref enterAnimationIndex, value);
    }

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
