using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Launcher.App.Controls.Account;
using Launcher.Domain.Models;

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
    public void SkinFrontPreviewUsesClassicAndSlimWidths()
    {
        var skin = CreateSkinBitmap(64);

        var classic = MinecraftSkinFrontPreviewRenderer.BuildFrontBitmap(skin, MinecraftSkinModel.Classic);
        var slim = MinecraftSkinFrontPreviewRenderer.BuildFrontBitmap(skin, MinecraftSkinModel.Slim);

        Assert.Equal(16, classic.PixelWidth);
        Assert.Equal(32, classic.PixelHeight);
        Assert.Equal(14, slim.PixelWidth);
        Assert.Equal(32, slim.PixelHeight);
    }

    [Fact]
    public void SkinFrontPreviewOverlaysSecondLayerOnlyForModernSkin()
    {
        var modern = CreateSkinBitmap(64);
        var legacy = CreateSkinBitmap(32);
        FillRect(modern, MinecraftSkinPreviewGeometry.GetFaces(SkinPart.Head).Front, Colors.Blue);
        FillRect(modern, MinecraftSkinPreviewGeometry.GetFaces(SkinPart.HeadOverlay).Front, Colors.Red);
        FillRect(legacy, MinecraftSkinPreviewGeometry.GetFaces(SkinPart.Head).Front, Colors.Blue);

        var modernPreview = MinecraftSkinFrontPreviewRenderer.BuildFrontBitmap(modern, MinecraftSkinModel.Classic);
        var legacyPreview = MinecraftSkinFrontPreviewRenderer.BuildFrontBitmap(legacy, MinecraftSkinModel.Classic);

        Assert.Equal(Colors.Red, ReadPixel(modernPreview, 4, 0));
        Assert.Equal(Colors.Blue, ReadPixel(legacyPreview, 4, 0));
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

    [Fact]
    public void SkinCarouselAnimationAllowsOnlyAdjacentNextTransition()
    {
        var first = CreateSkinRecord("skin-a", "hash-a");
        var second = CreateSkinRecord("skin-b", "hash-b");
        var third = CreateSkinRecord("skin-c", "hash-c");

        Assert.True(SkinCarousel3DLayout.CanAnimateTransition(
            SkinCarouselDirection.Next,
            null,
            first,
            second,
            first,
            second,
            third));
        Assert.False(SkinCarousel3DLayout.CanAnimateTransition(
            SkinCarouselDirection.Next,
            null,
            first,
            second,
            null,
            third,
            null));
    }

    [Fact]
    public void SkinCarouselVisualMatchingPrefersRecordIdOverContentHash()
    {
        var oldSelected = CreateSkinRecord("skin-a", "same-hash");
        var newPrevious = CreateSkinRecord("skin-b", "same-hash");
        var newSelected = CreateSkinRecord("skin-c", "other-hash");

        Assert.False(SkinCarousel3DLayout.SkinsRepresentSameVisualItem(oldSelected, newPrevious));
        Assert.False(SkinCarousel3DLayout.CanAnimateTransition(
            SkinCarouselDirection.Next,
            null,
            oldSelected,
            newSelected,
            newPrevious,
            newSelected,
            null));
    }

    private static WriteableBitmap CreateSkinBitmap(int height)
    {
        var bitmap = new WriteableBitmap(64, height, 96, 96, PixelFormats.Bgra32, null);
        var pixels = Enumerable.Repeat<byte>(255, 64 * height * 4).ToArray();
        bitmap.WritePixels(new Int32Rect(0, 0, 64, height), pixels, 64 * 4, 0);
        return bitmap;
    }

    private static LauncherSkinRecord CreateSkinRecord(string id, string contentHash)
    {
        return new LauncherSkinRecord
        {
            Id = id,
            Source = $"{id}.png",
            SkinModel = MinecraftSkinModel.Classic,
            ContentHash = contentHash,
            AddedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static void FillRect(WriteableBitmap bitmap, Int32Rect rect, Color color)
    {
        var stride = rect.Width * 4;
        var pixels = new byte[stride * rect.Height];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = color.B;
            pixels[index + 1] = color.G;
            pixels[index + 2] = color.R;
            pixels[index + 3] = color.A;
        }

        bitmap.WritePixels(rect, pixels, stride, 0);
    }

    private static Color ReadPixel(BitmapSource bitmap, int x, int y)
    {
        var pixels = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixels, 4, 0);
        return Color.FromArgb(pixels[3], pixels[2], pixels[1], pixels[0]);
    }
}
