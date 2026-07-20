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
    [InlineData("0.12.5", true)]
    [InlineData("0.12.5+build.1", true)]
    [InlineData("0.12.6", false)]
    [InlineData("0.13.0", false)]
    [InlineData("invalid", true)]
    public void ShadingBoundaryMatchesAtLauncher(string loaderVersion, bool expected)
    {
        Assert.Equal(expected, FabricServerLauncherJarBuilder.ShouldShadeLibraries(loaderVersion));
    }

    [Fact]
    public async Task LegacyLauncherShadesLibrariesAndDiscardsSignatures()
    {
        var librariesRoot = Path.Combine(TempRoot, "libraries");
        var first = await WriteLibraryAsync(
            librariesRoot,
            "example/first/1.0/first-1.0.jar",
            new Dictionary<string, byte[]>
            {
                ["META-INF/MANIFEST.MF"] = Encoding.UTF8.GetBytes("Manifest-Version: 1.0\r\n"),
                ["META-INF/FIRST.SF"] = Encoding.UTF8.GetBytes("signature"),
                ["net/fabricmc/loader/launch/server/FabricServerLauncher.class"] = [1, 2, 3],
                ["duplicate/resource.txt"] = Encoding.UTF8.GetBytes("same")
            });
        var second = await WriteLibraryAsync(
            librariesRoot,
            "example/second/1.0/second-1.0.jar",
            new Dictionary<string, byte[]>
            {
                ["example/Dependency.class"] = [4, 5, 6],
                ["duplicate/resource.txt"] = Encoding.UTF8.GetBytes("same")
            });
        var destination = Path.Combine(TempRoot, "fabric-server-launch.jar");

        await FabricServerLauncherJarBuilder.CreateAsync(
            destination,
            "net.fabricmc.loader.launch.server.FabricServerLauncher",
            "net.fabricmc.loader.launch.knot.KnotServer",
            [CreateArtifact(librariesRoot, first), CreateArtifact(librariesRoot, second)],
            librariesRoot,
            "0.12.5",
            CancellationToken.None);

        using var launcher = ZipFile.OpenRead(destination);
        var manifest = await ReadEntryAsync(launcher, "META-INF/MANIFEST.MF");
        Assert.Contains("Main-Class: net.fabricmc.loader.launch.server.FabricServerLauncher", manifest);
        Assert.DoesNotContain("Class-Path:", manifest);
        Assert.NotNull(launcher.GetEntry("fabric-server-launch.properties"));
        Assert.NotNull(launcher.GetEntry("net/fabricmc/loader/launch/server/FabricServerLauncher.class"));
        Assert.NotNull(launcher.GetEntry("example/Dependency.class"));
        Assert.Single(launcher.Entries.Where(entry => entry.FullName == "duplicate/resource.txt"));
        Assert.Null(launcher.GetEntry("META-INF/FIRST.SF"));
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

    [Fact]
    public async Task LegacyLauncherRejectsUnsafeLibraryEntryAndRemovesTemporaryOutput()
    {
        var librariesRoot = Path.Combine(TempRoot, "libraries");
        var library = await WriteLibraryAsync(
            librariesRoot,
            "example/unsafe/1.0/unsafe-1.0.jar",
            new Dictionary<string, byte[]> { ["../escape.class"] = [1] });
        var destination = Path.Combine(TempRoot, "fabric-server-launch.jar");

        await Assert.ThrowsAsync<InvalidDataException>(() => FabricServerLauncherJarBuilder.CreateAsync(
            destination,
            "net.fabricmc.loader.launch.server.FabricServerLauncher",
            "example.Main",
            [CreateArtifact(librariesRoot, library)],
            librariesRoot,
            "0.12.5",
            CancellationToken.None));

        Assert.False(File.Exists(destination));
        Assert.Empty(Directory.EnumerateFiles(TempRoot, "*.download", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task LegacyLauncherRejectsLibraryChangedAfterDownloadValidation()
    {
        var librariesRoot = Path.Combine(TempRoot, "libraries");
        var library = await WriteLibraryAsync(
            librariesRoot,
            "example/changed/1.0/changed-1.0.jar",
            new Dictionary<string, byte[]> { ["example/Original.class"] = [1] });
        var artifact = CreateArtifact(librariesRoot, library);
        await File.WriteAllBytesAsync(library, [1, 2, 3]);

        await Assert.ThrowsAsync<InvalidDataException>(() => FabricServerLauncherJarBuilder.CreateAsync(
            Path.Combine(TempRoot, "fabric-server-launch.jar"),
            "net.fabricmc.loader.launch.server.FabricServerLauncher",
            "example.Main",
            [artifact],
            librariesRoot,
            "0.12.5",
            CancellationToken.None));
    }

    [Fact]
    public async Task LegacyLauncherKeepsFirstDuplicateEntryInProfileOrder()
    {
        var librariesRoot = Path.Combine(TempRoot, "libraries");
        var first = await WriteLibraryAsync(
            librariesRoot,
            "example/first/1.0/first-1.0.jar",
            new Dictionary<string, byte[]> { ["example/Duplicate.class"] = [1] });
        var second = await WriteLibraryAsync(
            librariesRoot,
            "example/second/1.0/second-1.0.jar",
            new Dictionary<string, byte[]> { ["example/Duplicate.class"] = [2] });
        var destination = Path.Combine(TempRoot, "fabric-server-launch.jar");

        await FabricServerLauncherJarBuilder.CreateAsync(
            destination,
            "net.fabricmc.loader.launch.server.FabricServerLauncher",
            "example.Main",
            [CreateArtifact(librariesRoot, first), CreateArtifact(librariesRoot, second)],
            librariesRoot,
            "0.12.5",
            CancellationToken.None);

        using var launcher = ZipFile.OpenRead(destination);
        var entry = launcher.GetEntry("example/Duplicate.class")!;
        await using var stream = entry.Open();
        Assert.Equal(1, stream.ReadByte());
    }

    [Fact]
    public async Task LegacyLauncherRejectsSymbolicLinkEntry()
    {
        var librariesRoot = Path.Combine(TempRoot, "libraries");
        var relativePath = "example/link/1.0/link-1.0.jar";
        var libraryPath = Path.Combine(librariesRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(libraryPath)!);
        using (var archive = ZipFile.Open(libraryPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("example/Link.class");
            entry.ExternalAttributes = 0xA000 << 16;
            await using var stream = entry.Open();
            await stream.WriteAsync(new byte[] { 1 });
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => FabricServerLauncherJarBuilder.CreateAsync(
            Path.Combine(TempRoot, "fabric-server-launch.jar"),
            "net.fabricmc.loader.launch.server.FabricServerLauncher",
            "example.Main",
            [CreateArtifact(librariesRoot, libraryPath)],
            librariesRoot,
            "0.12.5",
            CancellationToken.None));
    }

    private static async Task<string> WriteLibraryAsync(
        string librariesRoot,
        string relativePath,
        IReadOnlyDictionary<string, byte[]> entries)
    {
        var path = Path.Combine(librariesRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach (var item in entries)
            {
                var entry = archive.CreateEntry(item.Key);
                await using var stream = entry.Open();
                await stream.WriteAsync(item.Value);
            }
        }
        return path;
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
