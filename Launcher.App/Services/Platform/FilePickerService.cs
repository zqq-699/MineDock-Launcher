using System.Windows;
using Launcher.App.Resources;
using Microsoft.Win32;
using System.IO;

namespace Launcher.App.Services;

public sealed class FilePickerService : IFilePickerService
{
    public string? PickMinecraftSkin()
    {
        var dialog = new OpenFileDialog
        {
            Title = Strings.FilePicker_MinecraftSkinTitle,
            Filter = Strings.FilePicker_MinecraftSkinFilter,
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog(System.Windows.Application.Current?.MainWindow) == true ? dialog.FileName : null;
    }

    public string? PickJavaExecutable()
    {
        var dialog = new OpenFileDialog
        {
            Title = Strings.FilePicker_JavaExecutableTitle,
            Filter = Strings.FilePicker_JavaExecutableFilter,
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog(System.Windows.Application.Current?.MainWindow) == true ? dialog.FileName : null;
    }

    public string? PickFolder(string title, string? initialDirectory = null)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(initialDirectory))
        {
            var normalizedDirectory = Path.GetFullPath(initialDirectory);
            if (Directory.Exists(normalizedDirectory))
                dialog.InitialDirectory = normalizedDirectory;
        }

        return dialog.ShowDialog(System.Windows.Application.Current?.MainWindow) == true
            ? dialog.FolderName
            : null;
    }
}
