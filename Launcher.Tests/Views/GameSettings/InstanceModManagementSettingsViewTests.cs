using Launcher.App.Views.GameSettings;

namespace Launcher.Tests.Views.GameSettings;

public sealed class InstanceModManagementSettingsViewTests
{
    [Fact]
    public void CalculateStickyHeaderLayoutReturnsHiddenBeforeHeaderReachesTop()
    {
        var layout = GameSettingsPageView.CalculateStickyModHeaderLayout(
            anchorTop: 58,
            originalHeaderTop: 68,
            sectionBottom: 320,
            overlayHeight: 86);

        Assert.False(layout.IsVisible);
        Assert.Equal(0d, layout.TranslateY);
    }

    [Fact]
    public void CalculateStickyHeaderLayoutClampsOverlayInsideSectionBottom()
    {
        var layout = GameSettingsPageView.CalculateStickyModHeaderLayout(
            anchorTop: 58,
            originalHeaderTop: 12,
            sectionBottom: 60,
            overlayHeight: 90);

        Assert.True(layout.IsVisible);
        Assert.Equal(-30d, layout.TranslateY);
    }

    [Fact]
    public void CalculateStickyHeaderLayoutKeepsOverlayFixedAtAnchorBeforeBottomClamp()
    {
        var layout = GameSettingsPageView.CalculateStickyModHeaderLayout(
            anchorTop: 58,
            originalHeaderTop: 20,
            sectionBottom: 320,
            overlayHeight: 90);

        Assert.True(layout.IsVisible);
        Assert.Equal(58d, layout.TranslateY);
    }

    [Fact]
    public void CalculateStickyHeaderLayoutSupportsTheoreticalOverlayHeightFromOriginalHeader()
    {
        const double originalHeaderHeight = 74d;
        const double floatingHostBottomPadding = 12d;

        var layout = GameSettingsPageView.CalculateStickyModHeaderLayout(
            anchorTop: 58,
            originalHeaderTop: 8,
            sectionBottom: 320,
            overlayHeight: originalHeaderHeight + floatingHostBottomPadding);

        Assert.True(layout.IsVisible);
        Assert.Equal(58d, layout.TranslateY);
    }

}
