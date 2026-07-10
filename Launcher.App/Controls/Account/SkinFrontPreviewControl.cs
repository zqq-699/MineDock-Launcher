/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Launcher.Domain.Models;

namespace Launcher.App.Controls.Account;

public sealed class SkinFrontPreviewControl : Image
{
    public static readonly DependencyProperty SkinSourceProperty =
        DependencyProperty.Register(
            nameof(SkinSource),
            typeof(string),
            typeof(SkinFrontPreviewControl),
            new PropertyMetadata(null, OnPreviewPropertyChanged));

    public static readonly DependencyProperty SkinModelProperty =
        DependencyProperty.Register(
            nameof(SkinModel),
            typeof(MinecraftSkinModel?),
            typeof(SkinFrontPreviewControl),
            new PropertyMetadata(null, OnPreviewPropertyChanged));

    public string? SkinSource
    {
        get => (string?)GetValue(SkinSourceProperty);
        set => SetValue(SkinSourceProperty, value);
    }

    public MinecraftSkinModel? SkinModel
    {
        get => (MinecraftSkinModel?)GetValue(SkinModelProperty);
        set => SetValue(SkinModelProperty, value);
    }

    public SkinFrontPreviewControl()
    {
        Stretch = Stretch.Uniform;
        SnapsToDevicePixels = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
    }

    private static void OnPreviewPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((SkinFrontPreviewControl)d).RebuildPreview();
    }

    private void RebuildPreview()
    {
        if (string.IsNullOrWhiteSpace(SkinSource))
        {
            Source = null;
            return;
        }

        try
        {
            var skin = MinecraftSkinPreviewModelBuilder.LoadSkinBitmap(SkinSource);
            Source = MinecraftSkinFrontPreviewRenderer.BuildFrontBitmap(skin, SkinModel);
        }
        catch
        {
            Source = null;
        }
    }
}

public static class MinecraftSkinFrontPreviewRenderer
{
    private const int PreviewHeight = 32;

    public static BitmapSource BuildFrontBitmap(BitmapSource skin, MinecraftSkinModel? skinModel)
    {
        var source = EnsureBgra32(skin);
        var armWidth = MinecraftSkinPreviewGeometry.GetArmWidth(skinModel);
        var width = armWidth * 2 + 8;
        var output = new byte[width * PreviewHeight * 4];
        var hasSecondLayer = MinecraftSkinPreviewGeometry.CanUseSecondLayer(source.PixelHeight);
        var leftArmPart = hasSecondLayer ? SkinPart.LeftArm : SkinPart.RightArm;
        var leftLegPart = hasSecondLayer ? SkinPart.LeftLeg : SkinPart.RightLeg;

        DrawPart(source, output, width, SkinPart.RightArm, 0, 8, armWidth);
        DrawPart(source, output, width, SkinPart.Body, armWidth, 8, armWidth);
        DrawPart(source, output, width, leftArmPart, armWidth + 8, 8, armWidth);
        DrawPart(source, output, width, SkinPart.Head, armWidth, 0, armWidth);
        DrawPart(source, output, width, SkinPart.RightLeg, armWidth, 20, armWidth);
        DrawPart(source, output, width, leftLegPart, armWidth + 4, 20, armWidth);

        if (hasSecondLayer)
        {
            DrawPart(source, output, width, SkinPart.RightArmOverlay, 0, 8, armWidth);
            DrawPart(source, output, width, SkinPart.BodyOverlay, armWidth, 8, armWidth);
            DrawPart(source, output, width, SkinPart.LeftArmOverlay, armWidth + 8, 8, armWidth);
            DrawPart(source, output, width, SkinPart.HeadOverlay, armWidth, 0, armWidth);
            DrawPart(source, output, width, SkinPart.RightLegOverlay, armWidth, 20, armWidth);
            DrawPart(source, output, width, SkinPart.LeftLegOverlay, armWidth + 4, 20, armWidth);
        }

        var bitmap = BitmapSource.Create(
            width,
            PreviewHeight,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            output,
            width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static void DrawPart(
        BitmapSource source,
        byte[] output,
        int outputWidth,
        SkinPart part,
        int destinationX,
        int destinationY,
        int armWidth)
    {
        var rect = MinecraftSkinPreviewGeometry.GetFaces(part, armWidth).Front;
        var clampedRect = ClampRect(source, rect);
        var stride = clampedRect.Width * 4;
        var pixels = new byte[stride * clampedRect.Height];
        source.CopyPixels(clampedRect, pixels, stride, 0);

        for (var y = 0; y < clampedRect.Height; y++)
        {
            for (var x = 0; x < clampedRect.Width; x++)
            {
                var targetX = destinationX + x;
                var targetY = destinationY + y;
                if (targetX < 0 || targetX >= outputWidth || targetY < 0 || targetY >= PreviewHeight)
                    continue;

                var sourceIndex = y * stride + x * 4;
                var targetIndex = (targetY * outputWidth + targetX) * 4;
                AlphaBlend(output, targetIndex, pixels, sourceIndex);
            }
        }
    }

    private static Int32Rect ClampRect(BitmapSource source, Int32Rect rect)
    {
        var x = Math.Clamp(rect.X, 0, source.PixelWidth - 1);
        var y = Math.Clamp(rect.Y, 0, source.PixelHeight - 1);
        return new Int32Rect(
            x,
            y,
            Math.Max(1, Math.Min(rect.Width, source.PixelWidth - x)),
            Math.Max(1, Math.Min(rect.Height, source.PixelHeight - y)));
    }

    private static void AlphaBlend(byte[] destination, int destinationIndex, byte[] source, int sourceIndex)
    {
        var sourceAlpha = source[sourceIndex + 3] / 255d;
        if (sourceAlpha <= 0)
            return;

        if (sourceAlpha >= 1)
        {
            destination[destinationIndex] = source[sourceIndex];
            destination[destinationIndex + 1] = source[sourceIndex + 1];
            destination[destinationIndex + 2] = source[sourceIndex + 2];
            destination[destinationIndex + 3] = source[sourceIndex + 3];
            return;
        }

        var destinationAlpha = destination[destinationIndex + 3] / 255d;
        var outputAlpha = sourceAlpha + destinationAlpha * (1 - sourceAlpha);
        if (outputAlpha <= 0)
            return;

        for (var offset = 0; offset < 3; offset++)
        {
            var sourceValue = source[sourceIndex + offset] / 255d;
            var destinationValue = destination[destinationIndex + offset] / 255d;
            var outputValue = (sourceValue * sourceAlpha + destinationValue * destinationAlpha * (1 - sourceAlpha)) / outputAlpha;
            destination[destinationIndex + offset] = (byte)Math.Round(outputValue * 255);
        }

        destination[destinationIndex + 3] = (byte)Math.Round(outputAlpha * 255);
    }

    private static BitmapSource EnsureBgra32(BitmapSource source)
    {
        if (source.Format == PixelFormats.Bgra32)
            return source;

        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }
}
