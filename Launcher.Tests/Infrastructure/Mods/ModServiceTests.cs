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
using Launcher.Application;
using Launcher.Domain.Models;
using Launcher.Infrastructure;
using Launcher.Infrastructure.FileSystem;

namespace Launcher.Tests.Infrastructure.Mods;

public sealed class ModServiceTests : TestTempDirectory
{
    [Fact]
    public async Task ModServiceImportsDisablesAndEnablesJar()
    {
        var instanceDirectory = Path.Combine(TempRoot, "instances", "modded");
        Directory.CreateDirectory(instanceDirectory);
        var sourceJar = Path.Combine(TempRoot, "example.jar");
        Directory.CreateDirectory(TempRoot);
        await File.WriteAllTextAsync(sourceJar, "fake jar");

        var instance = new GameInstance { InstanceDirectory = instanceDirectory };
        var service = CreateService();

        var imported = await service.ImportAsync(instance, sourceJar);
        await service.SetEnabledAsync(imported, false);
        var disabled = (await service.GetModsAsync(instance)).Single();

        Assert.False(disabled.IsEnabled);
        Assert.Equal("example.jar.disabled", disabled.FileName);
        Assert.True(File.Exists(Path.Combine(instanceDirectory, "mods", "example.jar.disabled")));

        await service.SetEnabledAsync(disabled, true);
        var enabled = (await service.GetModsAsync(instance)).Single();

        Assert.True(enabled.IsEnabled);
        Assert.Equal("example.jar", enabled.FileName);
        Assert.True(File.Exists(Path.Combine(instanceDirectory, "mods", "example.jar")));
    }

    [Fact]
    public async Task ModServiceImportAsyncOverwritesExistingJarWhenRequested()
    {
        var instanceDirectory = Path.Combine(TempRoot, "instances", "overwrite");
        Directory.CreateDirectory(instanceDirectory);
        var sourceJar = Path.Combine(TempRoot, "replace-me.jar");
        await File.WriteAllTextAsync(sourceJar, "first");

        var instance = new GameInstance { InstanceDirectory = instanceDirectory };
        var service = CreateService();

        await service.ImportAsync(instance, sourceJar);
        await File.WriteAllTextAsync(sourceJar, "second");

        await service.ImportAsync(instance, sourceJar, overwriteExisting: true);

        var importedPath = Path.Combine(instanceDirectory, "mods", "replace-me.jar");
        Assert.Equal("second", await File.ReadAllTextAsync(importedPath));
        Assert.Single(await service.GetModsAsync(instance));
    }

    [Fact]
    public async Task GetModsAsyncReadsJarDisabledFilesFromModsDirectory()
    {
        var instance = CreateInstance("disabled-in-mods-folder");
        await File.WriteAllTextAsync(
            Path.Combine(instance.InstanceDirectory, "mods", "disabled-example.jar.disabled"),
            "fake jar");

        var mod = Assert.Single(await CreateService().GetModsAsync(instance));

        Assert.False(mod.IsEnabled);
        Assert.Equal("disabled-example.jar.disabled", mod.FileName);
        Assert.Equal("disabled-example", mod.Name);
    }

    [Fact]
    public async Task GetModsAsyncIgnoresFabricStringIconAndLeavesIconSourceEmpty()
    {
        var instance = CreateInstance("fabric-string-icon");
        var jarPath = Path.Combine(instance.InstanceDirectory, "mods", "fabric-api.jar");
        CreateZip(
            jarPath,
            TextEntry(
                "fabric.mod.json",
                """
                {
                  "schemaVersion": 1,
                  "id": "fabric-api",
                  "version": "1.0.0",
                  "name": "Fabric API",
                  "icon": "assets/fabric-api/icon.png"
                }
                """),
            ("assets/fabric-api/icon.png", CreatePngBytes(Colors.Red)));

        var mods = await CreateService().GetModsAsync(instance);

        var mod = Assert.Single(mods);
        Assert.Equal("Fabric API", mod.Name);
        Assert.Equal("fabric", mod.Loader);
        Assert.Equal("fabric-api", mod.ModId);
        Assert.Equal("1.0.0", mod.Version);
        Assert.True(string.IsNullOrWhiteSpace(mod.IconSource));
    }

    [Fact]
    public async Task GetModsAsyncIgnoresFabricMappedIconAndLeavesIconSourceEmpty()
    {
        var instance = CreateInstance("fabric-mapped-icon");
        var jarPath = Path.Combine(instance.InstanceDirectory, "mods", "mapped-icon.jar");
        CreateZip(
            jarPath,
            TextEntry(
                "fabric.mod.json",
                """
                {
                  "schemaVersion": 1,
                  "id": "mapped-icon",
                  "version": "1.0.0",
                  "icon": {
                    "16": "assets/mod/icon-16.png",
                    "64": "assets/mod/icon-64.png"
                  }
                }
                """),
            ("assets/mod/icon-16.png", CreatePngBytes(Colors.Red)),
            ("assets/mod/icon-64.png", CreatePngBytes(Colors.Lime)));

        var mods = await CreateService().GetModsAsync(instance);

        var mod = Assert.Single(mods);
        Assert.Equal("fabric", mod.Loader);
        Assert.Equal("mapped-icon", mod.ModId);
        Assert.Equal("1.0.0", mod.Version);
        Assert.True(string.IsNullOrWhiteSpace(mod.IconSource));
    }

