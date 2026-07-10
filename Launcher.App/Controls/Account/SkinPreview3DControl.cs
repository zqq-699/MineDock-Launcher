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
using System.Windows.Media.Media3D;
using Launcher.Domain.Models;

namespace Launcher.App.Controls.Account;

public sealed class SkinPreview3DControl : Viewport3D
{
    public static readonly DependencyProperty SkinSourceProperty =
        DependencyProperty.Register(
            nameof(SkinSource),
            typeof(string),
            typeof(SkinPreview3DControl),
            new PropertyMetadata(null, OnPreviewPropertyChanged));

    public static readonly DependencyProperty SkinModelProperty =
        DependencyProperty.Register(
            nameof(SkinModel),
            typeof(MinecraftSkinModel?),
            typeof(SkinPreview3DControl),
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

    public SkinPreview3DControl()
    {
        ClipToBounds = true;
        Camera = new PerspectiveCamera(
            new Point3D(0, 4, 52),
            new Vector3D(0, 0, -52),
            new Vector3D(0, 1, 0),
            28);
    }

    private static void OnPreviewPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((SkinPreview3DControl)d).RebuildPreview();
    }

    private void RebuildPreview()
    {
        Children.Clear();

        if (string.IsNullOrWhiteSpace(SkinSource))
            return;

        BitmapImage skin;
        try
        {
            skin = MinecraftSkinPreviewModelBuilder.LoadSkinBitmap(SkinSource);
        }
        catch
        {
            return;
        }

        Children.Add(new ModelVisual3D
        {
            Content = new Model3DGroup
            {
                Children =
                {
                    MinecraftSkinPreviewModelBuilder.CreateAmbientLight(),
                    MinecraftSkinPreviewModelBuilder.CreateDirectionalLight(),
                    MinecraftSkinPreviewModelBuilder.BuildPlayerModel(skin, SkinModel)
                }
            }
        });
    }
}

internal static class MinecraftSkinPreviewModelBuilder
{
    public static AmbientLight CreateAmbientLight()
    {
        return new AmbientLight(Color.FromRgb(160, 160, 160));
    }

    public static DirectionalLight CreateDirectionalLight()
    {
        return new DirectionalLight(Color.FromRgb(130, 130, 130), new Vector3D(-0.2, -0.35, -0.9));
    }

