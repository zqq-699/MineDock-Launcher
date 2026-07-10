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

using Launcher.Domain.Models;
using Launcher.Infrastructure.FileSystem;
using Launcher.Tests.Helpers;
using System.IO.Compression;

namespace Launcher.Tests.Infrastructure.ShaderPacks;

public sealed class LocalShaderPackServiceTests : TestTempDirectory
{
    [Fact]
    public async Task GetShaderPacksAsyncLoadsTopLevelZipFilesOnly()
    {
        var instance = CreateInstance("shader-pack-list");
        var shaderPacksDirectory = Path.Combine(instance.InstanceDirectory, "shaderpacks");
        Directory.CreateDirectory(shaderPacksDirectory);
        var nestedDirectory = Path.Combine(shaderPacksDirectory, "nested");
        Directory.CreateDirectory(nestedDirectory);

        var topLevelPath = Path.Combine(shaderPacksDirectory, "Complementary.zip");
        var secondTopLevelPath = Path.Combine(shaderPacksDirectory, "BSL.zip");
        var nestedZipPath = Path.Combine(nestedDirectory, "ignored.zip");
        CreateZip(topLevelPath);
        CreateZip(secondTopLevelPath);
        CreateZip(nestedZipPath);
        await File.WriteAllTextAsync(Path.Combine(shaderPacksDirectory, "not-a-zip.txt"), "ignored");

        var createdAt = new DateTimeOffset(2026, 1, 3, 10, 20, 30, TimeSpan.Zero);
        File.SetCreationTimeUtc(topLevelPath, createdAt.UtcDateTime);

        var shaderPacks = await new LocalShaderPackService().GetShaderPacksAsync(instance);

        Assert.Equal(2, shaderPacks.Count);
        Assert.DoesNotContain(shaderPacks, shaderPack => string.Equals(shaderPack.FileName, "ignored.zip", StringComparison.OrdinalIgnoreCase));

        var complementary = shaderPacks.Single(shaderPack => shaderPack.FileName == "Complementary.zip");
        Assert.Equal("Complementary", complementary.Name);
        Assert.Equal(topLevelPath, complementary.FullPath);
        Assert.Null(complementary.IconSource);
        Assert.Equal(createdAt, complementary.CreatedAt);
    }

    [Fact]
    public async Task ImportAsyncCopiesZipIntoShaderPacksAndRenamesDuplicates()
    {
        var instance = CreateInstance("shader-pack-import");
        var shaderPacksDirectory = Path.Combine(instance.InstanceDirectory, "shaderpacks");
        Directory.CreateDirectory(shaderPacksDirectory);
        CreateZip(Path.Combine(shaderPacksDirectory, "Complementary.zip"));

        var sourcePath = Path.Combine(TempRoot, "Complementary.zip");
        CreateZip(sourcePath);

        var result = await new LocalShaderPackService().ImportAsync(instance, sourcePath);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.ImportedShaderPack);
        Assert.Equal("Complementary (1).zip", result.ImportedShaderPack!.FileName);
        Assert.True(File.Exists(Path.Combine(shaderPacksDirectory, "Complementary (1).zip")));
    }

    [Fact]
    public async Task DeleteAsyncRemovesShaderPackFile()
    {
        var instance = CreateInstance("shader-pack-delete");
        var zipPath = Path.Combine(instance.InstanceDirectory, "shaderpacks", "Complementary.zip");
        CreateZip(zipPath);

        var service = new LocalShaderPackService();
        await service.DeleteAsync(new LocalShaderPack
        {
            Name = "Complementary",
            FileName = "Complementary.zip",
            FullPath = zipPath,
            CreatedAt = DateTimeOffset.UtcNow
        });

        Assert.False(File.Exists(zipPath));
    }

    private GameInstance CreateInstance(string name)
    {
        var instanceDirectory = Path.Combine(TempRoot, "instances", name);
        Directory.CreateDirectory(Path.Combine(instanceDirectory, "shaderpacks"));
        return new GameInstance
        {
            Id = name,
            Name = name,
            InstanceDirectory = instanceDirectory
        };
    }

    private static void CreateZip(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        archive.CreateEntry("shaders.properties");
    }
}
