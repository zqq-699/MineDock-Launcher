using System.Windows;
using System.Windows.Controls;

namespace Launcher.App.Views;

public partial class DeleteAccountDialogView : UserControl
{
    public DeleteAccountDialogView()
    {
        InitializeComponent();
    }

    public event RoutedEventHandler? CancelRequested;
    public event RoutedEventHandler? ConfirmRequested;

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        CancelRequested?.Invoke(this, e);
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        ConfirmRequested?.Invoke(this, e);
    }
}
