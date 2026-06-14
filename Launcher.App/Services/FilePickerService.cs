using System.Windows;
using Launcher.App.Resources;
using Microsoft.Win32;

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
}