    public static BitmapImage LoadSkinBitmap(string source)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        bitmap.UriSource = new Uri(source, UriKind.RelativeOrAbsolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    public static Model3DGroup BuildPlayerModel(
        BitmapSource skin,
        MinecraftSkinModel? skinModel,
        double brightness = 1)
    {
        var model = new Model3DGroup();
        var height = Math.Max(skin.PixelHeight, 32);
        var armWidth = MinecraftSkinPreviewGeometry.GetArmWidth(skinModel);
        var hasSecondLayer = MinecraftSkinPreviewGeometry.CanUseSecondLayer(height);
        var transform = new Transform3DGroup();
        transform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 2)));
        model.Transform = transform;

        AddCuboid(model, skin, new Rect3D(-4, 12, -4, 8, 8, 8), height, SkinPart.Head, brightness: brightness);
        AddCuboid(model, skin, new Rect3D(-4, 0, -2, 8, 12, 4), height, SkinPart.Body, brightness: brightness);
        AddCuboid(model, skin, new Rect3D(-4, -12, -2, 4, 12, 4), height, SkinPart.RightLeg, brightness: brightness);
        AddCuboid(model, skin, new Rect3D(0, -12, -2, 4, 12, 4), height, hasSecondLayer ? SkinPart.LeftLeg : SkinPart.RightLeg, brightness: brightness);
        AddCuboid(model, skin, new Rect3D(-4 - armWidth, 0, -2, armWidth, 12, 4), height, SkinPart.RightArm, armWidth, brightness);
        AddCuboid(model, skin, new Rect3D(4, 0, -2, armWidth, 12, 4), height, hasSecondLayer ? SkinPart.LeftArm : SkinPart.RightArm, armWidth, brightness);

        if (hasSecondLayer)
        {
            AddCuboid(model, skin, new Rect3D(-4.35, 11.65, -4.35, 8.7, 8.7, 8.7), height, SkinPart.HeadOverlay, brightness: brightness);
            AddCuboid(model, skin, new Rect3D(-4.25, -0.25, -2.25, 8.5, 12.5, 4.5), height, SkinPart.BodyOverlay, brightness: brightness);
            AddCuboid(model, skin, new Rect3D(-4.25, -12.25, -2.25, 4.5, 12.5, 4.5), height, SkinPart.RightLegOverlay, brightness: brightness);
            AddCuboid(model, skin, new Rect3D(-0.25, -12.25, -2.25, 4.5, 12.5, 4.5), height, SkinPart.LeftLegOverlay, brightness: brightness);
            AddCuboid(model, skin, new Rect3D(-4.25 - armWidth, -0.25, -2.25, armWidth + 0.5, 12.5, 4.5), height, SkinPart.RightArmOverlay, armWidth, brightness);
            AddCuboid(model, skin, new Rect3D(3.75, -0.25, -2.25, armWidth + 0.5, 12.5, 4.5), height, SkinPart.LeftArmOverlay, armWidth, brightness);
        }

        return model;
    }

    private static void AddCuboid(
        Model3DGroup group,
        ImageSource skin,
        Rect3D bounds,
        int skinHeight,
        SkinPart part,
        int armWidth = 4,
        double brightness = 1)
    {
        var faces = MinecraftSkinPreviewGeometry.GetFaces(part, armWidth);
        AddFace(group, skin, skinHeight, bounds, CubeFace.Front, faces.Front, brightness);
        AddFace(group, skin, skinHeight, bounds, CubeFace.Back, faces.Back, brightness);
        AddFace(group, skin, skinHeight, bounds, CubeFace.Left, faces.Left, brightness);
        AddFace(group, skin, skinHeight, bounds, CubeFace.Right, faces.Right, brightness);
        AddFace(group, skin, skinHeight, bounds, CubeFace.Top, faces.Top, brightness);
        AddFace(group, skin, skinHeight, bounds, CubeFace.Bottom, faces.Bottom, brightness);
    }

    private static void AddFace(
        Model3DGroup group,
        ImageSource skin,
        int skinHeight,
        Rect3D bounds,
        CubeFace face,
        Int32Rect textureRect,
        double brightness)
    {
        var mesh = CreateFaceMesh(bounds, face);
        var faceBitmap = CreatePixelSharpFaceBitmap(skin, textureRect, brightness);
        var brush = new ImageBrush(faceBitmap)
        {
            Stretch = Stretch.Fill,
            TileMode = TileMode.None
        };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
        brush.Freeze();
        var material = new DiffuseMaterial(brush);
        material.Freeze();

        group.Children.Add(new GeometryModel3D
        {
            Geometry = mesh,
            Material = material,
            BackMaterial = material
        });
    }

    private static MeshGeometry3D CreateFaceMesh(Rect3D b, CubeFace face)
    {
        var x0 = b.X;
        var x1 = b.X + b.SizeX;
        var y0 = b.Y;
        var y1 = b.Y + b.SizeY;
        var z0 = b.Z;
        var z1 = b.Z + b.SizeZ;

        Point3D p0;
        Point3D p1;
        Point3D p2;
        Point3D p3;
        switch (face)
        {
            case CubeFace.Front:
                p0 = new Point3D(x0, y1, z1);
                p1 = new Point3D(x1, y1, z1);
                p2 = new Point3D(x1, y0, z1);
                p3 = new Point3D(x0, y0, z1);
                break;
            case CubeFace.Back:
                p0 = new Point3D(x1, y1, z0);
                p1 = new Point3D(x0, y1, z0);
                p2 = new Point3D(x0, y0, z0);
                p3 = new Point3D(x1, y0, z0);
                break;
            case CubeFace.Left:
                p0 = new Point3D(x0, y1, z0);
                p1 = new Point3D(x0, y1, z1);
                p2 = new Point3D(x0, y0, z1);
                p3 = new Point3D(x0, y0, z0);
                break;
            case CubeFace.Right:
                p0 = new Point3D(x1, y1, z1);
                p1 = new Point3D(x1, y1, z0);
                p2 = new Point3D(x1, y0, z0);
                p3 = new Point3D(x1, y0, z1);
                break;
            case CubeFace.Top:
                p0 = new Point3D(x0, y1, z0);
                p1 = new Point3D(x1, y1, z0);
                p2 = new Point3D(x1, y1, z1);
                p3 = new Point3D(x0, y1, z1);
                break;
            default:
                p0 = new Point3D(x0, y0, z1);
                p1 = new Point3D(x1, y0, z1);
                p2 = new Point3D(x1, y0, z0);
                p3 = new Point3D(x0, y0, z0);
                break;
        }

        var mesh = new MeshGeometry3D
        {
            Positions = [p0, p1, p2, p3],
            TextureCoordinates =
            [
                new Point(0, 0),
                new Point(1, 0),
                new Point(1, 1),
                new Point(0, 1)
            ],
            TriangleIndices = [0, 1, 2, 0, 2, 3]
        };
        mesh.Freeze();
        return mesh;
    }

    private static BitmapSource CreatePixelSharpFaceBitmap(
        ImageSource skin,
        Int32Rect textureRect,
        double brightness)
    {
        const int scale = 16;
        brightness = Math.Clamp(brightness, 0, 1);
        var source = EnsureBgra32((BitmapSource)skin);
        var clampedX = Math.Clamp(textureRect.X, 0, source.PixelWidth - 1);
        var clampedY = Math.Clamp(textureRect.Y, 0, source.PixelHeight - 1);
        var clampedRect = new Int32Rect(
            clampedX,
            clampedY,
            Math.Max(1, Math.Min(textureRect.Width, source.PixelWidth - clampedX)),
            Math.Max(1, Math.Min(textureRect.Height, source.PixelHeight - clampedY)));
        var stride = clampedRect.Width * 4;
        var pixels = new byte[stride * clampedRect.Height];
        source.CopyPixels(clampedRect, pixels, stride, 0);

        var outputWidth = clampedRect.Width * scale;
        var outputHeight = clampedRect.Height * scale;
        var outputStride = outputWidth * 4;
        var output = new byte[outputStride * outputHeight];
        for (var y = 0; y < outputHeight; y++)
        {
            var sourceY = y / scale;
            for (var x = 0; x < outputWidth; x++)
            {
                var sourceX = x / scale;
                var sourceIndex = sourceY * stride + sourceX * 4;
                var outputIndex = y * outputStride + x * 4;
                output[outputIndex] = ApplyBrightness(pixels[sourceIndex], brightness);
                output[outputIndex + 1] = ApplyBrightness(pixels[sourceIndex + 1], brightness);
                output[outputIndex + 2] = ApplyBrightness(pixels[sourceIndex + 2], brightness);
                output[outputIndex + 3] = pixels[sourceIndex + 3];
            }
        }

        var bitmap = BitmapSource.Create(
            outputWidth,
            outputHeight,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            output,
            outputStride);
        bitmap.Freeze();
        return bitmap;
    }

    private static byte ApplyBrightness(byte value, double brightness)
    {
        return (byte)Math.Round(value * brightness);
    }

    private static BitmapSource EnsureBgra32(BitmapSource source)
    {
        if (source.Format == PixelFormats.Bgra32)
            return source;

        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    private enum CubeFace
    {
        Front,
        Back,
        Left,
        Right,
        Top,
        Bottom
    }
}

