/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Launcher.Infrastructure.Minecraft;
using Launcher.Infrastructure.Modpacks;
using Launcher.Tests.Helpers;

namespace Launcher.Tests.Infrastructure.Modpacks;

public sealed class FabricServerLauncherJarBuilderTests : TestTempDirectory
{
    [Theory]
    [InlineData("0.12.4", true)]
    [InlineData("0.12.6", false)]
    [InlineData("invalid", true)]
    public void ShadingBoundaryMatchesAtLauncher(string loaderVersion, bool expected)
    {
        Assert.Equal(expected, FabricServerLauncherJarBuilder.ShouldShadeLibraries(loaderVersion));
    }

    [Fact]
    public async Task ModernLauncherUsesClassPathWithoutOpeningLibraryArchive()
    {
        var librariesRoot = Path.Combine(TempRoot, "libraries");
        var relativePath = "example/modern/1.0/modern-1.0.jar";
        var libraryPath = Path.Combine(librariesRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(libraryPath)!);
        await File.WriteAllTextAsync(libraryPath, "not-a-jar");
        var destination = Path.Combine(TempRoot, "fabric-server-launch.jar");

        await FabricServerLauncherJarBuilder.CreateAsync(
            destination,
            "net.fabricmc.loader.launch.server.FabricServerLauncher",
            "example.Main",
            [CreateArtifact(librariesRoot, libraryPath)],
            librariesRoot,
            "0.12.6",
            CancellationToken.None);

        using var launcher = ZipFile.OpenRead(destination);
        var manifest = await ReadEntryAsync(launcher, "META-INF/MANIFEST.MF");
        Assert.Contains($"Class-Path: libraries/{relativePath}", manifest);
        Assert.Equal(2, launcher.Entries.Count);
    }

    private static ManagedLibraryArtifact CreateArtifact(string librariesRoot, string path)
    {
        var bytes = File.ReadAllBytes(path);
        return new ManagedLibraryArtifact(
            "https://example.test/library.jar",
            Path.GetRelativePath(librariesRoot, path).Replace('\\', '/'),
            "example:library:1.0",
            "Fabric",
            Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant(),
            bytes.LongLength);
    }

    private static async Task<string> ReadEntryAsync(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path) ?? throw new InvalidDataException($"Missing entry: {path}");
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}
