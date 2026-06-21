using System.Diagnostics;
using System.IO;

namespace Launcher.App.Services;

public sealed class InstanceFolderService : IInstanceFolderService
{
    public bool DirectoryExists(string folderPath)
    {
        return !string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath);
    }

    public string EnsureDirectoryExists(string folderPath)
    {
        var normalizedFolderPath = Path.GetFullPath(folderPath);
        Directory.CreateDirectory(normalizedFolderPath);
        return normalizedFolderPath;
    }

    public bool TryOpen(string folderPath)
    {
        if (!DirectoryExists(folderPath))
            return false;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{folderPath}\"",
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool TryRevealFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return false;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{filePath}\"",
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
