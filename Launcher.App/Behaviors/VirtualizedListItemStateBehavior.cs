using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Launcher.App.Controls;
using Launcher.App.Utilities;

namespace Launcher.App.Behaviors;

public static class VirtualizedListItemStateBehavior
{
    private const int EntranceAnimationPassCount = 8;
    private static readonly TimeSpan EntranceAnimationPassInterval = TimeSpan.FromMilliseconds(45);

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(VirtualizedListItemStateBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty EntranceAnimationTokenProperty =
        DependencyProperty.RegisterAttached(
            "EntranceAnimationToken",
            typeof(int),
            typeof(VirtualizedListItemStateBehavior),
            new PropertyMetadata(0, OnEntranceAnimationTokenChanged));

    public static readonly DependencyProperty ContentTopOffsetProperty =
        DependencyProperty.RegisterAttached(
            "ContentTopOffset",
            typeof(double),
            typeof(VirtualizedListItemStateBehavior),
            new PropertyMetadata(0d));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static int GetEntranceAnimationToken(DependencyObject element) => (int)element.GetValue(EntranceAnimationTokenProperty);

    public static void SetEntranceAnimationToken(DependencyObject element, int value) => element.SetValue(EntranceAnimationTokenProperty, value);

    public static double GetContentTopOffset(DependencyObject element) => (double)element.GetValue(ContentTopOffsetProperty);

    public static void SetContentTopOffset(DependencyObject element, double value) => element.SetValue(ContentTopOffsetProperty, value);

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not ListBox listBox)
            return;

        if ((bool)e.NewValue)
        {
            GetState(listBox).Attach(listBox);
            return;
        }

