using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Launcher.App.Controls.Account;
using Launcher.Application.Accounts;
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

    [Fact]
    public void CapeCarouselLayoutKeepsCenterLargerThanSideSlots()
    {
        var left = CapeCarousel3DLayout.GetPlacement(CapeCarouselSlot.Left);
        var center = CapeCarousel3DLayout.GetPlacement(CapeCarouselSlot.Center);
        var right = CapeCarousel3DLayout.GetPlacement(CapeCarouselSlot.Right);

        Assert.True(left.X < center.X);
        Assert.True(right.X > center.X);
        Assert.True(left.Scale < center.Scale);
        Assert.True(right.Scale < center.Scale);
        Assert.Equal(left.Scale, right.Scale);
    }

    [Fact]
    public void CapeCarouselAnimationAllowsOnlyAdjacentNextTransition()
    {
        var first = CreateCapeOption("cape-a");
        var second = CreateCapeOption("cape-b");
        var third = CreateCapeOption("cape-c");

        Assert.True(CapeCarousel3DLayout.CanAnimateTransition(
            CapeCarouselDirection.Next,
            null,
            first,
            second,
            first,
            second,
            third));
        Assert.False(CapeCarousel3DLayout.CanAnimateTransition(
            CapeCarouselDirection.Next,
            null,
            first,
            second,
            null,
            third,
            null));
    }

    [Fact]
    public void NoneCapePreviewDoesNotRequireImageUrl()
    {
        var noneCape = new AccountCapeOption
        {
            DisplayName = string.Empty,
            IsNone = true
        };

        var model = MinecraftCapePreviewModelBuilder.BuildCapeModel(noneCape);

        Assert.NotEmpty(model.Children);
    }

    [Fact]
    public void CapePreviewCreatesVisibleFallbackBeforeImageLoads()
    {
        var cape = new AccountCapeOption
        {
            Id = "cape-a",
            DisplayName = "Cape",
            ImageUrl = "missing-cape.png"
        };

        var model = MinecraftCapePreviewModelBuilder.BuildCapeModel(cape);

        Assert.NotEmpty(model.Children);
    }

    [Fact]
    public void CapePreviewUsesLoadedTextureWhenAvailable()
    {
        var cape = new AccountCapeOption
        {
            Id = "cape-a",
            DisplayName = "Cape",
            ImageUrl = "cape.png"
        };
        var texture = CreateCapeBitmap();

        var model = MinecraftCapePreviewModelBuilder.BuildCapeModel(cape, texture: texture);

        Assert.True(model.Children.Count > 2);
    }

    [Fact]
    public void CapePreviewFaceBitmapUsesNearestNeighborPixelBlocks()
    {
        var cape = CreateCapeBitmap();
        FillRect(cape, new Int32Rect(1, 1, 1, 1), Colors.Lime);
        FillRect(cape, new Int32Rect(2, 1, 1, 1), Colors.SaddleBrown);

        var face = MinecraftCapePreviewModelBuilder.CreatePixelSharpFaceBitmap(
            cape,
            new Int32Rect(1, 1, 2, 1),
            1);

        Assert.Equal(16, face.PixelWidth);
        Assert.Equal(8, face.PixelHeight);
        Assert.Equal(Colors.Lime, ReadPixel(face, 0, 0));
        Assert.Equal(Colors.Lime, ReadPixel(face, 7, 7));
        Assert.Equal(Colors.SaddleBrown, ReadPixel(face, 8, 0));
        Assert.Equal(Colors.SaddleBrown, ReadPixel(face, 15, 7));
    }

    private static WriteableBitmap CreateSkinBitmap(int height)
    {
        var bitmap = new WriteableBitmap(64, height, 96, 96, PixelFormats.Bgra32, null);
        var pixels = Enumerable.Repeat<byte>(255, 64 * height * 4).ToArray();
        bitmap.WritePixels(new Int32Rect(0, 0, 64, height), pixels, 64 * 4, 0);
        return bitmap;
    }

    private static WriteableBitmap CreateCapeBitmap()
    {
        var bitmap = new WriteableBitmap(64, 32, 96, 96, PixelFormats.Bgra32, null);
        var pixels = Enumerable.Repeat<byte>(255, 64 * 32 * 4).ToArray();
        bitmap.WritePixels(new Int32Rect(0, 0, 64, 32), pixels, 64 * 4, 0);
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

    private static AccountCapeOption CreateCapeOption(string id)
    {
        return new AccountCapeOption
        {
            Id = id,
            DisplayName = id,
            ImageUrl = $"{id}.png"
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
