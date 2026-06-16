using System.Diagnostics;
using System.IO;

namespace Launcher.App.Services;

public sealed class InstanceFolderService : IInstanceFolderService
{
    public bool TryOpen(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
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
}
