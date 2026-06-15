using System.Windows;
using System.Windows.Controls;

namespace Launcher.App.Controls;

public partial class SecondaryMenuFrame : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(SecondaryMenuFrame), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty MenuContentProperty =
        DependencyProperty.Register(
            nameof(MenuContent),
            typeof(object),
            typeof(SecondaryMenuFrame),
            new PropertyMetadata(null, OnContentHostPropertyChanged));

    public static readonly DependencyProperty UseInternalScrollViewerProperty =
        DependencyProperty.Register(
            nameof(UseInternalScrollViewer),
            typeof(bool),
            typeof(SecondaryMenuFrame),
            new PropertyMetadata(true, OnContentHostPropertyChanged));

    public SecondaryMenuFrame()
    {
        InitializeComponent();
        UpdateContentHost();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public object? MenuContent
    {
        get => GetValue(MenuContentProperty);
        set => SetValue(MenuContentProperty, value);
    }

    public bool UseInternalScrollViewer
    {
        get => (bool)GetValue(UseInternalScrollViewerProperty);
        set => SetValue(UseInternalScrollViewerProperty, value);
    }

    private static void OnContentHostPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((SecondaryMenuFrame)d).UpdateContentHost();
    }

    private void UpdateContentHost()
    {
        if (ScrollableContentHost is null || DirectContentHost is null)
            return;

        if (UseInternalScrollViewer)
        {
            DirectContentHost.Content = null;
            ScrollableContentHost.Content = MenuContent;
        }
        else
        {
            ScrollableContentHost.Content = null;
            DirectContentHost.Content = MenuContent;
        }
    }
}
