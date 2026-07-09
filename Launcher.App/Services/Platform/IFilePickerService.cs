namespace Launcher.App.Services;

public interface IFilePickerService
{
    string? PickMinecraftSkin();
    string? PickJavaExecutable();
    string? PickLocalImportFile();
    string? PickModFile();
    string? PickSaveArchive();
    string? PickResourcePackArchive();
    string? PickShaderPackArchive();
    string? PickModpackExportArchive(string defaultFileName);
    string? PickFolder(string title, string? initialDirectory = null);
}
