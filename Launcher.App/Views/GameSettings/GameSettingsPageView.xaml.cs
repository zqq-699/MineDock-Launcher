using System.Windows;
using System.Windows.Controls;

namespace Launcher.App.Views.GameSettings;

public partial class GameSettingsPageView : UserControl
{
    public GameSettingsPageView()
    {
        InitializeComponent();
    }

    public FrameworkElement RootElement => PageRoot;
}

