using System.Windows;
using System.Windows.Controls;

namespace Launcher.App.Views;

public partial class HomePageView : UserControl
{
    public HomePageView()
    {
        InitializeComponent();
    }

    public FrameworkElement RootElement => PageRoot;
}
