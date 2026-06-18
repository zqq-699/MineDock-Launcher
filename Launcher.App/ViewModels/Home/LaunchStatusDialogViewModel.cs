using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Services;

namespace Launcher.App.ViewModels.Home;

public sealed partial class LaunchStatusDialogViewModel : ObservableObject
{
    private readonly IInstanceFolderService instanceFolderService;
    private readonly IStatusService statusService;
    private string? diagnosticDirectory;

    [ObservableProperty]
    private bool isOpen;

    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private string message = string.Empty;

    [ObservableProperty]
    private string diagnosticHint = string.Empty;

    public LaunchStatusDialogViewModel(
        IInstanceFolderService instanceFolderService,
        IStatusService statusService)
    {
        this.instanceFolderService = instanceFolderService;
        this.statusService = statusService;
    }

    public bool CanOpenLogDirectory => IsOpen && !string.IsNullOrWhiteSpace(diagnosticDirectory);

    public void Show(LaunchFailureReport report)
    {
        diagnosticDirectory = report.DiagnosticDirectory;
        Title = GetTitle(report.Kind);
        Message = string.Format(
            Strings.Dialog_LaunchStatusMessageFormat,
            string.IsNullOrWhiteSpace(report.InstanceName) ? report.VersionName : report.InstanceName,
            GetDescription(report));
        DiagnosticHint = string.IsNullOrWhiteSpace(report.DiagnosticPath)
            ? Strings.Dialog_LaunchStatusDiagnosticDirectoryHint
            : Strings.Dialog_LaunchStatusDiagnosticFileHint;
        IsOpen = true;
        OpenLogDirectoryCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        OpenLogDirectoryCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanOpenLogDirectory))]
    private void OpenLogDirectory()
    {
        if (string.IsNullOrWhiteSpace(diagnosticDirectory)
            || !instanceFolderService.TryOpen(diagnosticDirectory))
        {
            statusService.Report(Strings.Status_OpenLaunchLogFolderFailed);
        }
    }

    partial void OnIsOpenChanged(bool value)
    {
        OpenLogDirectoryCommand.NotifyCanExecuteChanged();
    }

    private static string GetTitle(LaunchFailureKind kind)
    {
        return kind switch
        {
            LaunchFailureKind.StartupProcessExited => Strings.Dialog_LaunchStatusExitedTitle,
            LaunchFailureKind.RuntimeAbnormalExit => Strings.Dialog_LaunchStatusRuntimeFailedTitle,
            _ => Strings.Dialog_LaunchStatusFailedTitle
        };
    }

    private static string GetDescription(LaunchFailureReport report)
    {
        return report.Kind switch
        {
            LaunchFailureKind.StartupProcessExited => Strings.Dialog_LaunchStatusStartupExitedMessage,
            LaunchFailureKind.RuntimeAbnormalExit => FormatExitCode(
                Strings.Dialog_LaunchStatusRuntimeAbnormalMessage,
                report.ExitCode),
            LaunchFailureKind.StartupAbnormalExit => FormatExitCode(
                Strings.Dialog_LaunchStatusStartupAbnormalMessage,
                report.ExitCode),
            _ => Strings.Dialog_LaunchStatusStartupFailedMessage
        };
    }

    private static string FormatExitCode(string format, int? exitCode)
    {
        return string.Format(
            format,
            exitCode is int value ? value.ToString() : Strings.Dialog_LaunchStatusUnknownExitCode);
    }
}
