using System.IO;

namespace Launcher.Infrastructure.Minecraft;

internal static class MinecraftVersionDirectoryCopier
{
    public static void CopyVersionDirectory(
        string sourceGameDirectory,
        string destinationGameDirectory,
        string versionName)
    {
        var sourceDirectory = Path.Combine(sourceGameDirectory, "versions", versionName);
        var destinationDirectory = Path.Combine(destinationGameDirectory, "versions", versionName);
        if (!Directory.Exists(sourceDirectory))
            throw new DirectoryNotFoundException($"Version directory is missing: {sourceDirectory}");

        if (Directory.Exists(destinationDirectory))
            throw new IOException($"Version directory already exists: {versionName}");

        Directory.CreateDirectory(destinationDirectory);
        try
        {
            foreach (var filePath in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourceDirectory, filePath);
                var destinationPath = Path.Combine(destinationDirectory, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(filePath, destinationPath, overwrite: false);
            }
        }
        catch
        {
            if (Directory.Exists(destinationDirectory))
                Directory.Delete(destinationDirectory, recursive: true);

            throw;
        }
    }
}
