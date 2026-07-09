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
        && IsCurseForgeSelected
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

        var outputPath = filePickerService.PickModpackExportArchive(CreateDefaultArchiveFileName());
        if (string.IsNullOrWhiteSpace(outputPath))
            return;

        IsExporting = true;
        floatingMessageService.Show(Strings.Status_ModpackExporting);
        try
        {
            var result = await modpackExportService.ExportAsync(new ModpackExportRequest(
                selectedInstance,
                ModpackExportKind.CurseForge,
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
        if (value is not null && !IsCurseForgeSelected)
            statusService.Report(Strings.Status_ModrinthExportUnsupported);
    }

    partial void OnIsExportingChanged(bool value)
    {
        NotifyCanExportChanged();
    }

    private bool IsCurseForgeSelected =>
        string.Equals(SelectedExportTypeOption?.Id, "curseforge", StringComparison.OrdinalIgnoreCase);

    private void NotifyCanExportChanged()
    {
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(IsExportModpackNameEmpty));
        ExportCommand.NotifyCanExecuteChanged();
    }

    private string CreateDefaultArchiveFileName()
    {
        var fileName = string.IsNullOrWhiteSpace(ExportModpackName)
            ? "modpack"
            : ExportModpackName.Trim();
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(invalidChar, '_');

        return fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            ? fileName
            : $"{fileName}.zip";
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
            ModpackExportFailureReason.InvalidRequest => Strings.Status_ModpackExportInvalidRequest,
            ModpackExportFailureReason.FileSystemError => Strings.Status_ModpackExportFileSystemFailed,
            _ => Strings.Status_ModpackExportFailed
        };
    }
}
