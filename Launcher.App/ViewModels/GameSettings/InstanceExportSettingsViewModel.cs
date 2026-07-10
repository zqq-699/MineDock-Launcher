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

using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.GameSettings;

public sealed partial class InstanceExportSettingsViewModel : GameSettingsDetailsSectionViewModelBase
{
    private const string DefaultExportVersion = "1.0.0";
    private readonly IModpackExportService? modpackExportService;
    private readonly IFilePickerService filePickerService;
    private readonly IStatusService statusService;
    private readonly IFloatingMessageService floatingMessageService;

    [ObservableProperty]
    private InstanceExportTypeOption? selectedExportTypeOption;

    [ObservableProperty]
    private string exportModpackName = string.Empty;

    [ObservableProperty]
    private string exportAuthor = string.Empty;

    [ObservableProperty]
    private string exportVersion = string.Empty;

    [ObservableProperty]
    private bool packMods = true;

    [ObservableProperty]
    private bool packDisabledMods;

    [ObservableProperty]
    private bool packResourcePacks = true;

    [ObservableProperty]
    private bool packShaderPacks = true;

    [ObservableProperty]
    private bool packSaves = true;

    [ObservableProperty]
    private bool isExporting;

    public InstanceExportSettingsViewModel(
        GameSettingsDetailsViewModel parent,
        IFilePickerService filePickerService,
        IStatusService statusService,
        IFloatingMessageService floatingMessageService,
        IModpackExportService? modpackExportService = null)
        : base(parent)
    {
        this.filePickerService = filePickerService;
        this.statusService = statusService;
        this.floatingMessageService = floatingMessageService;
        this.modpackExportService = modpackExportService;
        ExportTypeOptions.Add(new InstanceExportTypeOption("curseforge", Strings.GameSettings_ExportTypeCurseForge));
        ExportTypeOptions.Add(new InstanceExportTypeOption("modrinth", Strings.GameSettings_ExportTypeModrinth));
        SelectedExportTypeOption = ExportTypeOptions[0];
    }

    public override bool UsesFullViewportLayout => true;

    public bool CanExport =>
        !IsExporting
        && modpackExportService is not null
        && Parent.SelectedInstance is not null
        && !IsExportModpackNameEmpty;

    public bool CanPackDisabledMods => PackMods;

    public bool IsExportModpackNameEmpty => string.IsNullOrWhiteSpace(ExportModpackName);

    public ObservableCollection<InstanceExportTypeOption> ExportTypeOptions { get; } = [];

    public override void OnSelectedInstanceChanged(GameInstance? instance)
    {
        OnPropertyChanged(nameof(CanExport));
        ExportCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        var selectedInstance = Parent.SelectedInstance?.Instance;
        if (selectedInstance is null || modpackExportService is null)
            return;

        var exportKind = SelectedExportKind;
        var outputPath = filePickerService.PickModpackExportArchive(
            CreateDefaultArchiveFileName(exportKind),
            exportKind);
        if (string.IsNullOrWhiteSpace(outputPath))
            return;

        IsExporting = true;
        floatingMessageService.Show(Strings.Status_ModpackExporting);
        try
        {
            var result = await modpackExportService.ExportAsync(new ModpackExportRequest(
                selectedInstance,
                exportKind,
                ExportModpackName,
                ExportAuthor,
                ResolveExportVersion(),
                outputPath,
                PackMods,
                PackDisabledMods,
                PackResourcePacks,
                PackShaderPacks,
                PackSaves));

            if (result.IsSuccess)
            {
                floatingMessageService.Show(Strings.Status_ModpackExported);
                statusService.Report(string.Format(Strings.Status_ModpackExportedFormat, result.OutputArchivePath));
                return;
            }

            statusService.Report(ResolveFailureMessage(result.FailureReason));
        }
        finally
        {
            IsExporting = false;
        }
    }

    partial void OnExportModpackNameChanged(string value)
    {
        NotifyCanExportChanged();
    }

    partial void OnExportAuthorChanged(string value)
    {
        NotifyCanExportChanged();
    }

    partial void OnExportVersionChanged(string value)
    {
        NotifyCanExportChanged();
    }

    partial void OnPackModsChanged(bool value)
    {
        if (!value)
            PackDisabledMods = false;

        OnPropertyChanged(nameof(CanPackDisabledMods));
    }

    partial void OnSelectedExportTypeOptionChanged(InstanceExportTypeOption? value)
    {
        NotifyCanExportChanged();
    }

    partial void OnIsExportingChanged(bool value)
    {
        NotifyCanExportChanged();
    }

    private ModpackExportKind SelectedExportKind =>
        string.Equals(SelectedExportTypeOption?.Id, "modrinth", StringComparison.OrdinalIgnoreCase)
            ? ModpackExportKind.Modrinth
            : ModpackExportKind.CurseForge;

    private void NotifyCanExportChanged()
    {
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(IsExportModpackNameEmpty));
        ExportCommand.NotifyCanExecuteChanged();
    }

    private string CreateDefaultArchiveFileName(ModpackExportKind kind)
    {
        var fileName = string.IsNullOrWhiteSpace(ExportModpackName)
            ? "modpack"
            : ExportModpackName.Trim();
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(invalidChar, '_');

        var extension = kind is ModpackExportKind.Modrinth ? ".mrpack" : ".zip";
        return fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            ? fileName
            : $"{fileName}{extension}";
    }

    private string ResolveExportVersion()
    {
        return string.IsNullOrWhiteSpace(ExportVersion)
            ? DefaultExportVersion
            : ExportVersion;
    }

    private static string ResolveFailureMessage(ModpackExportFailureReason reason)
    {
        return reason switch
        {
            ModpackExportFailureReason.MissingCurseForgeApiKey => Strings.Status_ModpackExportMissingCurseForgeApiKey,
            ModpackExportFailureReason.MissingLoaderVersion => Strings.Status_ModpackExportMissingLoaderVersion,
            ModpackExportFailureReason.UnsupportedType => Strings.Status_ModrinthExportUnsupported,
            ModpackExportFailureReason.CurseForgeApiFailed => Strings.Status_ModpackExportCurseForgeApiFailed,
            ModpackExportFailureReason.ModrinthApiFailed => Strings.Status_ModpackExportModrinthApiFailed,
            ModpackExportFailureReason.InvalidRequest => Strings.Status_ModpackExportInvalidRequest,
            ModpackExportFailureReason.FileSystemError => Strings.Status_ModpackExportFileSystemFailed,
            _ => Strings.Status_ModpackExportFailed
        };
    }
}
