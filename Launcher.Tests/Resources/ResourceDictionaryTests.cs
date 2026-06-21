using System.Windows;
using System.Windows.Media;
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
    public void ThemeResourceDictionariesLoadAtRuntime()
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                _ = global::System.Windows.Application.Current ?? new global::System.Windows.Application();
                var shared = LoadDictionary("Resources/Themes/Shared.xaml");
                var dark = LoadDictionary("Resources/Themes/Dark.xaml");
                var light = LoadDictionary("Resources/Themes/Light.xaml");

                Assert.NotNull(shared["LauncherFontFamily"]);
                Assert.NotNull(dark["Brush.Text.Primary"]);
                Assert.NotNull(light["Brush.Text.Primary"]);
                Assert.NotNull(dark["Brush.Icon.Primary"]);
                Assert.NotNull(light["Brush.Icon.Primary"]);
                Assert.Equal("#5A4A4A4A", ((SolidColorBrush)dark["Brush.SecondaryMenu.Panel"]).Color.ToString());
                Assert.Equal("#B3181818", ((SolidColorBrush)dark["Brush.Page.Background"]).Color.ToString());
                Assert.Equal("#E8FFFFFF", ((SolidColorBrush)light["Brush.SecondaryMenu.Panel"]).Color.ToString());
                Assert.Equal("#10000000", ((SolidColorBrush)light["Brush.SecondaryMenu.Border"]).Color.ToString());
                Assert.Equal("#08000000", ((Color)light["Color.SecondaryMenu.Shadow"]).ToString());
                Assert.Equal("#D9F3F4F6", ((SolidColorBrush)light["Brush.Page.Background"]).Color.ToString());
                Assert.Equal("#FFFFFFFF", ((SolidColorBrush)light["Brush.Card.Surface"]).Color.ToString());
                Assert.Equal("#FFF5F6F8", ((SolidColorBrush)light["Brush.Input.TextBox.Background"]).Color.ToString());
                Assert.Equal("#FFE9EAEC", ((SolidColorBrush)light["Brush.Input.ComboBox.Background"]).Color.ToString());
                Assert.Equal("#FFE9EAEC", ((SolidColorBrush)light["Brush.Button.Secondary.Background"]).Color.ToString());
                Assert.Equal("#FFF0F1F3", ((SolidColorBrush)light["Brush.Field.ReadOnly.Surface"]).Color.ToString());
                Assert.Equal("#FFE8EDF4", ((SolidColorBrush)light["Brush.List.Item.Selected"]).Color.ToString());
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
        application.Resources.MergedDictionaries.Add(LoadDictionary("Resources/Themes/Shared.xaml"));
        application.Resources.MergedDictionaries.Add(LoadDictionary("Resources/Themes/Dark.xaml"));
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/Launcher.App;component/Styles/ControlStyles.xaml",
                UriKind.Absolute)
        });
        application.Resources["BooleanToMenuTextVisibilityConverter"] = new BooleanToMenuTextVisibilityConverter();
        application.Resources["SkinActiveStateVisibilityConverter"] = new SkinActiveStateVisibilityConverter();
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
}
