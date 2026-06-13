using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Launcher.App.Controls;

public partial class VirtualizedListPageItemsControl : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(VirtualizedListPageItemsControl),
            new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty ItemTemplateProperty =
        DependencyProperty.Register(nameof(ItemTemplate), typeof(DataTemplate), typeof(VirtualizedListPageItemsControl), new PropertyMetadata(null));

    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(
            nameof(SelectedItem),
            typeof(object),
            typeof(VirtualizedListPageItemsControl),
            new PropertyMetadata(null, OnSelectedItemChanged));

    public static readonly DependencyProperty EstimatedItemHeightProperty =
        DependencyProperty.Register(nameof(EstimatedItemHeight), typeof(double), typeof(VirtualizedListPageItemsControl), new PropertyMetadata(58d));

    public static readonly DependencyProperty ContentTopOffsetProperty =
        DependencyProperty.Register(nameof(ContentTopOffset), typeof(double), typeof(VirtualizedListPageItemsControl), new PropertyMetadata(132d));

    public static readonly DependencyProperty InitialItemCountProperty =
        DependencyProperty.Register(nameof(InitialItemCount), typeof(int), typeof(VirtualizedListPageItemsControl), new PropertyMetadata(64));

    public static readonly DependencyProperty MinimumRenderedItemCountProperty =
        DependencyProperty.Register(nameof(MinimumRenderedItemCount), typeof(int), typeof(VirtualizedListPageItemsControl), new PropertyMetadata(88));

    public static readonly DependencyProperty OverscanItemCountProperty =
        DependencyProperty.Register(nameof(OverscanItemCount), typeof(int), typeof(VirtualizedListPageItemsControl), new PropertyMetadata(28));

    public static readonly DependencyProperty WindowStepProperty =
        DependencyProperty.Register(nameof(WindowStep), typeof(int), typeof(VirtualizedListPageItemsControl), new PropertyMetadata(8));

    public static readonly DependencyProperty EnterAnimationLimitProperty =
        DependencyProperty.Register(nameof(EnterAnimationLimit), typeof(int), typeof(VirtualizedListPageItemsControl), new PropertyMetadata(14));

    public static readonly DependencyProperty TopSpacerHeightProperty =
        DependencyProperty.Register(nameof(TopSpacerHeight), typeof(double), typeof(VirtualizedListPageItemsControl), new PropertyMetadata(0d));

    public static readonly DependencyProperty BottomSpacerHeightProperty =
        DependencyProperty.Register(nameof(BottomSpacerHeight), typeof(double), typeof(VirtualizedListPageItemsControl), new PropertyMetadata(0d));

    private readonly List<object> items = [];
    private readonly Dictionary<object, int> itemIndexes = new(ReferenceEqualityComparer.Instance);
    private INotifyCollectionChanged? currentCollection;
    private ScrollViewer? scrollViewer;
    private bool isRebuildQueued;
    private bool isButtonHookQueued;
    private bool isStateRefreshQueued;
    private bool isWindowUpdateQueued;
    private object? hoveredItem;
    private int renderedWindowStartIndex = -1;

    public VirtualizedListPageItemsControl()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            AttachScrollViewer();
            RefreshViewport();
        };
        Unloaded += (_, _) => DetachScrollViewer();
        SizeChanged += (_, _) => QueueWindowUpdateFromScroll();
    }

    public ObservableCollection<VirtualizedListPageItem> RenderedItems { get; } = [];

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public DataTemplate? ItemTemplate
    {
        get => (DataTemplate?)GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public double EstimatedItemHeight
    {
        get => (double)GetValue(EstimatedItemHeightProperty);
        set => SetValue(EstimatedItemHeightProperty, value);
    }

    public double ContentTopOffset
    {
        get => (double)GetValue(ContentTopOffsetProperty);
        set => SetValue(ContentTopOffsetProperty, value);
    }

    public int InitialItemCount
    {
        get => (int)GetValue(InitialItemCountProperty);
        set => SetValue(InitialItemCountProperty, value);
    }

    public int MinimumRenderedItemCount
    {
        get => (int)GetValue(MinimumRenderedItemCountProperty);
        set => SetValue(MinimumRenderedItemCountProperty, value);
    }

    public int OverscanItemCount
    {
        get => (int)GetValue(OverscanItemCountProperty);
        set => SetValue(OverscanItemCountProperty, value);
    }

    public int WindowStep
    {
        get => (int)GetValue(WindowStepProperty);
        set => SetValue(WindowStepProperty, value);
    }

    public int EnterAnimationLimit
    {
        get => (int)GetValue(EnterAnimationLimitProperty);
        set => SetValue(EnterAnimationLimitProperty, value);
    }

    public double TopSpacerHeight
    {
        get => (double)GetValue(TopSpacerHeightProperty);
        private set => SetValue(TopSpacerHeightProperty, value);
    }

    public double BottomSpacerHeight
    {
        get => (double)GetValue(BottomSpacerHeightProperty);
        private set => SetValue(BottomSpacerHeightProperty, value);
    }

    public bool ContainsItem(object? item)
    {
        return item is not null && itemIndexes.ContainsKey(item);
    }

    public bool IsItemRendered(object? item)
    {
        return item is not null && RenderedItems.Any(renderedItem => ReferenceEquals(renderedItem.Item, item));
    }

    public double GetItemTopOffset(object item)
    {
        var index = FindItemIndex(item);
        return index >= 0 ? index * EstimatedItemHeight : 0;
    }

    public Button? FindRenderedButton(object item)
    {
        return VisualTreeSearch.FindDescendant<Button>(
            PART_ItemsControl,
            button => button.DataContext is VirtualizedListPageItem renderedItem
                && ReferenceEquals(renderedItem.Item, item));
    }

    public void RefreshViewport()
    {
        isWindowUpdateQueued = false;
        UpdateWindowFromScroll();
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is VirtualizedListPageItemsControl control)
            control.SetItemsSource(e.OldValue as IEnumerable, e.NewValue as IEnumerable);
    }

    private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is VirtualizedListPageItemsControl control)
            control.QueueStateRefresh();
    }

    private void SetItemsSource(IEnumerable? oldSource, IEnumerable? newSource)
    {
        if (currentCollection is not null)
            currentCollection.CollectionChanged -= ItemsSourceCollectionChanged;

        currentCollection = newSource as INotifyCollectionChanged;
        if (currentCollection is not null)
            currentCollection.CollectionChanged += ItemsSourceCollectionChanged;

        RebuildItems(newSource);
    }

    private void ItemsSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        QueueRebuildItems();
    }

    private void QueueRebuildItems()
    {
        if (isRebuildQueued)
            return;

        isRebuildQueued = true;
        Dispatcher.BeginInvoke(
            () =>
            {
                isRebuildQueued = false;
                RebuildItems(ItemsSource);
            },
            DispatcherPriority.Background);
    }

    private void RebuildItems(IEnumerable? source)
    {
        items.Clear();
        itemIndexes.Clear();
        if (source is not null)
        {
            foreach (var item in source)
            {
                itemIndexes[item] = items.Count;
                items.Add(item);
            }
        }

        var visibleCount = Math.Min(Math.Max(0, InitialItemCount), items.Count);
        SetVisibleWindow(0, visibleCount, animateFromStart: true);
        if (IsLoaded)
            QueueWindowUpdateFromScroll();
    }

    private void AttachScrollViewer()
    {
        var nextScrollViewer = FindAncestorScrollViewer();
        if (ReferenceEquals(scrollViewer, nextScrollViewer))
            return;

        DetachScrollViewer();
        scrollViewer = nextScrollViewer;
        if (scrollViewer is null)
            return;

        scrollViewer.ScrollChanged += ScrollViewer_OnScrollChanged;
        scrollViewer.SizeChanged += ScrollViewer_OnSizeChanged;
    }

    private void DetachScrollViewer()
    {
        if (scrollViewer is null)
            return;

        scrollViewer.ScrollChanged -= ScrollViewer_OnScrollChanged;
        scrollViewer.SizeChanged -= ScrollViewer_OnSizeChanged;
        scrollViewer = null;
    }

    private void ScrollViewer_OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        QueueWindowUpdateFromScroll();
    }

    private void ScrollViewer_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        QueueWindowUpdateFromScroll();
    }

    private void QueueWindowUpdateFromScroll()
    {
        if (isWindowUpdateQueued)
            return;

        isWindowUpdateQueued = true;
        Dispatcher.BeginInvoke(
            () =>
            {
                isWindowUpdateQueued = false;
                UpdateWindowFromScroll();
            },
            DispatcherPriority.Render);
    }

    private void UpdateWindowFromScroll()
    {
        if (items.Count == 0)
        {
            SetVisibleWindow(0, 0, animateFromStart: false);
            return;
        }

        AttachScrollViewer();
        if (scrollViewer is null || scrollViewer.ViewportHeight <= 0)
            return;

        var estimatedItemHeight = Math.Max(1, EstimatedItemHeight);
        var listOffset = Math.Max(0, scrollViewer.VerticalOffset - ContentTopOffset);
        var firstVisibleIndex = Math.Max(0, (int)Math.Floor(listOffset / estimatedItemHeight));
        var viewportItemCount = Math.Max(1, (int)Math.Ceiling(scrollViewer.ViewportHeight / estimatedItemHeight));
        var startIndex = Math.Max(0, firstVisibleIndex - Math.Max(0, OverscanItemCount));
        var step = Math.Max(1, WindowStep);
        startIndex = Math.Max(0, (startIndex / step) * step);

        var visibleCount = Math.Max(Math.Max(1, MinimumRenderedItemCount), viewportItemCount + Math.Max(0, OverscanItemCount) * 2);
        SetVisibleWindow(startIndex, visibleCount, animateFromStart: false);
    }

    private void SetVisibleWindow(int startIndex, int requestedVisibleCount, bool animateFromStart)
    {
        if (items.Count == 0 || requestedVisibleCount <= 0)
        {
            renderedWindowStartIndex = 0;
            TopSpacerHeight = 0;
            BottomSpacerHeight = 0;
            RenderedItems.Clear();
            QueueStateRefresh();
            return;
        }

        startIndex = Math.Clamp(startIndex, 0, items.Count - 1);
        var visibleCount = Math.Min(requestedVisibleCount, items.Count - startIndex);
        var oldStartIndex = renderedWindowStartIndex;
        var oldEndIndex = oldStartIndex + RenderedItems.Count;
        var newEndIndex = startIndex + visibleCount;

        if (oldStartIndex == startIndex && RenderedItems.Count == visibleCount)
        {
            QueueStateRefresh();
            return;
        }

        renderedWindowStartIndex = startIndex;
        TopSpacerHeight = renderedWindowStartIndex * EstimatedItemHeight;
        BottomSpacerHeight = Math.Max(0, items.Count - renderedWindowStartIndex - visibleCount) * EstimatedItemHeight;

        if (!animateFromStart
            && oldStartIndex >= 0
            && startIndex < oldEndIndex
            && newEndIndex > oldStartIndex)
        {
            MoveVisibleWindow(oldStartIndex, oldEndIndex, startIndex, newEndIndex);
        }
        else
        {
            RenderedItems.Clear();
            for (var i = 0; i < visibleCount; i++)
            {
                RenderedItems.Add(new VirtualizedListPageItem(
                    items[renderedWindowStartIndex + i],
                    i,
                    shouldPlayEnterAnimation: animateFromStart && i < EnterAnimationLimit));
            }
        }

        RefreshRenderedItemState();
        QueueButtonHook();
    }

    private void MoveVisibleWindow(int oldStartIndex, int oldEndIndex, int newStartIndex, int newEndIndex)
    {
        if (newStartIndex > oldStartIndex)
        {
            var removeCount = Math.Min(newStartIndex - oldStartIndex, RenderedItems.Count);
            for (var i = 0; i < removeCount; i++)
                RenderedItems.RemoveAt(0);
        }
        else if (newStartIndex < oldStartIndex)
        {
            var prependEnd = Math.Min(oldStartIndex - 1, newEndIndex - 1);
            for (var i = prependEnd; i >= newStartIndex; i--)
                RenderedItems.Insert(0, new VirtualizedListPageItem(items[i], 0, shouldPlayEnterAnimation: false));
        }

        var appendStart = Math.Max(oldEndIndex, newStartIndex);
        for (var i = appendStart; i < newEndIndex; i++)
            RenderedItems.Add(new VirtualizedListPageItem(items[i], RenderedItems.Count, shouldPlayEnterAnimation: false));

        while (RenderedItems.Count > newEndIndex - newStartIndex)
            RenderedItems.RemoveAt(RenderedItems.Count - 1);
    }

    private void QueueButtonHook()
    {
        if (isButtonHookQueued)
            return;

        isButtonHookQueued = true;
        Dispatcher.BeginInvoke(
            () =>
            {
                isButtonHookQueued = false;
                HookRenderedItemButtons();
            },
            DispatcherPriority.Loaded);
    }

    private void QueueStateRefresh()
    {
        if (isStateRefreshQueued)
            return;

        isStateRefreshQueued = true;
        Dispatcher.BeginInvoke(
            () =>
            {
                isStateRefreshQueued = false;
                RefreshRenderedItemState();
            },
            DispatcherPriority.Loaded);
    }

    private void RefreshRenderedItemState()
    {
        for (var i = 0; i < RenderedItems.Count; i++)
        {
            var item = RenderedItems[i];
            item.EnterAnimationIndex = i;
            item.IsFirstVisible = i == 0;
            item.IsLastVisible = i == RenderedItems.Count - 1;
            item.IsPreviousItemHighlighted = i > 0 && IsHighlighted(RenderedItems[i - 1]);
        }
    }

    private void HookRenderedItemButtons()
    {
        var buttons = FindRenderedItemButtons();
        foreach (var button in buttons)
        {
            button.MouseEnter -= ItemButton_OnMouseEnter;
            button.MouseLeave -= ItemButton_OnMouseLeave;
            button.MouseEnter += ItemButton_OnMouseEnter;
            button.MouseLeave += ItemButton_OnMouseLeave;
        }
    }

    private void ItemButton_OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: VirtualizedListPageItem item })
            hoveredItem = item.Item;

        QueueStateRefresh();
    }

    private void ItemButton_OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        hoveredItem = null;
        QueueStateRefresh();
    }

    private IReadOnlyList<ListPageItemButton> FindRenderedItemButtons()
    {
        var buttons = new List<ListPageItemButton>();
        AddDescendantItemButtons(PART_ItemsControl, buttons);
        return buttons;
    }

    private static void AddDescendantItemButtons(DependencyObject root, ICollection<ListPageItemButton> buttons)
    {
        var childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is ListPageItemButton itemButton)
                buttons.Add(itemButton);

            AddDescendantItemButtons(child, buttons);
        }
    }

    private bool IsHighlighted(VirtualizedListPageItem item)
    {
        return ReferenceEquals(item.Item, SelectedItem) || ReferenceEquals(item.Item, hoveredItem);
    }

    private int FindItemIndex(object item)
    {
        return itemIndexes.TryGetValue(item, out var index) ? index : -1;
    }

    private ScrollViewer? FindAncestorScrollViewer()
    {
        DependencyObject? current = this;
        while (current is not null)
        {
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            if (current is ScrollViewer found)
                return found;
        }

        return null;
    }
}

public sealed class VirtualizedListPageItem : INotifyPropertyChanged
{
    private bool isFirstVisible;
    private bool isLastVisible;
    private bool isPreviousItemHighlighted;
    private bool shouldPlayEnterAnimation;
    private int enterAnimationIndex;

    public VirtualizedListPageItem(object item, int enterAnimationIndex, bool shouldPlayEnterAnimation)
    {
        Item = item;
        this.enterAnimationIndex = enterAnimationIndex;
        this.shouldPlayEnterAnimation = shouldPlayEnterAnimation;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public object Item { get; }

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
