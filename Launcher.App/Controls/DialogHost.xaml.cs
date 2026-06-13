using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace Launcher.App.Controls;

[ContentProperty(nameof(DialogContent))]
public partial class DialogHost : UserControl
{
    public static readonly DependencyProperty DialogWidthProperty =
        DependencyProperty.Register(nameof(DialogWidth), typeof(double), typeof(DialogHost), new PropertyMetadata(420d));

    public static readonly DependencyProperty DialogContentProperty =
        DependencyProperty.Register(nameof(DialogContent), typeof(object), typeof(DialogHost), new PropertyMetadata(null));

    public DialogHost()
    {
        InitializeComponent();
    }

    public double DialogWidth
    {
        get => (double)GetValue(DialogWidthProperty);
        set => SetValue(DialogWidthProperty, value);
    }

    public object? DialogContent
    {
        get => GetValue(DialogContentProperty);
        set => SetValue(DialogContentProperty, value);
    }

    public Grid OverlayRoot => RootOverlay;

    public Border SurfaceBorder => Surface;

    public Border BlurLayerBorder => BlurLayer;
}
