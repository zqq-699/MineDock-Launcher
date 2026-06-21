namespace Launcher.App.Services;

public interface IInstanceFolderService
{
    bool DirectoryExists(string folderPath);

    string EnsureDirectoryExists(string folderPath);

    bool TryOpen(string folderPath);
}
