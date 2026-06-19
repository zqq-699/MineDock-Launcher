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
}
