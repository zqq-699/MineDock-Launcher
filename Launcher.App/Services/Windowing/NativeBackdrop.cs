using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;

namespace Launcher.App.Services;

internal static class NativeBackdrop
{
    public static void Enable(Window window, DwmSystemBackdropType backdropType)
    {
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
                return;

            window.Background = Brushes.Transparent;

            var source = HwndSource.FromHwnd(handle);
            if (source?.CompositionTarget is not null)
                source.CompositionTarget.BackgroundColor = Colors.Transparent;

            TryApply(handle, backdropType);
        };
    }

    public static bool TryApplyToPopup(Popup popup, DwmSystemBackdropType backdropType)
    {
        if (popup.Child is null)
            return false;

        if (PresentationSource.FromVisual(popup.Child) is not HwndSource source)
            return false;

        if (source.CompositionTarget is not null)
            source.CompositionTarget.BackgroundColor = Colors.Transparent;

        return TryApply(source.Handle, backdropType);
    }

    public static bool TryApply(IntPtr handle, DwmSystemBackdropType backdropType)
    {
        if (handle == IntPtr.Zero)
            return false;

        try
        {
            var margins = new Margins
            {
                Left = -1,
                Right = -1,
                Top = -1,
                Bottom = -1
            };
            _ = DwmExtendFrameIntoClientArea(handle, ref margins);

            var darkMode = 1;
            _ = DwmSetWindowAttribute(handle, DwmWindowAttribute.UseImmersiveDarkMode, ref darkMode, sizeof(int));

            var cornerPreference = (int)DwmWindowCornerPreference.Round;
            _ = DwmSetWindowAttribute(handle, DwmWindowAttribute.WindowCornerPreference, ref cornerPreference, sizeof(int));

            var borderColorNone = unchecked((int)0xFFFFFFFE);
            _ = DwmSetWindowAttribute(handle, DwmWindowAttribute.BorderColor, ref borderColorNone, sizeof(int));

            var backdrop = (int)backdropType;
            return DwmSetWindowAttribute(handle, DwmWindowAttribute.SystemBackdropType, ref backdrop, sizeof(int)) == 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        catch (COMException)
        {
            return false;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, DwmWindowAttribute attribute, ref int attributeValue, int attributeSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    private enum DwmWindowAttribute
    {
        UseImmersiveDarkMode = 20,
        WindowCornerPreference = 33,
        BorderColor = 34,
        SystemBackdropType = 38
    }

    private enum DwmWindowCornerPreference
    {
        Round = 2
    }

    internal enum DwmSystemBackdropType
    {
        None = 1,
        MainWindow = 2,
        TransientWindow = 3,
        TabbedWindow = 4
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }
}
