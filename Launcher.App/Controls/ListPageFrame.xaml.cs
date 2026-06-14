using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Launcher.App.Controls;

public partial class ListPageFrame : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(ListPageFrame), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TitleIconSourceProperty =
        DependencyProperty.Register(nameof(TitleIconSource), typeof(ImageSource), typeof(ListPageFrame), new PropertyMetadata(null));

    public static readonly DependencyProperty SearchTextProperty =
        DependencyProperty.Register(
            nameof(SearchText),
            typeof(string),
            typeof(ListPageFrame),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty IsSearchVisibleProperty =
        DependencyProperty.Register(nameof(IsSearchVisible), typeof(bool), typeof(ListPageFrame), new PropertyMetadata(true));

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
}
