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

using System.Reflection;
using System.Windows;
using WpfApplication = System.Windows.Application;

namespace Launcher.Tests.Helpers;

internal static class WpfApplicationTestHelper
{
    public static WpfApplication GetOrCreateApplication()
    {
        var application = WpfApplication.Current;
        if (application is not null && !application.Dispatcher.CheckAccess())
        {
            ResetApplicationStatics();
            application = null;
        }

        application ??= new WpfApplication();
        application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        return application;
    }

    public static void ShutdownAndResetCurrentApplication()
    {
        var application = WpfApplication.Current;
        if (application is not null && application.Dispatcher.CheckAccess())
        {
            try
            {
                application.Shutdown();
            }
            catch (InvalidOperationException)
            {
            }
        }

        ResetApplicationStatics();
    }

    private static void ResetApplicationStatics()
    {
        SetStaticField("_appInstance", null);
        SetStaticField("_appCreatedInThisAppDomain", false);
        SetStaticField("_isShuttingDown", false);
    }

    private static void SetStaticField(string name, object? value)
    {
        typeof(WpfApplication)
            .GetField(name, BindingFlags.Static | BindingFlags.NonPublic)
            ?.SetValue(null, value);
    }
}
