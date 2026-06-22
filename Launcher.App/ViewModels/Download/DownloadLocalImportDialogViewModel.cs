using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.Download;

public sealed partial class DownloadLocalImportDialogViewModel : ObservableObject
{
    private readonly IFilePickerService filePickerService;
    private readonly ILogger<DownloadLocalImportDialogViewModel> logger;

    [ObservableProperty]
    private bool isOpen;

    [ObservableProperty]
    private string selectedFilePath = string.Empty;

    [ObservableProperty]
    private string selectedFileName = string.Empty;

    [ObservableProperty]
    private bool isDragOver;

    public DownloadLocalImportDialogViewModel(
        IFilePickerService filePickerService,
        ILogger<DownloadLocalImportDialogViewModel>? logger = null)
    {
        this.filePickerService = filePickerService;
        this.logger = logger ?? NullLogger<DownloadLocalImportDialogViewModel>.Instance;
    }

    public bool HasSelectedFile => !string.IsNullOrWhiteSpace(SelectedFilePath);

    public void Open()
    {
        Reset();
        IsOpen = true;
        logger.LogInformation("Opened local import dialog.");
    }

    public void Reset()
    {
        SelectedFilePath = string.Empty;
        SelectedFileName = string.Empty;
        IsDragOver = false;
    }

    public bool PreviewDroppedFiles(IReadOnlyList<string> paths)
    {
        var canAccept = TryResolveSingleFile(paths, out _);
        IsDragOver = canAccept;
        return canAccept;
    }

    public bool ApplyDroppedFiles(IReadOnlyList<string> paths)
    {
        if (!TryResolveSingleFile(paths, out var resolvedPath))
        {
            IsDragOver = false;
            return false;
        }

        SetSelectedFile(resolvedPath, "dragdrop");
        IsDragOver = false;
        return true;
    }

    public void ClearDropState()
    {
        IsDragOver = false;
    }

    [RelayCommand]
    private void Cancel()
    {
        logger.LogInformation(
            "Canceled local import dialog. SelectedFileName={SelectedFileName}",
            string.IsNullOrWhiteSpace(SelectedFileName) ? "<none>" : SelectedFileName);

        IsOpen = false;
        Reset();
    }

    [RelayCommand]
    private void ConfirmNoOp()
    {
        logger.LogInformation(
            "Confirmed local import dialog placeholder. SelectedFileName={SelectedFileName}",
            string.IsNullOrWhiteSpace(SelectedFileName) ? "<none>" : SelectedFileName);
    }

    [RelayCommand]
    private void SelectFile()
    {
        var filePath = filePickerService.PickLocalImportFile();
        if (string.IsNullOrWhiteSpace(filePath))
            return;

        SetSelectedFile(filePath, "picker");
    }

    partial void OnSelectedFilePathChanged(string value)
    {
        OnPropertyChanged(nameof(HasSelectedFile));
    }

    private void SetSelectedFile(string path, string source)
    {
        var normalizedPath = Path.GetFullPath(path);
        SelectedFilePath = normalizedPath;
        SelectedFileName = Path.GetFileName(normalizedPath);
        logger.LogInformation(
            "Selected local import file. Source={Source} SelectedFileName={SelectedFileName}",
            source,
            SelectedFileName);
    }

    private static bool TryResolveSingleFile(IReadOnlyList<string> paths, out string resolvedPath)
    {
        resolvedPath = string.Empty;
        if (paths.Count != 1)
            return false;

        var path = paths[0];
        if (string.IsNullOrWhiteSpace(path))
            return false;

        if (Directory.Exists(path) || !File.Exists(path))
            return false;

        resolvedPath = Path.GetFullPath(path);
        return true;
    }
}
