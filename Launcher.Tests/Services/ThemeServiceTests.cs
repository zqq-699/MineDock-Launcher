using System.Windows;
using Launcher.App.Services;

namespace Launcher.Tests.Services;

public sealed class ThemeServiceTests
{
    [Fact]
    public void ApplyPreferenceReplacesRuntimeThemeDictionary()
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                var application = global::System.Windows.Application.Current ?? new global::System.Windows.Application();
                application.Resources.MergedDictionaries.Clear();
                application.Resources.MergedDictionaries.Add(LoadDictionary("Resources/Themes/Shared.xaml"));

                using var themeService = new ThemeService(ImmediateUiDispatcher.Instance);
                var changedCount = 0;
                themeService.EffectiveThemeChanged += (_, _) => changedCount++;

                themeService.ApplyPreference("Dark", followSystem: false);
                Assert.Single(application.Resources.MergedDictionaries, IsDarkThemeDictionary);
                Assert.DoesNotContain(application.Resources.MergedDictionaries, IsLightThemeDictionary);
                Assert.Equal(0, changedCount);

                themeService.ApplyPreference("Light", followSystem: false);
                Assert.Single(application.Resources.MergedDictionaries, IsLightThemeDictionary);
                Assert.DoesNotContain(application.Resources.MergedDictionaries, IsDarkThemeDictionary);
                Assert.Equal(EffectiveTheme.Light, themeService.EffectiveTheme);
                Assert.Equal(1, changedCount);

                themeService.ApplyPreference("Light", followSystem: false);
                Assert.Single(application.Resources.MergedDictionaries, IsLightThemeDictionary);
                Assert.Equal(1, changedCount);
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

    private static ResourceDictionary LoadDictionary(string relativePath)
    {
        return new ResourceDictionary
        {
            Source = new Uri(
                $"pack://application:,,,/Launcher.App;component/{relativePath}",
                UriKind.Absolute)
        };
    }

    private static bool IsDarkThemeDictionary(ResourceDictionary dictionary)
    {
        return dictionary.Source?.ToString().EndsWith("/Resources/Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsLightThemeDictionary(ResourceDictionary dictionary)
    {
        return dictionary.Source?.ToString().EndsWith("/Resources/Themes/Light.xaml", StringComparison.OrdinalIgnoreCase) == true;
    }
}
