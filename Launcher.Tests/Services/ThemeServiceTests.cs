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
                var backgroundBlurDisabledChangedCount = 0;
                themeService.EffectiveThemeChanged += (_, _) => changedCount++;
                themeService.BackgroundBlurDisabledChanged += (_, _) => backgroundBlurDisabledChangedCount++;

                themeService.ApplyPreference(
                    "Dark",
                    followSystem: false,
                    backgroundOpacityPercent: 85,
                    disableBackgroundBlur: false);
                Assert.Single(application.Resources.MergedDictionaries, IsDarkThemeDictionary);
                Assert.DoesNotContain(application.Resources.MergedDictionaries, IsLightThemeDictionary);
                Assert.Equal(0.85d, Assert.IsType<double>(application.Resources["Opacity.Page.Background"]), 3);
                Assert.True(Assert.IsType<bool>(application.Resources["Is.BackdropBlur.Enabled"]));
                Assert.Equal(0, changedCount);
                Assert.Equal(0, backgroundBlurDisabledChangedCount);

                themeService.ApplyPreference(
                    "Light",
                    followSystem: false,
                    backgroundOpacityPercent: 65,
                    disableBackgroundBlur: true);
                Assert.Single(application.Resources.MergedDictionaries, IsLightThemeDictionary);
                Assert.DoesNotContain(application.Resources.MergedDictionaries, IsDarkThemeDictionary);
                Assert.Equal(EffectiveTheme.Light, themeService.EffectiveTheme);
                Assert.Equal(0.65d, Assert.IsType<double>(application.Resources["Opacity.Page.Background"]), 3);
                Assert.False(Assert.IsType<bool>(application.Resources["Is.BackdropBlur.Enabled"]));
                Assert.Equal(1, changedCount);
                Assert.Equal(1, backgroundBlurDisabledChangedCount);

                themeService.ApplyPreference(
                    "Light",
                    followSystem: false,
                    backgroundOpacityPercent: 65,
                    disableBackgroundBlur: true);
                Assert.Single(application.Resources.MergedDictionaries, IsLightThemeDictionary);
                Assert.Equal(1, changedCount);

                themeService.ApplyBackgroundOpacity(42);
                Assert.Equal(0.42d, Assert.IsType<double>(application.Resources["Opacity.Page.Background"]), 3);
                Assert.Equal(1, changedCount);

                themeService.ApplyBackgroundBlurDisabled(false);
                Assert.True(Assert.IsType<bool>(application.Resources["Is.BackdropBlur.Enabled"]));
                Assert.Equal(1, changedCount);
                Assert.Equal(2, backgroundBlurDisabledChangedCount);

                themeService.ApplyBackgroundBlurDisabled(false);
                Assert.Equal(1, changedCount);
                Assert.Equal(2, backgroundBlurDisabledChangedCount);
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
