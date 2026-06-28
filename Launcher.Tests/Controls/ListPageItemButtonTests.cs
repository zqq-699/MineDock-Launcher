using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Launcher.App.Behaviors;
using Launcher.App.Controls;

namespace Launcher.Tests.Controls;

[Collection(Launcher.Tests.WpfTestCollection.Name)]
public sealed class ListPageItemButtonTests
{
    [Fact]
    public void EnterAnimationPendingDefaultsToFalse()
    {
        var metadata = ListPageItemButton.IsEnterAnimationPendingProperty.GetMetadata(typeof(ListPageItemButton));

        Assert.False((bool)metadata.DefaultValue);
    }

    [Fact]
    public void EntranceStyleHoldsItemPendingUntilBehaviorPublishesState()
    {
        _ = global::System.Windows.Application.Current;
        var dictionary = new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/Launcher.App;component/Styles/ControlStyles.xaml",
                UriKind.Absolute)
        };

        var style = Assert.IsType<Style>(dictionary["ListPageItemButtonEntranceStyle"]);
        var pendingSetter = style.Setters
            .OfType<Setter>()
            .FirstOrDefault(setter => setter.Property == ListPageItemButton.IsEnterAnimationPendingProperty);

        Assert.NotNull(pendingSetter);
        Assert.True((bool)pendingSetter.Value);
    }

    [Fact]
    public void IconSourceImageLoaderLoadsLocalFileUri()
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), $"launcher-list-icon-{Guid.NewGuid():N}.png");
            try
            {
                WritePng(path);

                Assert.NotNull(IconSourceImageLoader.TryLoad(new Uri(path).AbsoluteUri));
                Assert.Null(IconSourceImageLoader.TryLoad(null));
            }
            catch (Exception ex)
            {
                exception = ex;
            }
            finally
            {
                File.Delete(path);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            throw exception;
    }

    [Fact]
    public void IconSourceImageLoaderLoadsAppResourcePath()
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                _ = global::System.Windows.Application.Current;
                Assert.NotNull(IconSourceImageLoader.TryLoad("/Assets/Icons/block/fabric.png"));
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
    public void IconSourceImageLoaderReusesCachedLocalFileImage()
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), $"launcher-list-icon-{Guid.NewGuid():N}.png");
            try
            {
                WritePng(path);
                var uri = new Uri(path).AbsoluteUri;

                var first = IconSourceImageLoader.TryLoad(uri);
                var second = IconSourceImageLoader.TryLoad(uri);

                Assert.NotNull(first);
                Assert.Same(first, second);
            }
            catch (Exception ex)
            {
                exception = ex;
            }
            finally
            {
                File.Delete(path);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            throw exception;
    }

    [Fact]
    public void IconSourceImageLoaderReloadsWhenLocalFileChanges()
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), $"launcher-list-icon-{Guid.NewGuid():N}.png");
            try
            {
                WritePng(path);
                var uri = new Uri(path).AbsoluteUri;
                var first = IconSourceImageLoader.TryLoad(uri);

                Thread.Sleep(20);
                WritePng(path, Colors.OrangeRed);

                var second = IconSourceImageLoader.TryLoad(uri);

                Assert.NotNull(first);
                Assert.NotNull(second);
                Assert.NotSame(first, second);
            }
            catch (Exception ex)
            {
                exception = ex;
            }
            finally
            {
                File.Delete(path);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            throw exception;
    }

    [Fact]
    public void IconSourceImageLoaderAcceptsExistingImageSource()
    {
        var bitmap = BitmapSource.Create(
            1,
            1,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            new byte[] { 0x1f, 0x77, 0xb4, 0xff },
            4);

        Assert.Same(bitmap, IconSourceImageLoader.TryLoad(bitmap));
    }

    [Fact]
    public void ConsumingEnterAnimationDoesNotLeavePendingLocalValue()
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var dictionary = new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/Launcher.App;component/Styles/ControlStyles.xaml",
                        UriKind.Absolute)
                };

                var state = new VirtualizedListItemState
                {
                    IsEntranceAnimationPending = true
                };
                var button = new ListPageItemButton
                {
                    Style = Assert.IsType<Style>(dictionary["ListPageItemButtonEntranceStyle"])
                };
                BindingOperations.SetBinding(
                    button,
                    ListPageItemButton.ShouldPlayEnterAnimationProperty,
                    new Binding(nameof(VirtualizedListItemState.ShouldPlayEnterAnimation))
                    {
                        Source = state,
                        Mode = BindingMode.TwoWay
                    });
                BindingOperations.SetBinding(
                    button,
                    ListPageItemButton.EnterAnimationIndexProperty,
                    new Binding(nameof(VirtualizedListItemState.EnterAnimationIndex))
                    {
                        Source = state
                    });

                var container = new ListBoxItem
                {
                    Tag = state,
                    Content = button
                };
                var listBox = new ListBox();
                listBox.Resources.MergedDictionaries.Add(dictionary);
                listBox.Items.Add(container);
                window = new Window
                {
                    Width = 240,
                    Height = 80,
                    Content = listBox,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Opacity = 0
                };

                window.Show();
                listBox.UpdateLayout();
                button.UpdateLayout();
                window.UpdateLayout();
                Assert.True(button.IsLoaded);
                Assert.True(button.IsEnterAnimationPending);

                state.ShouldPlayEnterAnimation = true;
                button.GetBindingExpression(ListPageItemButton.ShouldPlayEnterAnimationProperty)?.UpdateTarget();
                window.UpdateLayout();

                Assert.False(state.ShouldPlayEnterAnimation);
                Assert.False(state.IsEntranceAnimationPending);
                Assert.Equal(
                    DependencyProperty.UnsetValue,
                    button.ReadLocalValue(ListPageItemButton.IsEnterAnimationPendingProperty));

                state.IsEntranceAnimationPending = true;
                window.UpdateLayout();

                Assert.True(button.IsEnterAnimationPending);
            }
            catch (Exception ex)
            {
                exception = ex;
            }
            finally
            {
                window?.Close();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            throw exception;
    }

    private static void WritePng(string path)
    {
        WritePng(path, Colors.SteelBlue);
    }

    private static void WritePng(string path, Color color)
    {
        var pixels = new byte[]
        {
            color.B, color.G, color.R, color.A,
            color.B, color.G, color.R, color.A,
            color.B, color.G, color.R, color.A,
            color.B, color.G, color.R, color.A
        };
        var bitmap = BitmapSource.Create(
            2,
            2,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            8);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
