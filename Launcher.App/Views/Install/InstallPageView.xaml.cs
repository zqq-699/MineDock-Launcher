using System.Windows;
using System.Windows.Controls;

namespace Launcher.App.Views.Install;

public partial class InstallPageView : UserControl
{
    public InstallPageView()
    {
        InitializeComponent();
    }

    public FrameworkElement RootElement => PageRoot;
}

