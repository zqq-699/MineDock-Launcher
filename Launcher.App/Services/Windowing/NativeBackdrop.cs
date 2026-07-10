/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;

namespace Launcher.App.Services;

internal static class NativeBackdrop
{
    public static void Enable(Window window, DwmSystemBackdropType backdropType, EffectiveTheme theme)
    {
        window.SourceInitialized += (_, _) =>
        {
            ApplyToWindow(window, backdropType, theme);
        };
    }

    public static bool ApplyToWindow(
        Window window,
        DwmSystemBackdropType backdropType,
        EffectiveTheme theme,
        bool extendIntoClientArea = true)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return false;

        var source = HwndSource.FromHwnd(handle);
        if (source?.CompositionTarget is not null)
        {
            source.CompositionTarget.BackgroundColor = extendIntoClientArea
                ? Colors.Transparent
                : GetOpaqueWindowBackgroundColor(theme);
        }

        return TryApply(handle, backdropType, theme, extendIntoClientArea);
    }

    public static bool TryApplyToPopup(Popup popup, DwmSystemBackdropType backdropType, EffectiveTheme theme)
    {
        if (popup.Child is null)
            return false;

        if (PresentationSource.FromVisual(popup.Child) is not HwndSource source)
            return false;

        if (source.CompositionTarget is not null)
            source.CompositionTarget.BackgroundColor = Colors.Transparent;

        return TryApply(source.Handle, backdropType, theme);
    }

    public static bool TryApply(
        IntPtr handle,
        DwmSystemBackdropType backdropType,
        EffectiveTheme theme,
        bool extendIntoClientArea = true)
    {
        if (handle == IntPtr.Zero)
            return false;

        try
        {
            var margins = new Margins
            {
                Left = extendIntoClientArea ? -1 : 0,
                Right = extendIntoClientArea ? -1 : 0,
                Top = extendIntoClientArea ? -1 : 0,
                Bottom = extendIntoClientArea ? -1 : 0
            };
            _ = DwmExtendFrameIntoClientArea(handle, ref margins);

            var darkMode = theme is EffectiveTheme.Dark ? 1 : 0;
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

    public static Color GetOpaqueWindowBackgroundColor(EffectiveTheme theme)
    {
        return theme is EffectiveTheme.Light
            ? Colors.White
            : Color.FromRgb(0x15, 0x15, 0x15);
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
