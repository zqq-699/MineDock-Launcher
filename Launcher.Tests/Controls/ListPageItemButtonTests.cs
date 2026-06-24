using System.Windows;
using Launcher.App.Controls;

namespace Launcher.Tests.Controls;

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
}
