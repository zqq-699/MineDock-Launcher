namespace Launcher.App.Services;

public interface IFilePickerService
{
    string? PickMinecraftSkin();
    string? PickJavaExecutable();
    string? PickModFile();
    string? PickFolder(string title, string? initialDirectory = null);
}
