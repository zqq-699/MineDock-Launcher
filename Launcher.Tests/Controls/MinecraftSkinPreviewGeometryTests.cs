using System.Windows;
using Launcher.App.Controls.Account;

namespace Launcher.Tests.Controls;

public sealed class MinecraftSkinPreviewGeometryTests
{
    [Fact]
    public void GetArmWidthUsesSlimAndClassicModelWidths()
    {
        Assert.Equal(4, MinecraftSkinPreviewGeometry.GetArmWidth(MinecraftSkinModel.Classic));
        Assert.Equal(3, MinecraftSkinPreviewGeometry.GetArmWidth(MinecraftSkinModel.Slim));
        Assert.Equal(4, MinecraftSkinPreviewGeometry.GetArmWidth(null));
    }

    [Theory]
    [InlineData(32, false)]
    [InlineData(64, true)]
    public void CanUseSecondLayerRequiresModernSkinHeight(int skinHeight, bool expected)
    {
        Assert.Equal(expected, MinecraftSkinPreviewGeometry.CanUseSecondLayer(skinHeight));
    }

    [Fact]
    public void SlimArmTextureUsesThreePixelWideFrontFace()
    {
        var faces = MinecraftSkinPreviewGeometry.GetFaces(
            SkinPart.RightArm,
            MinecraftSkinPreviewGeometry.GetArmWidth(MinecraftSkinModel.Slim));

        Assert.Equal(new Int32Rect(44, 20, 3, 12), faces.Front);
    }

    [Fact]
    public void SkinCarouselLayoutKeepsCenterLargerThanSideSlots()
    {
        var left = SkinCarousel3DLayout.GetPlacement(SkinCarouselSlot.Left);
        var center = SkinCarousel3DLayout.GetPlacement(SkinCarouselSlot.Center);
        var right = SkinCarousel3DLayout.GetPlacement(SkinCarouselSlot.Right);

        Assert.True(left.X < center.X);
        Assert.True(right.X > center.X);
        Assert.True(left.Scale < center.Scale);
        Assert.True(right.Scale < center.Scale);
        Assert.Equal(left.Scale, right.Scale);
    }

    [Fact]
    public void SkinCarouselEntryPlacementsStartOutsideSideSlots()
    {
        var left = SkinCarousel3DLayout.GetPlacement(SkinCarouselSlot.Left);
        var right = SkinCarousel3DLayout.GetPlacement(SkinCarouselSlot.Right);
        var previousEntry = SkinCarousel3DLayout.GetEntryPlacement(SkinCarouselDirection.Previous);
        var nextEntry = SkinCarousel3DLayout.GetEntryPlacement(SkinCarouselDirection.Next);

        Assert.True(previousEntry.X < left.X);
        Assert.True(nextEntry.X > right.X);
        Assert.Equal(left.Scale, previousEntry.Scale);
        Assert.Equal(right.Scale, nextEntry.Scale);
    }
}
