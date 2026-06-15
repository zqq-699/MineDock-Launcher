using System.Windows;
using System.Windows.Controls;

namespace Launcher.App.Views.General;

public partial class GeneralPageView : UserControl
{
    public GeneralPageView()
    {
        InitializeComponent();
    }

    public FrameworkElement RootElement => PageRoot;
}

