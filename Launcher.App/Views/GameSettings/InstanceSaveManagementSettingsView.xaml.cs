using System.Windows;
using System.Windows.Controls;

namespace Launcher.App.Views.GameSettings;

public partial class InstanceSaveManagementSettingsView : UserControl
{
    public InstanceSaveManagementSettingsView()
    {
        InitializeComponent();
    }

    internal FrameworkElement OriginalSaveListHeaderElement => OriginalSaveListHeaderHost;

    internal FrameworkElement SaveListSectionElement => SaveListSection;

    internal DataTemplate SaveListHeaderTemplate => (DataTemplate)Resources["SaveListSectionHeaderTemplate"];
}
