using System.Windows;
using System.Windows.Controls;

namespace Launcher.App.Views;

public partial class AddAccountDialogView : UserControl
{
    public AddAccountDialogView()
    {
        InitializeComponent();
    }

    public event RoutedEventHandler? BackRequested;
    public event RoutedEventHandler? CancelRequested;
    public event RoutedEventHandler? ConfirmRequested;

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        BackRequested?.Invoke(this, e);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        CancelRequested?.Invoke(this, e);
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        ConfirmRequested?.Invoke(this, e);
    }
}
