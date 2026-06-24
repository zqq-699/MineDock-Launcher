using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
}
