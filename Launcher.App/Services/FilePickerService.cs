using System.Windows;
using Microsoft.Win32;

namespace Launcher.App.Services;

public sealed class FilePickerService : IFilePickerService
{
    public string? PickMinecraftSkin()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 Minecraft 皮肤",
            Filter = "PNG 皮肤 (*.png)|*.png",
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog(System.Windows.Application.Current?.MainWindow) == true ? dialog.FileName : null;
    }
}
