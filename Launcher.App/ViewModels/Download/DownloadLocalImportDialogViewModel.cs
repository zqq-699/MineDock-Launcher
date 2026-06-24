using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.Utilities;
using Launcher.Domain.Models;
using Launcher.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.Download;

public enum DownloadLocalImportDialogState
{
    Selection,
    Unrecognized
}

public sealed partial class DownloadLocalImportDialogViewModel : ObservableObject
{
    private readonly IFilePickerService filePickerService;
    private readonly ILocalModpackImportService modpackImportService;
    private readonly DownloadTasksPageViewModel downloadTasksPage;
    private readonly IUiDispatcher uiDispatcher;
    private readonly IFloatingMessageService floatingMessageService;
    private readonly DownloadModpackManualDownloadsDialogViewModel modpackManualDownloadsDialog;
    private readonly ILogger<DownloadLocalImportDialogViewModel> logger;
    private DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto;
    private int downloadSpeedLimitMbPerSecond;

    [ObservableProperty]
    private bool isOpen;

    [ObservableProperty]
    private string selectedFilePath = string.Empty;

    [ObservableProperty]
    private string selectedFileName = string.Empty;

    [ObservableProperty]
    private bool isDragOver;

    [ObservableProperty]
    private bool isImporting;

    [ObservableProperty]
    private DownloadLocalImportDialogState dialogState = DownloadLocalImportDialogState.Selection;

    public DownloadLocalImportDialogViewModel(
        IFilePickerService filePickerService,
        ILocalModpackImportService modpackImportService,
        DownloadTasksPageViewModel downloadTasksPage,
        IUiDispatcher uiDispatcher,
        IFloatingMessageService floatingMessageService,
        DownloadModpackManualDownloadsDialogViewModel modpackManualDownloadsDialog,
        ILogger<DownloadLocalImportDialogViewModel>? logger = null)
    {
        this.filePickerService = filePickerService;
        this.modpackImportService = modpackImportService;
        this.downloadTasksPage = downloadTasksPage;
        this.uiDispatcher = uiDispatcher;
        this.floatingMessageService = floatingMessageService;
        this.modpackManualDownloadsDialog = modpackManualDownloadsDialog;
        this.logger = logger ?? NullLogger<DownloadLocalImportDialogViewModel>.Instance;
    }

    public event EventHandler<GameInstance>? ModpackImported;

    public bool HasSelectedFile => !string.IsNullOrWhiteSpace(SelectedFilePath);

    public bool IsSelectionState => DialogState is DownloadLocalImportDialogState.Selection;

    public bool IsUnrecognizedState => DialogState is DownloadLocalImportDialogState.Unrecognized;

    public bool CanAcceptDroppedFiles(IReadOnlyList<string> paths)
    {
        return !IsImporting && TryResolveSingleFile(paths, out _);
    }

