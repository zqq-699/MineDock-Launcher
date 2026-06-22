using System.IO;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Download;

public sealed partial class DownloadModpackManualDownloadsDialogViewModel : ObservableObject
{
    private readonly IInstanceFolderService instanceFolderService;
    private readonly IFloatingMessageService floatingMessageService;
    private string? manualDownloadsFilePath;
    private string? instanceDirectory;

    [ObservableProperty]
    private bool isOpen;

    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private string message = string.Empty;

    [ObservableProperty]
    private string hint = string.Empty;

    public DownloadModpackManualDownloadsDialogViewModel(
        IInstanceFolderService instanceFolderService,
        IFloatingMessageService floatingMessageService)
    {
        this.instanceFolderService = instanceFolderService;
        this.floatingMessageService = floatingMessageService;
    }

    public ObservableCollection<DownloadModpackManualDownloadItemViewModel> Files { get; } = [];

    public void Show(GameInstance instance, IReadOnlyList<ManualModpackDownload> manualDownloads)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(manualDownloads);

        instanceDirectory = instance.InstanceDirectory;
        manualDownloadsFilePath = Path.Combine(instance.InstanceDirectory, ModpackManualDownloads.FileName);
        Title = Strings.Dialog_ModpackManualDownloadsTitle;
        Message = string.Format(Strings.Dialog_ModpackManualDownloadsMessageFormat, instance.Name, manualDownloads.Count);
        Hint = Strings.Dialog_ModpackManualDownloadsHint;

        Files.Clear();
        foreach (var manualDownload in manualDownloads)
        {
            Files.Add(new DownloadModpackManualDownloadItemViewModel(
                string.IsNullOrWhiteSpace(manualDownload.DisplayName) ? manualDownload.FileName : manualDownload.DisplayName,
                manualDownload.FileName,
                manualDownload.FailureSummary));
        }

        IsOpen = true;
        OpenFileCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        OpenFileCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanOpenFile))]
    private void OpenFile()
    {
        if (!string.IsNullOrWhiteSpace(manualDownloadsFilePath)
            && instanceFolderService.TryRevealFile(manualDownloadsFilePath))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(instanceDirectory)
            && instanceFolderService.TryOpen(instanceDirectory))
        {
            return;
        }

        floatingMessageService.Show(Strings.Status_OpenModpackManualDownloadsFileFailed);
    }

    public bool CanOpenFile => IsOpen
        && (!string.IsNullOrWhiteSpace(manualDownloadsFilePath)
            || !string.IsNullOrWhiteSpace(instanceDirectory));

    partial void OnIsOpenChanged(bool value)
    {
        OpenFileCommand.NotifyCanExecuteChanged();
    }
}

public sealed class DownloadModpackManualDownloadItemViewModel
{
    public DownloadModpackManualDownloadItemViewModel(string title, string fileName, string failureSummary)
    {
        Title = title;
        FileName = fileName;
        FailureSummary = failureSummary;
    }

    public string Title { get; }

    public string FileName { get; }

    public string FailureSummary { get; }
}