        if (GetOptionalState(listBox) is { } state)
            state.Detach();
    }

    private static void OnEntranceAnimationTokenChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not ListBox listBox || !GetIsEnabled(listBox))
            return;

        var token = (int)e.NewValue;
        if (token > 0)
            GetState(listBox).QueueEntranceAnimation(token);
    }

    private static ListBoxState GetState(ListBox listBox)
    {
        if (listBox.GetValue(StateProperty) is ListBoxState state)
            return state;

        state = new ListBoxState();
        listBox.SetValue(StateProperty, state);
        return state;
    }

    private static ListBoxState? GetOptionalState(ListBox listBox)
    {
        return listBox.GetValue(StateProperty) as ListBoxState;
    }

    private static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached(
            "State",
            typeof(ListBoxState),
            typeof(VirtualizedListItemStateBehavior),
            new PropertyMetadata(null));

    private sealed class ListBoxState
    {
        private readonly HashSet<object> animatedEntranceItems = new(ReferenceEqualityComparer.Instance);
        private readonly DispatcherTimer entranceAnimationTimer;
        private ListBox? listBox;
        private ScrollViewer? scrollViewer;
        private bool isStateRefreshQueued;
        private bool isEntranceAnimationPending;
        private int entranceAnimationPassesRemaining;
        private int observedEntranceAnimationToken;
        private object? hoveredItem;

        public ListBoxState()
        {
            entranceAnimationTimer = new DispatcherTimer(DispatcherPriority.Loaded)
            {
                Interval = EntranceAnimationPassInterval
            };
            entranceAnimationTimer.Tick += EntranceAnimationTimer_OnTick;
        }

        public void Attach(ListBox target)
        {
            if (ReferenceEquals(listBox, target))
                return;

            Detach();
            listBox = target;
            target.Loaded += ListBox_OnLoaded;
            target.Unloaded += ListBox_OnUnloaded;
            target.SelectionChanged += ListBox_OnSelectionChanged;
            target.ItemContainerGenerator.StatusChanged += ItemContainerGenerator_OnStatusChanged;
            AttachScrollViewer();
            QueueRenderedItemStateRefresh();
        }

        public void Detach()
        {
            entranceAnimationTimer.Stop();
            DetachScrollViewer();

            if (listBox is null)
                return;

            listBox.Loaded -= ListBox_OnLoaded;
            listBox.Unloaded -= ListBox_OnUnloaded;
            listBox.SelectionChanged -= ListBox_OnSelectionChanged;
            listBox.ItemContainerGenerator.StatusChanged -= ItemContainerGenerator_OnStatusChanged;
            listBox = null;
            hoveredItem = null;
            animatedEntranceItems.Clear();
        }

        public void QueueEntranceAnimation(int token)
        {
            if (token == observedEntranceAnimationToken)
                return;

            observedEntranceAnimationToken = token;
            isEntranceAnimationPending = true;
            entranceAnimationPassesRemaining = EntranceAnimationPassCount;
            animatedEntranceItems.Clear();
            entranceAnimationTimer.Start();
            QueueRenderedItemStateRefresh();
        }

        private void ListBox_OnLoaded(object sender, RoutedEventArgs e)
        {
            AttachScrollViewer();
            QueueRenderedItemStateRefresh();

            if (listBox is { } target)
                QueueEntranceAnimation(GetEntranceAnimationToken(target));
        }

        private void ListBox_OnUnloaded(object sender, RoutedEventArgs e)
        {
            entranceAnimationTimer.Stop();
            DetachScrollViewer();
        }

        private void ListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            QueueRenderedItemStateRefresh();
        }

        private void ItemContainerGenerator_OnStatusChanged(object? sender, EventArgs e)
        {
            if (listBox?.ItemContainerGenerator.Status == GeneratorStatus.ContainersGenerated)
                PreparePendingEntranceAnimationStates();

            QueueRenderedItemStateRefresh();
        }

        private void AttachScrollViewer()
        {
            if (listBox is null)
                return;

            listBox.ApplyTemplate();
            var nextScrollViewer = VisualTreeSearch.FindDescendant<ScrollViewer>(listBox, _ => true);
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
            if (isStateRefreshQueued || listBox is null)
                return;

            isStateRefreshQueued = true;
            listBox.Dispatcher.BeginInvoke(
                () =>
                {
                    isStateRefreshQueued = false;
                    RefreshRenderedItemState();
                    PlayPendingEntranceAnimations();
                },
                DispatcherPriority.Loaded);
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
            if (listBox is null)
                return;

            var containers = FindRealizedContainers(listBox);
            for (var i = 0; i < containers.Count; i++)
            {
                var container = containers[i];
                var state = EnsureItemState(container);
                state.IsFirstVisible = i == 0;
                state.IsLastVisible = i == containers.Count - 1;
                state.IsPreviousItemHighlighted = i > 0 && IsHighlighted(containers[i - 1].DataContext);

                if (VisualTreeSearch.FindDescendant<ListPageItemButton>(container, _ => true) is { } button)
                    HookItemButton(button);
            }
        }

        private void PlayPendingEntranceAnimations()
        {
            if (!isEntranceAnimationPending || listBox is null)
                return;

            if (listBox.Items.Count == 0)
            {
                return;
            }

            PreparePendingEntranceAnimationStates();
        }

        private void PreparePendingEntranceAnimationStates()
        {
            if (!isEntranceAnimationPending || listBox is null)
                return;

            var containers = FindRealizedContainers(listBox);
            foreach (var container in containers)
            {
                var index = listBox.ItemContainerGenerator.IndexFromContainer(container);
                if (index < 0)
                    continue;

                if (container.DataContext is not { } item || animatedEntranceItems.Contains(item))
                    continue;

                if (!IsContainerInUsableViewport(container))
                    continue;

                var state = EnsureItemState(container);
                state.EnterAnimationIndex = index;
                state.ShouldPlayEnterAnimation = true;
                animatedEntranceItems.Add(item);
            }
        }

        private bool IsContainerInUsableViewport(ListBoxItem container)
        {
            if (scrollViewer is null || scrollViewer.ActualHeight <= 0 || listBox is null)
                return true;

            try
            {
                var bounds = container
                    .TransformToAncestor(scrollViewer)
                    .TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight));
                var visibleTop = GetContentTopOffset(listBox);
                var visibleBottom = Math.Max(visibleTop, scrollViewer.ActualHeight);
                return bounds.Bottom > visibleTop && bounds.Top < visibleBottom;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static IReadOnlyList<ListBoxItem> FindRealizedContainers(ListBox listBox)
        {
            var containers = new List<ListBoxItem>();
            AddRealizedContainers(listBox, containers);
            containers.Sort((left, right) =>
                listBox.ItemContainerGenerator.IndexFromContainer(left)
                    .CompareTo(listBox.ItemContainerGenerator.IndexFromContainer(right)));
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

        private static VirtualizedListItemState EnsureItemState(ListBoxItem container)
        {
            if (container.Tag is VirtualizedListItemState state)
                return state;

            state = new VirtualizedListItemState();
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
            if (sender is FrameworkElement { DataContext: { } item })
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
            return item is not null
                && (ReferenceEquals(item, hoveredItem) || ReferenceEquals(item, listBox?.SelectedItem));
        }
    }
}

public sealed class VirtualizedListItemState : INotifyPropertyChanged
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
