using System.Windows;
using System.Windows.Controls;

namespace Launcher.App.Views.GameSettings;

public partial class InstanceResourcePackManagementSettingsView : UserControl
{
    public InstanceResourcePackManagementSettingsView()
    {
        InitializeComponent();
    }

    internal FrameworkElement OriginalResourcePackListHeaderElement => OriginalResourcePackListHeaderHost;

    internal FrameworkElement ResourcePackListSectionElement => ResourcePackListSection;

    internal DataTemplate ResourcePackListHeaderTemplate => (DataTemplate)Resources["ResourcePackListSectionHeaderTemplate"];
}
