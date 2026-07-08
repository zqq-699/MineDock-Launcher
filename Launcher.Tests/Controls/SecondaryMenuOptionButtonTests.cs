using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Launcher.App.Behaviors;
using Launcher.App.Controls;
using Launcher.Tests.Helpers;

namespace Launcher.Tests.Controls;

[Collection(Launcher.Tests.WpfTestCollection.Name)]
public sealed class SecondaryMenuOptionButtonTests
{
    [Fact]
    public void SecondaryMenuOptionButtonUpdatesSelectedBackgroundFromIsSelected()
    {
        RunOnSta(() =>
        {
            Window? window = null;
            try
            {
                var application = WpfApplicationTestHelper.GetOrCreateApplication();
                EnsureApplicationResources(application);
                var optionButton = new SecondaryMenuOptionButton
                {
                    Text = "Mods",
                    IsSelected = true,
                    Width = 240
                };
                window = CreateHiddenWindow(optionButton);

                window.Show();
                PumpDispatcher(DispatcherPriority.ApplicationIdle);

                var button = Assert.IsType<Button>(FindVisualDescendant<Button>(optionButton));
                Assert.True(SecondaryMenuButtonBehavior.GetIsSelected(button));
                var selectedBackground = Assert.IsType<Border>(
                    button.Template.FindName("SelectedBackground", button));
                Assert.Equal(1d, selectedBackground.Opacity);

                optionButton.IsSelected = false;
                PumpDispatcher(DispatcherPriority.ApplicationIdle);

                Assert.Equal(0d, selectedBackground.Opacity);
            }
            finally
            {
                window?.Close();
            }
        });
    }

    private static Window CreateHiddenWindow(FrameworkElement content)
    {
        return new Window
        {
            Width = 320,
            Height = 120,
            Content = content,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Opacity = 0
        };
    }

    private static void EnsureApplicationResources(System.Windows.Application application)
    {
        AddDictionaryIfMissing(application, "Resources/Themes/Shared.xaml");
        AddDictionaryIfMissing(application, "Resources/Themes/Dark.xaml");
        AddDictionaryIfMissing(application, "Resources/Themes/Accents/Blue.xaml");
        AddDictionaryIfMissing(application, "Styles/ControlStyles.xaml");
    }

    private static void AddDictionaryIfMissing(System.Windows.Application application, string relativePath)
    {
        var source = new Uri($"pack://application:,,,/MineDock_Launcher_x64;component/{relativePath}", UriKind.Absolute);
        if (application.Resources.MergedDictionaries.Any(dictionary => dictionary.Source == source))
            return;

        application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = source });
    }

    private static void RunOnSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
            finally
            {
                WpfApplicationTestHelper.ShutdownAndResetCurrentApplication();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            throw exception;
    }

    private static void PumpDispatcher(DispatcherPriority priority)
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            priority,
            new DispatcherOperationCallback(_ =>
            {
                frame.Continue = false;
                return null;
            }),
            null);
        Dispatcher.PushFrame(frame);
    }

    private static T? FindVisualDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                return match;

            var result = FindVisualDescendant<T>(child);
            if (result is not null)
                return result;
        }

        return null;
    }
}