    public async Task<bool> ImportDroppedFilesAsync(IReadOnlyList<string> paths)
    {
        if (!CanAcceptDroppedFiles(paths) || !TryResolveSingleFile(paths, out var resolvedPath))
            return false;

        Reset();
        SetSelectedFile(resolvedPath, "page-dragdrop");
        await ConfirmImportCommand.ExecuteAsync(null);
        if (DialogState is DownloadLocalImportDialogState.Unrecognized)
            IsOpen = true;

        return true;
    }

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
        DialogState = DownloadLocalImportDialogState.Selection;
    }

    public bool PreviewDroppedFiles(IReadOnlyList<string> paths)
    {
        if (IsImporting || DialogState is not DownloadLocalImportDialogState.Selection)
        {
            IsDragOver = false;
            return false;
        }

        var canAccept = TryResolveSingleFile(paths, out _);
        IsDragOver = canAccept;
        return canAccept;
    }

    public bool ApplyDroppedFiles(IReadOnlyList<string> paths)
    {
        if (IsImporting || DialogState is not DownloadLocalImportDialogState.Selection)
        {
            IsDragOver = false;
            return false;
        }

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

    public void ApplyDownloadSourcePreference(DownloadSourcePreference preference)
    {
        downloadSourcePreference = preference;
    }

    public void ApplyDownloadSpeedLimit(int value)
    {
        downloadSpeedLimitMbPerSecond = Math.Max(value, 0);
    }

    [RelayCommand]
    private void Cancel()
    {
        logger.LogInformation(
            "Canceled local import dialog. DialogState={DialogState} SelectedFileName={SelectedFileName}",
            DialogState,
            string.IsNullOrWhiteSpace(SelectedFileName) ? "<none>" : SelectedFileName);

        Close(resetDialogState: true);
    }

    [RelayCommand(CanExecute = nameof(CanConfirmImport))]
    private async Task ConfirmImportAsync()
    {
        if (!CanConfirmImport())
            return;

        var importPath = SelectedFilePath;
        var importFileName = SelectedFileName;
        logger.LogInformation(
            "Confirmed local modpack import. SelectedFileName={SelectedFileName}",
            importFileName);
        IsImporting = true;

        try
        {
            var recognitionResult = await modpackImportService.RecognizeArchiveAsync(importPath);
            if (!recognitionResult.IsSuccess)
            {
                logger.LogInformation(
                    "Selected local import file was not recognized. SelectedFileName={SelectedFileName} FailureReason={FailureReason}",
                    importFileName,
                    recognitionResult.FailureReason);
                ExecuteOnUiThread(() => DialogState = DownloadLocalImportDialogState.Unrecognized);
                return;
            }

            DownloadTaskItem createdTask = null!;
            ExecuteOnUiThread(() =>
            {
                floatingMessageService.Show(Strings.Status_ModpackInstalling);
                createdTask = downloadTasksPage.BeginTask(Strings.Download_LocalImportTaskTitle, importFileName);
                Close(resetDialogState: true);
            });

            _ = RunImportTaskAsync(
                importPath,
                importFileName,
                createdTask,
                downloadSourcePreference,
                downloadSpeedLimitMbPerSecond);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Unexpected local modpack recognition failure. SelectedFileName={SelectedFileName}",
                importFileName);
        }
        finally
        {
            ExecuteOnUiThread(() =>
            {
                IsImporting = false;
                ConfirmImportCommand.NotifyCanExecuteChanged();
            });
        }
    }

    private async Task RunImportTaskAsync(
        string importPath,
        string importFileName,
        DownloadTaskItem importTask,
        DownloadSourcePreference taskDownloadSourcePreference,
        int taskDownloadSpeedLimitMbPerSecond)
    {
        try
        {
            var result = await modpackImportService.ImportFromArchiveAsync(
                importPath,
                CreateProgressReporter(importTask),
                importTask.CancellationToken,
                taskDownloadSourcePreference,
                taskDownloadSpeedLimitMbPerSecond);

            if (result.IsSuccess && result.ImportedInstance is not null)
            {
                if (result.HasManualDownloads)
                {
                    importTask.Complete(string.Format(Strings.Status_ModpackImportedWithManualDownloadsFormat, result.ImportedInstance.Name));
                    ExecuteOnUiThread(() => modpackManualDownloadsDialog.Show(result.ImportedInstance, result.ManualDownloads));
                }
                else
                {
                    importTask.Complete(string.Format(Strings.Status_ModpackImportedFormat, result.ImportedInstance.Name));
                }

                ModpackImported?.Invoke(this, result.ImportedInstance);
                return;
            }

            importTask.Fail(MapFailureMessage(result.FailureReason));
        }
        catch (OperationCanceledException) when (importTask.IsCancellationRequested)
        {
            downloadTasksPage.CancelTask(importTask);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Unexpected local modpack import failure. SelectedFileName={SelectedFileName}",
                importFileName);
            importTask.Fail(Strings.Status_ModpackImportFailed);
        }
    }

    [RelayCommand]
    private void ConfirmUnrecognized()
    {
        logger.LogInformation(
            "Acknowledged unrecognized local import file. SelectedFileName={SelectedFileName}",
            string.IsNullOrWhiteSpace(SelectedFileName) ? "<none>" : SelectedFileName);
        Close(resetDialogState: false);
    }

    [RelayCommand]
    private void SelectFile()
    {
        var filePath = filePickerService.PickLocalImportFile();
        if (string.IsNullOrWhiteSpace(filePath))
            return;

        if (!TryResolveSingleFile([filePath], out _))
            return;

        SetSelectedFile(filePath, "picker");
    }

    partial void OnSelectedFilePathChanged(string value)
    {
        OnPropertyChanged(nameof(HasSelectedFile));
        RefreshConfirmImportCanExecute();
    }

    partial void OnIsImportingChanged(bool value)
    {
        RefreshConfirmImportCanExecute();
    }

    partial void OnDialogStateChanged(DownloadLocalImportDialogState value)
    {
        OnPropertyChanged(nameof(IsSelectionState));
        OnPropertyChanged(nameof(IsUnrecognizedState));
        RefreshConfirmImportCanExecute();
    }

    private void SetSelectedFile(string path, string source)
    {
        var normalizedPath = Path.GetFullPath(path);
        SelectedFilePath = normalizedPath;
        SelectedFileName = Path.GetFileName(normalizedPath);
        DialogState = DownloadLocalImportDialogState.Selection;
        logger.LogInformation(
            "Selected local import file. Source={Source} SelectedFileName={SelectedFileName}",
            source,
            SelectedFileName);
    }

    private bool CanConfirmImport()
    {
        return !IsImporting
            && DialogState is DownloadLocalImportDialogState.Selection
            && HasSelectedFile;
    }

    private void RefreshConfirmImportCanExecute()
    {
        ExecuteOnUiThread(() => ConfirmImportCommand.NotifyCanExecuteChanged());
    }

    private void ExecuteOnUiThread(Action action)
    {
        if (uiDispatcher.HasAccess)
        {
            action();
            return;
        }

        uiDispatcher.Invoke(action);
    }

    private void Close(bool resetDialogState)
    {
        IsOpen = false;
        SelectedFilePath = string.Empty;
        SelectedFileName = string.Empty;
        IsDragOver = false;
        if (resetDialogState)
            DialogState = DownloadLocalImportDialogState.Selection;
    }

    private static bool TryResolveSingleFile(IReadOnlyList<string> paths, out string resolvedPath)
    {
        resolvedPath = string.Empty;
        if (paths.Count != 1)
            return false;

        var path = paths[0];
        if (string.IsNullOrWhiteSpace(path) || Directory.Exists(path) || !File.Exists(path))
            return false;

        resolvedPath = Path.GetFullPath(path);
        return true;
    }

    private static IProgress<LauncherProgress> CreateProgressReporter(DownloadTaskItem importTask)
    {
        return new Progress<LauncherProgress>(progress =>
        {
            importTask.Report(progress with { Message = LauncherProgressTextFormatter.Format(progress) });
        });
    }

    private static string MapFailureMessage(ModpackImportFailureReason failureReason)
    {
        return failureReason switch
        {
            ModpackImportFailureReason.FileNotFound
                or ModpackImportFailureReason.UnsupportedArchive
                or ModpackImportFailureReason.InvalidManifest
                => Strings.Status_ModpackInvalidArchive,
            ModpackImportFailureReason.UnsupportedLoader
                => Strings.Status_ModpackUnsupportedLoader,
            ModpackImportFailureReason.MissingCurseForgeApiKey
                => Strings.Status_ModpackMissingCurseForgeApiKey,
            ModpackImportFailureReason.HashMismatch
                => Strings.Status_ModpackHashMismatch,
            _ => Strings.Status_ModpackImportFailed
        };
    }
}
