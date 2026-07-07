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
