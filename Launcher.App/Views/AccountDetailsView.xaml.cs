using System.Windows.Controls;

namespace Launcher.App.Views;

public partial class AccountDetailsView : UserControl
{
    public AccountDetailsView()
    {
        InitializeComponent();
    }

    public void ScrollToTop()
    {
        AccountDetailsScrollViewer.ScrollToTop();
    }
}

