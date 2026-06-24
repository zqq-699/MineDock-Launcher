using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Launcher.App.Controls;

public partial class ListPageFrame : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(ListPageFrame), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TitleIconSourceProperty =
        DependencyProperty.Register(nameof(TitleIconSource), typeof(ImageSource), typeof(ListPageFrame), new PropertyMetadata(null));

    public static readonly DependencyProperty IsHeaderBackButtonVisibleProperty =
        DependencyProperty.Register(nameof(IsHeaderBackButtonVisible), typeof(bool), typeof(ListPageFrame), new PropertyMetadata(false));

    public static readonly DependencyProperty HeaderBackCommandProperty =
        DependencyProperty.Register(nameof(HeaderBackCommand), typeof(ICommand), typeof(ListPageFrame), new PropertyMetadata(null));

    public static readonly DependencyProperty SearchTextProperty =
        DependencyProperty.Register(
            nameof(SearchText),
            typeof(string),
            typeof(ListPageFrame),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty IsSearchVisibleProperty =
        DependencyProperty.Register(nameof(IsSearchVisible), typeof(bool), typeof(ListPageFrame), new PropertyMetadata(true));

    public static readonly DependencyProperty SearchTrailingContentProperty =
        DependencyProperty.Register(nameof(SearchTrailingContent), typeof(object), typeof(ListPageFrame), new PropertyMetadata(null));

    public static readonly DependencyProperty SearchToolbarContentProperty =
        DependencyProperty.Register(nameof(SearchToolbarContent), typeof(object), typeof(ListPageFrame), new PropertyMetadata(null));

    public static readonly DependencyProperty IsSearchToolbarVisibleProperty =
        DependencyProperty.Register(nameof(IsSearchToolbarVisible), typeof(bool), typeof(ListPageFrame), new PropertyMetadata(false));

    public static readonly DependencyProperty SearchToolbarContentTemplateProperty =
        DependencyProperty.Register(nameof(SearchToolbarContentTemplate), typeof(DataTemplate), typeof(ListPageFrame), new PropertyMetadata(null));

    public static readonly DependencyProperty SearchFilterContentProperty =
        DependencyProperty.Register(nameof(SearchFilterContent), typeof(object), typeof(ListPageFrame), new PropertyMetadata(null));

    public static readonly DependencyProperty IsSearchFilterVisibleProperty =
        DependencyProperty.Register(nameof(IsSearchFilterVisible), typeof(bool), typeof(ListPageFrame), new PropertyMetadata(false));

    public static readonly DependencyProperty SearchFilterContentTemplateProperty =
        DependencyProperty.Register(nameof(SearchFilterContentTemplate), typeof(DataTemplate), typeof(ListPageFrame), new PropertyMetadata(null));

    public static readonly DependencyProperty IsListVisibleProperty =
        DependencyProperty.Register(nameof(IsListVisible), typeof(bool), typeof(ListPageFrame), new PropertyMetadata(true));

    public static readonly DependencyProperty UseFrameScrollViewerProperty =
        DependencyProperty.Register(nameof(UseFrameScrollViewer), typeof(bool), typeof(ListPageFrame), new PropertyMetadata(true));

    public static readonly DependencyProperty OverlayContentProperty =
        DependencyProperty.Register(nameof(OverlayContent), typeof(object), typeof(ListPageFrame), new PropertyMetadata(null));

    public static readonly DependencyProperty ListContentProperty =
        DependencyProperty.Register(nameof(ListContent), typeof(object), typeof(ListPageFrame), new PropertyMetadata(null));

    public static readonly DependencyProperty FloatingContentProperty =
        DependencyProperty.Register(nameof(FloatingContent), typeof(object), typeof(ListPageFrame), new PropertyMetadata(null));

    public ListPageFrame()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public ImageSource? TitleIconSource
    {
        get => (ImageSource?)GetValue(TitleIconSourceProperty);
        set => SetValue(TitleIconSourceProperty, value);
    }

    public bool IsHeaderBackButtonVisible
    {
        get => (bool)GetValue(IsHeaderBackButtonVisibleProperty);
        set => SetValue(IsHeaderBackButtonVisibleProperty, value);
    }

    public ICommand? HeaderBackCommand
    {
        get => (ICommand?)GetValue(HeaderBackCommandProperty);
        set => SetValue(HeaderBackCommandProperty, value);
    }

    public string SearchText
    {
        get => (string)GetValue(SearchTextProperty);
        set => SetValue(SearchTextProperty, value);
    }

    public bool IsSearchVisible
    {
        get => (bool)GetValue(IsSearchVisibleProperty);
        set => SetValue(IsSearchVisibleProperty, value);
    }

    public object? SearchTrailingContent
    {
        get => GetValue(SearchTrailingContentProperty);
        set => SetValue(SearchTrailingContentProperty, value);
    }

    public object? SearchToolbarContent
    {
        get => GetValue(SearchToolbarContentProperty);
        set => SetValue(SearchToolbarContentProperty, value);
    }

    public bool IsSearchToolbarVisible
    {
        get => (bool)GetValue(IsSearchToolbarVisibleProperty);
        set => SetValue(IsSearchToolbarVisibleProperty, value);
    }

    public DataTemplate? SearchToolbarContentTemplate
    {
        get => (DataTemplate?)GetValue(SearchToolbarContentTemplateProperty);
        set => SetValue(SearchToolbarContentTemplateProperty, value);
    }

    public object? SearchFilterContent
    {
        get => GetValue(SearchFilterContentProperty);
        set => SetValue(SearchFilterContentProperty, value);
    }

    public bool IsSearchFilterVisible
    {
        get => (bool)GetValue(IsSearchFilterVisibleProperty);
        set => SetValue(IsSearchFilterVisibleProperty, value);
    }

    public DataTemplate? SearchFilterContentTemplate
    {
        get => (DataTemplate?)GetValue(SearchFilterContentTemplateProperty);
        set => SetValue(SearchFilterContentTemplateProperty, value);
    }

    public bool IsListVisible
    {
        get => (bool)GetValue(IsListVisibleProperty);
        set => SetValue(IsListVisibleProperty, value);
    }

    public bool UseFrameScrollViewer
    {
        get => (bool)GetValue(UseFrameScrollViewerProperty);
        set => SetValue(UseFrameScrollViewerProperty, value);
    }

    public object? OverlayContent
    {
        get => GetValue(OverlayContentProperty);
        set => SetValue(OverlayContentProperty, value);
    }

    public object? ListContent
    {
        get => GetValue(ListContentProperty);
        set => SetValue(ListContentProperty, value);
    }

    public object? FloatingContent
    {
        get => GetValue(FloatingContentProperty);
        set => SetValue(FloatingContentProperty, value);
    }

    public ScrollViewer ScrollViewer => PART_ScrollViewer;

    internal FrameworkElement ListLayerElement => PART_ListLayer;

    internal FrameworkElement HeaderOverlayElement => PART_HeaderOverlay;

    internal FrameworkElement HeaderTitleRowElement => PART_HeaderTitleRow;
}
