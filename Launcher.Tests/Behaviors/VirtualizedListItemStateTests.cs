using Launcher.App.Behaviors;

namespace Launcher.Tests.Behaviors;

public sealed class VirtualizedListItemStateTests
{
    [Fact]
    public void StateDoesNotHideItemByDefault()
    {
        var state = new VirtualizedListItemState();

        Assert.False(state.IsEntranceAnimationPending);
        Assert.False(state.ShouldPlayEnterAnimation);
    }

    [Fact]
    public void ClearingEnterAnimationReleasesPendingVisibility()
    {
        var state = new VirtualizedListItemState
        {
            IsEntranceAnimationPending = true,
            ShouldPlayEnterAnimation = true
        };

        state.ShouldPlayEnterAnimation = false;

        Assert.False(state.ShouldPlayEnterAnimation);
        Assert.False(state.IsEntranceAnimationPending);
    }

    [Fact]
    public void PendingVisibilityRaisesPropertyChanged()
    {
        var state = new VirtualizedListItemState();
        var changedProperties = new List<string?>();
        state.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        state.IsEntranceAnimationPending = true;

        Assert.Contains(nameof(VirtualizedListItemState.IsEntranceAnimationPending), changedProperties);
    }
}
