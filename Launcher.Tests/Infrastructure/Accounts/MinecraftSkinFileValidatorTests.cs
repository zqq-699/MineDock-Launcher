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

using Launcher.Infrastructure.Accounts;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Launcher.Tests.Infrastructure.Accounts;

public sealed class MinecraftSkinFileValidatorTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), $"launcher-skin-validator-tests-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(64, 64, true)]
    [InlineData(64, 32, true)]
    [InlineData(32, 64, false)]
    [InlineData(64, 128, false)]
    public async Task ValidateAsyncAcceptsOnlyMinecraftSkinDimensions(
        int width,
        int height,
        bool expectedValid)
    {
        Directory.CreateDirectory(tempRoot);
        var skinPath = Path.Combine(tempRoot, $"{width}x{height}.png");
        await File.WriteAllBytesAsync(skinPath, CreatePng(width, height));
        var validator = new MinecraftSkinFileValidator();

        var result = await validator.ValidateAsync(skinPath);

        Assert.Equal(expectedValid, result.IsValid);
        Assert.Equal(width, result.Width);
        Assert.Equal(height, result.Height);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, recursive: true);
    }

    private static byte[] CreatePng(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 0x66;
            pixels[i + 1] = 0xAA;
            pixels[i + 2] = 0xDD;
            pixels[i + 3] = byte.MaxValue;
        }

        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}

