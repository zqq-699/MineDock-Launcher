/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Infrastructure;
using Launcher.Infrastructure.FileSystem;

namespace Launcher.Tests.Infrastructure.FileSystem;

public sealed class LauncherBackgroundImageCatalogTests : IDisposable
{
    private readonly string testDirectory = Path.Combine(
        Path.GetTempPath(),
        $"bhl-background-catalog-{Guid.NewGuid():N}");

    [Fact]
    public void GetCandidatePathsCreatesDedicatedDirectoryAndReturnsOnlyTopLevelSupportedImages()
    {
        var catalog = CreateCatalog();

        Assert.Empty(catalog.GetCandidatePaths());
        Assert.True(Directory.Exists(catalog.DirectoryPath));

        File.WriteAllText(Path.Combine(catalog.DirectoryPath, "ignored.txt"), "not an image");
        File.WriteAllText(Path.Combine(catalog.DirectoryPath, "z-last.jpg"), string.Empty);
        File.WriteAllText(Path.Combine(catalog.DirectoryPath, "A-first.PNG"), string.Empty);
        var nestedDirectory = Directory.CreateDirectory(Path.Combine(catalog.DirectoryPath, "nested"));
        File.WriteAllText(Path.Combine(nestedDirectory.FullName, "nested.png"), string.Empty);

        Assert.Equal(
            [
                Path.Combine(catalog.DirectoryPath, "A-first.PNG"),
                Path.Combine(catalog.DirectoryPath, "z-last.jpg")
            ],
            catalog.GetCandidatePaths());
    }

    [Theory]
    [InlineData(".png")]
    [InlineData(".tiff")]
    public void GetCandidatePathsAcceptsSupportedExtensionCaseInsensitively(string extension)
    {
        var catalog = CreateCatalog();
        Directory.CreateDirectory(catalog.DirectoryPath);
        var imagePath = Path.Combine(catalog.DirectoryPath, $"background{extension.ToUpperInvariant()}");
        File.WriteAllText(imagePath, string.Empty);

        Assert.Equal(imagePath, Assert.Single(catalog.GetCandidatePaths()));
    }

    public void Dispose()
    {
        if (Directory.Exists(testDirectory))
            Directory.Delete(testDirectory, recursive: true);
    }

    private LauncherBackgroundImageCatalog CreateCatalog()
    {
        return new LauncherBackgroundImageCatalog(
            new LauncherPathProvider(testDirectory, testDirectory));
    }
}
