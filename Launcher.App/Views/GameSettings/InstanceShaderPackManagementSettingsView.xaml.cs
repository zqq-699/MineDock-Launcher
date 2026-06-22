using System.Windows;
using System.Windows.Controls;

namespace Launcher.App.Views.GameSettings;

public partial class InstanceShaderPackManagementSettingsView : UserControl
{
    public InstanceShaderPackManagementSettingsView()
    {
        InitializeComponent();
    }

    internal FrameworkElement OriginalShaderPackListHeaderElement => OriginalShaderPackListHeaderHost;

    internal FrameworkElement ShaderPackListSectionElement => ShaderPackListSection;

    internal DataTemplate ShaderPackListHeaderTemplate => (DataTemplate)Resources["ShaderPackListSectionHeaderTemplate"];
}
