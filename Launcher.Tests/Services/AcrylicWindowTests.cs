using System.Windows;
using System.Windows.Media;
using System.Windows.Shell;
using Launcher.App.Services;

namespace Launcher.Tests.Services;

public sealed class AcrylicWindowTests
{
    [Fact]
    public void BackgroundBlurToggleUpdatesWindowGlassFrame()
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new Window
                {
                    AllowsTransparency = false,
                    ResizeMode = ResizeMode.CanResize,
                    WindowStyle = WindowStyle.SingleBorderWindow
                };
                var chrome = new WindowChrome
                {
                    GlassFrameThickness = new Thickness(-1)
                };
                WindowChrome.SetWindowChrome(window, chrome);

                var themeService = new FakeThemeService();
                AcrylicWindow.Enable(window, themeService);

                Assert.Equal(new Thickness(-1), chrome.GlassFrameThickness);
                Assert.Same(Brushes.Transparent, window.Background);
                Assert.Equal(WindowStyle.SingleBorderWindow, window.WindowStyle);

                themeService.ApplyBackgroundBlurDisabled(true);

                Assert.Equal(new Thickness(0), chrome.GlassFrameThickness);
                Assert.NotSame(Brushes.Transparent, window.Background);

                themeService.ApplyBackgroundBlurDisabled(false);

                Assert.Equal(new Thickness(-1), chrome.GlassFrameThickness);
                Assert.Same(Brushes.Transparent, window.Background);
                Assert.Equal(WindowStyle.SingleBorderWindow, window.WindowStyle);
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
