using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Launcher.App.Views.GameSettings;

public partial class InstanceGeneralSettingsView : UserControl
{
    public InstanceGeneralSettingsView()
    {
        InitializeComponent();
    }

    private void DescriptionTextBox_OnLoaded(object sender, RoutedEventArgs e)
    {
        QueueDescriptionTextBoxHeightUpdate();
    }

    private void DescriptionTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        QueueDescriptionTextBoxHeightUpdate();
    }

    private void DescriptionTextBox_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged)
            QueueDescriptionTextBoxHeightUpdate();
    }

    private void QueueDescriptionTextBoxHeightUpdate()
    {
        Dispatcher.BeginInvoke(UpdateDescriptionTextBoxHeight, DispatcherPriority.Background);
    }

    private void UpdateDescriptionTextBoxHeight()
    {
        DescriptionTextBox.UpdateLayout();
        var lineCount = Math.Max(1, DescriptionTextBox.LineCount);
        DescriptionTextBox.VerticalContentAlignment = lineCount > 1 ? VerticalAlignment.Top : VerticalAlignment.Center;
        var lineHeight = TextBlock.GetLineHeight(DescriptionTextBox);
        if (double.IsNaN(lineHeight) || lineHeight <= 0)
        {
            lineHeight = Math.Max(
                DescriptionTextBox.FontFamily.LineSpacing * DescriptionTextBox.FontSize,
                DescriptionTextBox.FontSize * 1.35);
        }
        var chromeAllowance = 16d;
        DescriptionTextBox.Height = Math.Max(34, Math.Ceiling((lineCount * lineHeight) + chromeAllowance));
    }
}
