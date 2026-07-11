/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Windows.Media;
using System.Windows.Media.Imaging;
using Launcher.App.Controls.Account;
using Launcher.Domain.Models;

namespace Launcher.Tests.ViewModels.Account;

public sealed class MinecraftSkinPreviewGeometryTests
{
    [Theory]
    [InlineData(32, 42)]
    [InlineData(64, 72)]
    public void BuildPlayerModelIncludesLegacyHeadOverlay(int height, int expectedFaceCount)
    {
        var skin = CreateSkin(64, height);

        var model = MinecraftSkinPreviewModelBuilder.BuildPlayerModel(skin, MinecraftSkinModel.Classic);

        Assert.Equal(expectedFaceCount, model.Children.Count);
    }

    [Theory]
    [InlineData(64, 32, true)]
    [InlineData(64, 64, true)]
    [InlineData(32, 64, false)]
    public void HeadOverlayAvailabilityMatchesMinecraftTextureLayout(
        int width,
        int height,
        bool expected)
    {
        Assert.Equal(expected, MinecraftSkinPreviewGeometry.CanUseHeadOverlay(width, height));
    }

    private static BitmapSource CreateSkin(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            width * 4);
        bitmap.Freeze();
        return bitmap;
    }
}
