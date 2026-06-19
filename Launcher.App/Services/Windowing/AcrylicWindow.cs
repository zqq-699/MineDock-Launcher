using System.Windows;

namespace Launcher.App.Services;

public static class AcrylicWindow
{
    public static void Enable(Window window)
    {
        NativeBackdrop.Enable(window, NativeBackdrop.DwmSystemBackdropType.TransientWindow);
    }
}
