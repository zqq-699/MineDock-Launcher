/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Windows;
using Launcher.App.Resources;
using Launcher.Application.Services;
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

    public string? PickModpackExportArchive(string defaultFileName, ModpackExportKind kind)
    {
        var isModrinth = kind is ModpackExportKind.Modrinth;
        var dialog = new SaveFileDialog
        {
            Title = isModrinth
                ? Strings.FilePicker_ModrinthModpackExportArchiveTitle
                : Strings.FilePicker_ModpackExportArchiveTitle,
            Filter = isModrinth
                ? Strings.FilePicker_ModrinthModpackExportArchiveFilter
                : Strings.FilePicker_ModpackExportArchiveFilter,
            FileName = string.IsNullOrWhiteSpace(defaultFileName)
                ? isModrinth ? "modpack.mrpack" : "modpack.zip"
                : defaultFileName,
            AddExtension = true,
            DefaultExt = isModrinth ? ".mrpack" : ".zip",
            OverwritePrompt = true
        };

        return dialog.ShowDialog(System.Windows.Application.Current?.MainWindow) == true ? dialog.FileName : null;
    }

    public string? PickLaunchDiagnosticExportArchive(string instanceName)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars().ToHashSet();
        var safeInstanceName = new string((instanceName ?? string.Empty)
            .Select(character => invalidCharacters.Contains(character) ? '_' : character)
            .ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(safeInstanceName))
            safeInstanceName = "Minecraft";

        var dialog = new SaveFileDialog
        {
            Title = Strings.FilePicker_LaunchDiagnosticExportTitle,
            Filter = Strings.FilePicker_LaunchDiagnosticExportFilter,
            FileName = $"BlockHelm-{safeInstanceName}-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
            AddExtension = true,
            DefaultExt = ".zip",
            OverwritePrompt = true
        };

        return dialog.ShowDialog(System.Windows.Application.Current?.MainWindow) == true ? dialog.FileName : null;
    }

    public string? PickCustomDownloadDestination(string defaultFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = Strings.FilePicker_CustomFileDownloadTitle,
            Filter = Strings.FilePicker_CustomFileDownloadFilter,
            FileName = string.IsNullOrWhiteSpace(defaultFileName) ? "download" : defaultFileName,
            AddExtension = false,
            OverwritePrompt = true
        };

        return dialog.ShowDialog(System.Windows.Application.Current?.MainWindow) == true
            ? dialog.FileName
            : null;
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
