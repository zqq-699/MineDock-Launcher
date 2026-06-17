using System.Windows;
using System.Windows.Controls;

namespace Launcher.App.Views.Settings;

public partial class SettingsPageView : UserControl
{
    public SettingsPageView()
    {
        InitializeComponent();
    }

    public FrameworkElement RootElement => PageRoot;
}
