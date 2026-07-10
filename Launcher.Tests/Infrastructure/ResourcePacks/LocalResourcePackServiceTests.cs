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

using System.IO.Compression;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Launcher.Domain.Models;
using Launcher.Infrastructure;
using Launcher.Infrastructure.FileSystem;
using Launcher.Tests.Helpers;

namespace Launcher.Tests.Infrastructure.ResourcePacks;

public sealed class LocalResourcePackServiceTests : TestTempDirectory
{
    [Fact]
    public async Task GetResourcePacksAsyncLoadsTopLevelZipFilesAndReadsIcons()
    {
        var instance = CreateInstance("resource-pack-list");
        var resourcePacksDirectory = Path.Combine(instance.InstanceDirectory, "resourcepacks");
        Directory.CreateDirectory(resourcePacksDirectory);
        var nestedDirectory = Path.Combine(resourcePacksDirectory, "nested");
        Directory.CreateDirectory(nestedDirectory);

        var withIconPath = Path.Combine(resourcePacksDirectory, "Fresh Animations.zip");
        var withoutIconPath = Path.Combine(resourcePacksDirectory, "Bare Bones.zip");
        var nestedZipPath = Path.Combine(nestedDirectory, "ignored.zip");
        CreateZip(withIconPath, ("pack.png", CreatePngBytes(Colors.Red)));
        CreateZip(withoutIconPath);
        CreateZip(nestedZipPath, ("pack.png", CreatePngBytes(Colors.Blue)));

        var createdAt = new DateTimeOffset(2026, 1, 3, 10, 20, 30, TimeSpan.Zero);
        File.SetCreationTimeUtc(withIconPath, createdAt.UtcDateTime);

        var service = CreateService();
        var resourcePacks = await service.GetResourcePacksAsync(instance);

        Assert.Equal(2, resourcePacks.Count);
        Assert.DoesNotContain(resourcePacks, resourcePack => string.Equals(resourcePack.FileName, "ignored.zip", StringComparison.OrdinalIgnoreCase));

        var fresh = resourcePacks.Single(resourcePack => resourcePack.FileName == "Fresh Animations.zip");
        Assert.Equal("Fresh Animations", fresh.Name);
        Assert.Equal(withIconPath, fresh.FullPath);
        Assert.NotNull(fresh.IconSource);
        Assert.True(File.Exists(new Uri(fresh.IconSource!, UriKind.Absolute).LocalPath));
        Assert.Equal(createdAt, fresh.CreatedAt);

        var bareBones = resourcePacks.Single(resourcePack => resourcePack.FileName == "Bare Bones.zip");
        Assert.Null(bareBones.IconSource);
    }

    [Fact]
    public async Task GetResourcePacksAsyncFallsBackWhenIconCannotBeRead()
    {
        var instance = CreateInstance("resource-pack-fallback");
        var resourcePacksDirectory = Path.Combine(instance.InstanceDirectory, "resourcepacks");
        Directory.CreateDirectory(resourcePacksDirectory);

        CreateZip(Path.Combine(resourcePacksDirectory, "no-icon.zip"));
        CreateZip(Path.Combine(resourcePacksDirectory, "broken-icon.zip"), ("pack.png", "not an image"u8.ToArray()));
        await File.WriteAllTextAsync(Path.Combine(resourcePacksDirectory, "not-a-zip.zip"), "plain text");

        var resourcePacks = await CreateService().GetResourcePacksAsync(instance);

        Assert.Equal(3, resourcePacks.Count);
        Assert.All(resourcePacks, resourcePack => Assert.True(string.IsNullOrWhiteSpace(resourcePack.IconSource)));
    }

    [Fact]
    public async Task GetResourcePacksAsyncReusesStableCachedIconPath()
    {
        var instance = CreateInstance("resource-pack-stable-cache");
        var zipPath = Path.Combine(instance.InstanceDirectory, "resourcepacks", "stable.zip");
        CreateZip(zipPath, ("pack.png", CreatePngBytes(Colors.Gold)));

        var service = CreateService();
        var first = Assert.Single(await service.GetResourcePacksAsync(instance));
        var second = Assert.Single(await service.GetResourcePacksAsync(instance));

        Assert.Equal(first.IconSource, second.IconSource);
        Assert.True(File.Exists(new Uri(first.IconSource!).LocalPath));
    }

    [Fact]
    public async Task ImportAsyncCopiesZipIntoResourcePacksAndRenamesDuplicates()
    {
        var instance = CreateInstance("resource-pack-import");
        var resourcePacksDirectory = Path.Combine(instance.InstanceDirectory, "resourcepacks");
        Directory.CreateDirectory(resourcePacksDirectory);
        CreateZip(Path.Combine(resourcePacksDirectory, "Fresh Animations.zip"));

        var sourcePath = Path.Combine(TempRoot, "Fresh Animations.zip");
        CreateZip(sourcePath, ("pack.png", CreatePngBytes(Colors.Green)));

        var result = await CreateService().ImportAsync(instance, sourcePath);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.ImportedResourcePack);
        Assert.Equal("Fresh Animations (1).zip", result.ImportedResourcePack!.FileName);
        Assert.True(File.Exists(Path.Combine(resourcePacksDirectory, "Fresh Animations (1).zip")));
    }

    [Fact]
    public async Task DeleteAsyncRemovesResourcePackFile()
    {
        var instance = CreateInstance("resource-pack-delete");
        var zipPath = Path.Combine(instance.InstanceDirectory, "resourcepacks", "Fresh Animations.zip");
        CreateZip(zipPath);

        var service = CreateService();
        await service.DeleteAsync(new LocalResourcePack
        {
            Name = "Fresh Animations",
            FileName = "Fresh Animations.zip",
            FullPath = zipPath,
            CreatedAt = DateTimeOffset.UtcNow
        });

        Assert.False(File.Exists(zipPath));
    }

    private LocalResourcePackService CreateService()
    {
        return new LocalResourcePackService(new LauncherPathProvider(TempRoot));
    }

    private GameInstance CreateInstance(string name)
    {
        var instanceDirectory = Path.Combine(TempRoot, "instances", name);
        Directory.CreateDirectory(Path.Combine(instanceDirectory, "resourcepacks"));
        return new GameInstance
        {
            Id = name,
            Name = name,
            InstanceDirectory = instanceDirectory
        };
    }

    private static void CreateZip(string path, params (string EntryName, byte[] Content)[] entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var entry in entries)
        {
            var zipEntry = archive.CreateEntry(entry.EntryName);
            using var stream = zipEntry.Open();
            stream.Write(entry.Content);
        }
    }

    private static byte[] CreatePngBytes(Color color)
    {
        var pixels = new[] { color.B, color.G, color.R, color.A };
        var bitmap = BitmapSource.Create(
            1,
            1,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            4);
        bitmap.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
