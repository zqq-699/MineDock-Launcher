using System.Windows.Controls;

namespace Launcher.App.Views.Account;

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


