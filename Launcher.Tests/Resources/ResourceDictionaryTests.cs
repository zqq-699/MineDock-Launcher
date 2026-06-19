using System.Windows;
using Launcher.App.Controls;
using Launcher.App.Converters;
using Launcher.App.Views.Account.Dialogs;
using Launcher.Application.Accounts;

namespace Launcher.Tests.Resources;

public sealed class ResourceDictionaryTests
{
    [Fact]
    public void ControlStylesResourceDictionaryLoadsAtRuntime()
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                _ = global::System.Windows.Application.Current;
                var dictionary = new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/Launcher.App;component/Styles/ControlStyles.xaml",
                        UriKind.Absolute)
                };

                Assert.NotNull(dictionary["NavigationMenuButtonStyle"]);
                Assert.NotNull(dictionary["MainContentScrollViewerStyle"]);
                Assert.NotNull(dictionary["DownloadVersionListBoxStyle"]);
                Assert.NotNull(dictionary["ListPageItemButtonEntranceStyle"]);
                Assert.NotNull(dictionary["LauncherDialogButtonStyle"]);
                Assert.NotNull(dictionary["LauncherComboBoxStyle"]);
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

    [Fact]
    public void LauncherComboBoxStyleAppliesTemplateAtRuntime()
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                _ = global::System.Windows.Application.Current;
                var dictionary = new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/Launcher.App;component/Styles/ControlStyles.xaml",
                        UriKind.Absolute)
                };

                var comboBox = new AnimatedComboBox
                {
                    Style = (Style)dictionary["LauncherComboBoxStyle"],
                    Width = 220,
                    Height = 36,
                    ItemsSource = new[]
                    {
                        new AccountCapeOption { DisplayName = "None", IsNone = true }
                    }
                };

                comboBox.ApplyTemplate();
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

    [Fact]
    public void SkinManagerDialogViewInitializesRuntimeContent()
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                var application = global::System.Windows.Application.Current ?? new global::System.Windows.Application();
                EnsureApplicationResources(application);
                var view = new SkinManagerDialogView();
                view.ApplyTemplate();

                Assert.True(view.MinHeight > 0);
                Assert.NotNull(view.Content);
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

    private static void EnsureApplicationResources(global::System.Windows.Application application)
    {
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/Launcher.App;component/Resources/ThemeResources.xaml",
                UriKind.Absolute)
        });
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/Launcher.App;component/Styles/ControlStyles.xaml",
                UriKind.Absolute)
        });
        application.Resources["BooleanToMenuTextVisibilityConverter"] = new BooleanToMenuTextVisibilityConverter();
        application.Resources["SkinActiveStateVisibilityConverter"] = new SkinActiveStateVisibilityConverter();
    }
}
