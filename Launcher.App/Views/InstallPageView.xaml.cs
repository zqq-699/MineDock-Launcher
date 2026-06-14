using System.Windows;
using System.Windows.Controls;

namespace Launcher.App.Views;

public partial class InstallPageView : UserControl
{
    public InstallPageView()
    {
        InitializeComponent();
    }

    public FrameworkElement RootElement => PageRoot;
}
