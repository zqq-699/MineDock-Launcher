using System.Windows;

namespace Launcher.App.Services;

public static class AcrylicWindow
{
    public static void Enable(Window window, IThemeService themeService)
    {
        const NativeBackdrop.DwmSystemBackdropType backdropType = NativeBackdrop.DwmSystemBackdropType.TransientWindow;
        NativeBackdrop.Enable(window, backdropType, themeService.EffectiveTheme);

        EventHandler<EffectiveThemeChangedEventArgs>? handler = null;
        handler = (_, args) =>
        {
            if (!window.Dispatcher.CheckAccess())
            {
                window.Dispatcher.Invoke(() => NativeBackdrop.ApplyToWindow(window, backdropType, args.NewTheme));
                return;
            }

            NativeBackdrop.ApplyToWindow(window, backdropType, args.NewTheme);
        };

        themeService.EffectiveThemeChanged += handler;
        window.Closed += (_, _) =>
        {
            if (handler is not null)
                themeService.EffectiveThemeChanged -= handler;
        };
    }
}
