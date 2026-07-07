using System.Windows;
using System.Windows.Controls;

namespace Launcher.App.Views.Settings;

public partial class InfoSettingsView : UserControl
{
    public InfoSettingsView()
    {
        InitializeComponent();
    }

    private void CheckUpdatesButton_OnRequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        e.Handled = true;
    }
}
