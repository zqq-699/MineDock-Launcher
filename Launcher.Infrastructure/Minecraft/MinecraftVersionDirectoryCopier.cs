using System.IO;

namespace Launcher.Infrastructure.Minecraft;

internal static class MinecraftVersionDirectoryCopier
{
    public static void CopyVersionDirectory(
        string sourceGameDirectory,
        string destinationGameDirectory,
        string versionName,
        bool allowExistingDestination = false)
    {
        var sourceDirectory = Path.Combine(sourceGameDirectory, "versions", versionName);
        var destinationDirectory = Path.Combine(destinationGameDirectory, "versions", versionName);
        if (!Directory.Exists(sourceDirectory))
            throw new DirectoryNotFoundException($"Version directory is missing: {sourceDirectory}");

        var destinationAlreadyExists = Directory.Exists(destinationDirectory);
        if (destinationAlreadyExists && !allowExistingDestination)
            throw new IOException($"Version directory already exists: {versionName}");

        Directory.CreateDirectory(destinationDirectory);
        var copiedFiles = new List<string>();
        try
        {
            foreach (var filePath in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourceDirectory, filePath);
                var destinationPath = Path.Combine(destinationDirectory, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(filePath, destinationPath, overwrite: false);
                copiedFiles.Add(destinationPath);
            }
        }
        catch
        {
            if (destinationAlreadyExists)
            {
                foreach (var copiedFile in copiedFiles)
                {
                    try
                    {
                        if (File.Exists(copiedFile))
                            File.Delete(copiedFile);
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }
            else if (Directory.Exists(destinationDirectory))
            {
                Directory.Delete(destinationDirectory, recursive: true);
            }

            throw;
        }
    }
}
