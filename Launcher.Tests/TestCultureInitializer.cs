using System.Globalization;
using System.Runtime.CompilerServices;

namespace Launcher.Tests;

internal static class TestCultureInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var defaultCulture = new CultureInfo("zh-Hans");
        CultureInfo.DefaultThreadCurrentCulture = defaultCulture;
        CultureInfo.DefaultThreadCurrentUICulture = defaultCulture;
        CultureInfo.CurrentCulture = defaultCulture;
        CultureInfo.CurrentUICulture = defaultCulture;
    }
}
