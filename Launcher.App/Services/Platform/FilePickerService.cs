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

    public string? PickLocalImportFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = Strings.FilePicker_LocalImportFileTitle,
            Filter = Strings.FilePicker_LocalImportFileFilter,
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog(System.Windows.Application.Current?.MainWindow) == true ? dialog.FileName : null;
    }

    public string? PickModFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = Strings.FilePicker_ModFileTitle,
            Filter = Strings.FilePicker_ModFileFilter,
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog(System.Windows.Application.Current?.MainWindow) == true ? dialog.FileName : null;
    }

    public string? PickSaveArchive()
    {
        var dialog = new OpenFileDialog
        {
            Title = Strings.FilePicker_SaveArchiveTitle,
            Filter = Strings.FilePicker_SaveArchiveFilter,
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog(System.Windows.Application.Current?.MainWindow) == true ? dialog.FileName : null;
    }

    public string? PickResourcePackArchive()
    {
        var dialog = new OpenFileDialog
        {
            Title = Strings.FilePicker_ResourcePackArchiveTitle,
            Filter = Strings.FilePicker_ResourcePackArchiveFilter,
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog(System.Windows.Application.Current?.MainWindow) == true ? dialog.FileName : null;
    }

    public string? PickShaderPackArchive()
    {
        var dialog = new OpenFileDialog
        {
            Title = Strings.FilePicker_ShaderPackArchiveTitle,
            Filter = Strings.FilePicker_ShaderPackArchiveFilter,
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
