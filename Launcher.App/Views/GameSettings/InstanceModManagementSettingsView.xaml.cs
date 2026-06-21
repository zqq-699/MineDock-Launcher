using System.Windows;
using System.Windows.Controls;

namespace Launcher.App.Views.GameSettings;

public partial class InstanceModManagementSettingsView : UserControl
{
    public InstanceModManagementSettingsView()
    {
        InitializeComponent();
    }

    internal FrameworkElement OriginalModListHeaderElement => OriginalModListHeaderHost;

    internal FrameworkElement ModListSectionElement => ModListSection;

    internal DataTemplate ModListHeaderTemplate => (DataTemplate)Resources["ModListSectionHeaderTemplate"];
}
