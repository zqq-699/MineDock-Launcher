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

    [ObservableProperty]
    private bool hasAnalysis;

    [ObservableProperty]
    private string analysisReasonTitle = string.Empty;

    [ObservableProperty]
    private string analysisReasonDetail = string.Empty;

    [ObservableProperty]
    private string analysisRecommendation = string.Empty;

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
        ApplyAnalysis(report.Analysis);
        Message = string.Format(
            Strings.Dialog_LaunchStatusMessageFormat,
            string.IsNullOrWhiteSpace(report.InstanceName) ? report.VersionName : report.InstanceName,
            HasAnalysis ? AnalysisReasonDetail : GetDescription(report));
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

    private void ApplyAnalysis(LaunchFailureAnalysis? analysis)
    {
        if (analysis is null)
        {
            HasAnalysis = false;
            AnalysisReasonTitle = string.Empty;
            AnalysisReasonDetail = string.Empty;
            AnalysisRecommendation = string.Empty;
            return;
        }

        HasAnalysis = true;
        AnalysisReasonTitle = GetAnalysisReasonTitle(analysis);
        AnalysisReasonDetail = GetAnalysisReasonDetail(analysis);
        AnalysisRecommendation = GetAnalysisRecommendation(analysis);
    }

    private static string GetAnalysisReasonTitle(LaunchFailureAnalysis analysis)
    {
        return analysis.Category switch
        {
            LaunchFailureCategory.JavaVersionMismatch => Strings.Dialog_LaunchAnalysisJavaVersionTitle,
            LaunchFailureCategory.ModDependencyMissing => Strings.Dialog_LaunchAnalysisModDependencyTitle,
            LaunchFailureCategory.ModVersionIncompatible => Strings.Dialog_LaunchAnalysisModVersionTitle,
            LaunchFailureCategory.MissingGameFiles => Strings.Dialog_LaunchAnalysisMissingFilesTitle,
            LaunchFailureCategory.OutOfMemory => Strings.Dialog_LaunchAnalysisOutOfMemoryTitle,
            _ => Strings.Dialog_LaunchAnalysisUnknownTitle
        };
    }

    private static string GetAnalysisReasonDetail(LaunchFailureAnalysis analysis)
    {
        return analysis.Category switch
        {
            LaunchFailureCategory.JavaVersionMismatch => string.Format(
                Strings.Dialog_LaunchAnalysisJavaVersionDetailFormat,
                string.IsNullOrWhiteSpace(analysis.ModName)
                    ? Strings.Dialog_LaunchAnalysisCurrentInstance
                    : analysis.ModName,
                analysis.RequiredJavaMajorVersion?.ToString() ?? Strings.Dialog_LaunchStatusUnknownExitCode,
                analysis.CurrentJavaMajorVersion?.ToString() ?? Strings.Dialog_LaunchStatusUnknownExitCode),
            LaunchFailureCategory.ModDependencyMissing when !string.IsNullOrWhiteSpace(analysis.DependencyName) => string.Format(
                Strings.Dialog_LaunchAnalysisModDependencyDetailFormat,
                string.IsNullOrWhiteSpace(analysis.ModName)
                    ? Strings.Dialog_LaunchAnalysisCurrentInstance
                    : analysis.ModName,
                analysis.DependencyName),
            LaunchFailureCategory.ModDependencyMissing => Strings.Dialog_LaunchAnalysisModDependencyDetail,
            LaunchFailureCategory.ModVersionIncompatible => Strings.Dialog_LaunchAnalysisModVersionDetail,
            LaunchFailureCategory.MissingGameFiles
                when string.Equals(analysis.ReasonDetail, "missing_client_jar", StringComparison.OrdinalIgnoreCase)
                     && !string.IsNullOrWhiteSpace(analysis.MissingPath) => string.Format(
                         Strings.Dialog_LaunchAnalysisMissingClientJarDetailFormat,
                         analysis.MissingPath),
            LaunchFailureCategory.MissingGameFiles when !string.IsNullOrWhiteSpace(analysis.MissingPath) => string.Format(
                Strings.Dialog_LaunchAnalysisMissingClasspathEntryDetailFormat,
                analysis.MissingPath),
            LaunchFailureCategory.MissingGameFiles => Strings.Dialog_LaunchAnalysisMissingFilesDetail,
            LaunchFailureCategory.OutOfMemory => Strings.Dialog_LaunchAnalysisOutOfMemoryDetail,
            _ => Strings.Dialog_LaunchAnalysisUnknownDetail
        };
    }

    private static string GetAnalysisRecommendation(LaunchFailureAnalysis analysis)
    {
        return analysis.Category switch
        {
            LaunchFailureCategory.JavaVersionMismatch => string.Format(
                Strings.Dialog_LaunchAnalysisJavaVersionRecommendationFormat,
                analysis.RequiredJavaMajorVersion?.ToString() ?? Strings.Dialog_LaunchStatusUnknownExitCode),
            LaunchFailureCategory.ModDependencyMissing => Strings.Dialog_LaunchAnalysisModDependencyRecommendation,
            LaunchFailureCategory.ModVersionIncompatible => Strings.Dialog_LaunchAnalysisModVersionRecommendation,
            LaunchFailureCategory.MissingGameFiles => Strings.Dialog_LaunchAnalysisMissingFilesRecommendation,
            LaunchFailureCategory.OutOfMemory => Strings.Dialog_LaunchAnalysisOutOfMemoryRecommendation,
            _ => Strings.Dialog_LaunchAnalysisUnknownRecommendation
        };
    }
}
