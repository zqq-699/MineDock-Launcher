using System.Windows;

namespace Launcher.App.Services;

public sealed class WindowService : IWindowService
{
    private Window? window;

    public void Attach(Window window)
    {
        this.window = window;
    }

    public void Minimize()
    {
        if (window is not null)
            window.WindowState = WindowState.Minimized;
    }

    public void Close()
    {
        window?.Close();
    }
}