public static class MinecraftSkinPreviewGeometry
{
    public static int GetArmWidth(MinecraftSkinModel? skinModel)
    {
        return skinModel is MinecraftSkinModel.Slim ? 3 : 4;
    }

    public static bool CanUseSecondLayer(int skinPixelHeight)
    {
        return skinPixelHeight >= 64;
    }

    public static SkinPartFaces GetFaces(SkinPart part, int armWidth = 4)
    {
        return part switch
        {
            SkinPart.Head => Cube(8, 0, 8, 8, 8, 8, 8, 8, 16, 8, 0, 8),
            SkinPart.HeadOverlay => Cube(40, 0, 8, 8, 8, 8, 8, 8, 16, 8, 32, 8),
            SkinPart.Body => Cube(20, 16, 8, 4, 8, 12, 4, 12, 4, 12, 16, 20),
            SkinPart.BodyOverlay => Cube(20, 32, 8, 4, 8, 12, 4, 12, 4, 12, 16, 36),
            SkinPart.RightArm => Arm(40, 16, armWidth),
            SkinPart.RightArmOverlay => Arm(40, 32, armWidth),
            SkinPart.LeftArm => Arm(32, 48, armWidth),
            SkinPart.LeftArmOverlay => Arm(48, 48, armWidth),
            SkinPart.RightLeg => Cube(4, 16, 4, 4, 4, 12, 4, 12, 4, 12, 0, 20),
            SkinPart.RightLegOverlay => Cube(4, 32, 4, 4, 4, 12, 4, 12, 4, 12, 0, 36),
            SkinPart.LeftLeg => Cube(20, 48, 4, 4, 4, 12, 4, 12, 4, 12, 16, 52),
            SkinPart.LeftLegOverlay => Cube(4, 48, 4, 4, 4, 12, 4, 12, 4, 12, 0, 52),
            _ => Cube(8, 0, 8, 8, 8, 8, 8, 8, 16, 8, 0, 8)
        };
    }

    private static SkinPartFaces Arm(int x, int y, int armWidth)
    {
        return Cube(
            x + armWidth,
            y,
            armWidth,
            4,
            armWidth,
            12,
            4,
            12,
            armWidth,
            12,
            x,
            y + 4);
    }

    private static SkinPartFaces Cube(
        int topX,
        int topY,
        int width,
        int depth,
        int frontWidth,
        int height,
        int sideWidth,
        int sideHeight,
        int backWidth,
        int backHeight,
        int leftX,
        int sideY)
    {
        return new SkinPartFaces(
            new Int32Rect(leftX + sideWidth, sideY, frontWidth, height),
            new Int32Rect(leftX + sideWidth + frontWidth + sideWidth, sideY, backWidth, backHeight),
            new Int32Rect(leftX, sideY, sideWidth, sideHeight),
            new Int32Rect(leftX + sideWidth + frontWidth, sideY, sideWidth, sideHeight),
            new Int32Rect(topX, topY, width, depth),
            new Int32Rect(topX + width, topY, width, depth));
    }
}

public enum SkinPart
{
    Head,
    HeadOverlay,
    Body,
    BodyOverlay,
    RightArm,
    RightArmOverlay,
    LeftArm,
    LeftArmOverlay,
    RightLeg,
    RightLegOverlay,
    LeftLeg,
    LeftLegOverlay
}

public readonly record struct SkinPartFaces(
    Int32Rect Front,
    Int32Rect Back,
    Int32Rect Left,
    Int32Rect Right,
    Int32Rect Top,
    Int32Rect Bottom);
