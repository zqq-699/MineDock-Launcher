using System.Windows;
using System.Windows.Controls;

namespace Launcher.App.Controls;

public partial class SecondaryMenuFrame : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(SecondaryMenuFrame), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty MenuContentProperty =
        DependencyProperty.Register(nameof(MenuContent), typeof(object), typeof(SecondaryMenuFrame), new PropertyMetadata(null));

    public SecondaryMenuFrame()
    {
        InitializeComponent();
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
}
