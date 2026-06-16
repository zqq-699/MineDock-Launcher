namespace Launcher.App.Services;

public interface IInstanceFolderService
{
    bool TryOpen(string folderPath);
}
