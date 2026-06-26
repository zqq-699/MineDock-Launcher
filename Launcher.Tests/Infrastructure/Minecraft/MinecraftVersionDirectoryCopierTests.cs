using Launcher.Infrastructure.Minecraft;
using Launcher.Tests.Helpers;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class MinecraftVersionDirectoryCopierTests : TestTempDirectory
{
    [Fact]
    public void CopyVersionDirectoryCopiesToMissingDestination()
    {
        var sourceGameDirectory = Path.Combine(TempRoot, "source");
        var destinationGameDirectory = Path.Combine(TempRoot, "destination");
        WriteVersionFile(sourceGameDirectory, "Demo", "Demo.json", "{}");

        MinecraftVersionDirectoryCopier.CopyVersionDirectory(
            sourceGameDirectory,
            destinationGameDirectory,
            "Demo");

        Assert.True(File.Exists(Path.Combine(destinationGameDirectory, "versions", "Demo", "Demo.json")));
    }

    [Fact]
    public void CopyVersionDirectoryMergesIntoExistingDestinationWhenAllowed()
    {
        var sourceGameDirectory = Path.Combine(TempRoot, "source");
        var destinationGameDirectory = Path.Combine(TempRoot, "destination");
        WriteVersionFile(sourceGameDirectory, "Demo", "Demo.json", "{}");
        WriteVersionFile(sourceGameDirectory, "Demo", "Demo.jar", "jar");
        var existingConfigPath = Path.Combine(destinationGameDirectory, "versions", "Demo", "config", "pack.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(existingConfigPath)!);
        File.WriteAllText(existingConfigPath, "pack");

        MinecraftVersionDirectoryCopier.CopyVersionDirectory(
            sourceGameDirectory,
            destinationGameDirectory,
            "Demo",
            allowExistingDestination: true);

        Assert.Equal("pack", File.ReadAllText(existingConfigPath));
        Assert.True(File.Exists(Path.Combine(destinationGameDirectory, "versions", "Demo", "Demo.json")));
        Assert.True(File.Exists(Path.Combine(destinationGameDirectory, "versions", "Demo", "Demo.jar")));
    }

    [Fact]
    public void CopyVersionDirectoryRejectsExistingDestinationByDefault()
    {
        var sourceGameDirectory = Path.Combine(TempRoot, "source");
        var destinationGameDirectory = Path.Combine(TempRoot, "destination");
        WriteVersionFile(sourceGameDirectory, "Demo", "Demo.json", "{}");
        Directory.CreateDirectory(Path.Combine(destinationGameDirectory, "versions", "Demo"));

        Assert.Throws<IOException>(() => MinecraftVersionDirectoryCopier.CopyVersionDirectory(
            sourceGameDirectory,
            destinationGameDirectory,
            "Demo"));
    }

    [Fact]
    public void CopyVersionDirectoryDoesNotOverwriteExistingFilesWhenMerging()
    {
        var sourceGameDirectory = Path.Combine(TempRoot, "source");
        var destinationGameDirectory = Path.Combine(TempRoot, "destination");
        WriteVersionFile(sourceGameDirectory, "Demo", "00-new.txt", "new");
        WriteVersionFile(sourceGameDirectory, "Demo", "zz-conflict.txt", "source");
        var existingConfigPath = Path.Combine(destinationGameDirectory, "versions", "Demo", "config", "pack.txt");
        var conflictPath = Path.Combine(destinationGameDirectory, "versions", "Demo", "zz-conflict.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(existingConfigPath)!);
        File.WriteAllText(existingConfigPath, "pack");
        File.WriteAllText(conflictPath, "destination");

        Assert.Throws<IOException>(() => MinecraftVersionDirectoryCopier.CopyVersionDirectory(
            sourceGameDirectory,
            destinationGameDirectory,
            "Demo",
            allowExistingDestination: true));

        Assert.Equal("pack", File.ReadAllText(existingConfigPath));
        Assert.Equal("destination", File.ReadAllText(conflictPath));
        Assert.False(File.Exists(Path.Combine(destinationGameDirectory, "versions", "Demo", "00-new.txt")));
        Assert.True(Directory.Exists(Path.Combine(destinationGameDirectory, "versions", "Demo")));
    }

    private static void WriteVersionFile(
        string gameDirectory,
        string versionName,
        string relativePath,
        string content)
    {
        var filePath = Path.Combine(gameDirectory, "versions", versionName, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, content);
    }
}