    [Fact]
    public async Task GetModsAsyncIgnoresForgeLogoFileAndLeavesIconSourceEmpty()
    {
        var instance = CreateInstance("forge-logo-file");
        var jarPath = Path.Combine(instance.InstanceDirectory, "mods", "forge-example.jar");
        CreateZip(
            jarPath,
            TextEntry(
                "META-INF/mods.toml",
                """
                modLoader="javafml"
                loaderVersion="[47,)"
                license="MIT"

                [[mods]]
                modId="forge-example"
                version="1.0.0"
                displayName="Forge Example"
                logoFile="assets/forge-example/logo.png"
                """),
            ("assets/forge-example/logo.png", CreatePngBytes(Colors.Blue)));

        var mods = await CreateService().GetModsAsync(instance);

        var mod = Assert.Single(mods);
        Assert.Equal("Forge Example", mod.Name);
        Assert.Equal("forge", mod.Loader);
        Assert.Equal("forge-example", mod.ModId);
        Assert.Equal("1.0.0", mod.Version);
        Assert.True(string.IsNullOrWhiteSpace(mod.IconSource));
    }

    [Fact]
    public async Task GetModsAsyncReadsLegacyMcmodDisplayName()
    {
        var instance = CreateInstance("legacy-name");
        var jarPath = Path.Combine(instance.InstanceDirectory, "mods", "legacy-example.jar");
        CreateZip(
            jarPath,
            TextEntry(
                "mcmod.info",
                """
                [
                  {
                    "modid": "legacy-example",
                    "name": "Legacy Example",
                    "version": "1.0.0"
                  }
                ]
                """));

        var mods = await CreateService().GetModsAsync(instance);

        var mod = Assert.Single(mods);
        Assert.Equal("Legacy Example", mod.Name);
        Assert.Equal("forge", mod.Loader);
        Assert.Equal("legacy-example", mod.ModId);
        Assert.Equal("1.0.0", mod.Version);
        Assert.True(string.IsNullOrWhiteSpace(mod.IconSource));
    }

    [Fact]
    public async Task GetModsAsyncFallsBackWhenNoUsableIconExists()
    {
        var instance = CreateInstance("fallback-icon");
        CreateZip(
            Path.Combine(instance.InstanceDirectory, "mods", "no-icon.jar"),
            TextEntry(
                "fabric.mod.json",
                """
                {
                  "schemaVersion": 1,
                  "id": "no-icon",
                  "version": "1.0.0"
                }
                """));
        CreateZip(
            Path.Combine(instance.InstanceDirectory, "mods", "missing-icon.jar"),
            TextEntry(
                "fabric.mod.json",
                """
                {
                  "schemaVersion": 1,
                  "id": "missing-icon",
                  "version": "1.0.0",
                  "icon": "assets/missing.png"
                }
                """));
        CreateZip(
            Path.Combine(instance.InstanceDirectory, "mods", "broken-image.jar"),
            TextEntry(
                "fabric.mod.json",
                """
                {
                  "schemaVersion": 1,
                  "id": "broken-image",
                  "version": "1.0.0",
                  "icon": "assets/broken.png"
                }
                """),
            ("assets/broken.png", "not an image"u8.ToArray()));
        await File.WriteAllTextAsync(
            Path.Combine(instance.InstanceDirectory, "mods", "not-a-zip.jar"),
            "plain text");

        var mods = await CreateService().GetModsAsync(instance);

        Assert.Equal(4, mods.Count);
        Assert.All(mods, mod => Assert.True(string.IsNullOrWhiteSpace(mod.IconSource)));
    }

    [Fact]
    public async Task GetModsAsyncDeletesLegacyEmbeddedIconCacheDirectory()
    {
        var instance = CreateInstance("legacy-icon-cache-cleanup");
        var legacyCacheDirectory = Path.Combine(TempRoot, LauncherApplicationIdentity.StorageDirectoryName, "cache", "mods", "icons");
        Directory.CreateDirectory(legacyCacheDirectory);
        await File.WriteAllTextAsync(Path.Combine(legacyCacheDirectory, "old.png"), "old cache");

        await CreateService().GetModsAsync(instance);

        Assert.False(Directory.Exists(legacyCacheDirectory));
    }

    private ModService CreateService()
    {
        return new ModService(new LauncherPathProvider(TempRoot));
    }

    private GameInstance CreateInstance(string name)
    {
        var instanceDirectory = Path.Combine(TempRoot, "instances", name);
        Directory.CreateDirectory(Path.Combine(instanceDirectory, "mods"));
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

    private static (string EntryName, byte[] Content) TextEntry(string entryName, string content)
    {
        return (entryName, Encoding.UTF8.GetBytes(content));
    }

}
