using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Launcher.App.Controls;

namespace Launcher.Tests.Controls;

public sealed class SvgIconTests
{
    [Fact]
    public void SvgIconDoesNotAssignDefaultStrokeResource()
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                var icon = new SvgIcon();

                Assert.Null(icon.Stroke);
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            throw exception;
    }

    [Fact]
    public void DeleteButtonStyledSvgIconUsesDangerStyleForeground()
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                var expectedBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF2F2F"));
                var buttonStyle = new Style(typeof(Button));
                buttonStyle.Setters.Add(new Setter(Control.ForegroundProperty, expectedBrush));

                var iconStyle = new Style(typeof(SvgIcon));
                iconStyle.Setters.Add(new Setter(
                    Control.ForegroundProperty,
                    new Binding("Foreground")
                    {
                        RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1)
                    }));

                var button = new Button
                {
                    Style = buttonStyle
                };

                var icon = new SvgIcon
                {
                    Width = 16,
                    Height = 16,
                    IconKey = "general/general_delete",
                    Style = iconStyle
                };

                button.Content = icon;

                var window = new Window
                {
                    Width = 80,
                    Height = 80,
                    Content = button,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Opacity = 0
                };

                window.Show();
                window.UpdateLayout();
                button.ApplyTemplate();
                button.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

                Assert.Equal("#FFFF2F2F", ((SolidColorBrush)button.Foreground).Color.ToString());
                Assert.Equal("#FFFF2F2F", ((SolidColorBrush)icon.Foreground).Color.ToString());
                Assert.Null(icon.Stroke);

                window.Close();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            throw exception;
    }

}
