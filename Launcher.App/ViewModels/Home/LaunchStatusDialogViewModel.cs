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

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.Home;

public sealed partial class LaunchStatusDialogViewModel : ObservableObject
{
    private readonly IInstanceFolderService instanceFolderService;
    private readonly IFilePickerService filePickerService;
    private readonly ILaunchDiagnosticExportService diagnosticExportService;
    private readonly IStatusService statusService;
    private readonly ILogger<LaunchStatusDialogViewModel> logger;
    private IReadOnlyList<LaunchDiagnosticReference> diagnosticCandidates = [];
    private string instanceName = string.Empty;
    private string versionName = string.Empty;

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

    [ObservableProperty]
    private bool isExporting;

    public LaunchStatusDialogViewModel(
        IInstanceFolderService instanceFolderService,
        IFilePickerService filePickerService,
        ILaunchDiagnosticExportService diagnosticExportService,
        IStatusService statusService,
        ILogger<LaunchStatusDialogViewModel>? logger = null)
    {
        this.instanceFolderService = instanceFolderService;
        this.filePickerService = filePickerService;
        this.diagnosticExportService = diagnosticExportService;
        this.statusService = statusService;
        this.logger = logger ?? NullLogger<LaunchStatusDialogViewModel>.Instance;
    }

    public bool CanViewReport => IsOpen && diagnosticCandidates.Count > 0;

    public bool CanExportReport => IsOpen && !IsExporting && diagnosticCandidates.Count > 0;

    public void Show(LaunchFailureReport report)
    {
        instanceName = string.IsNullOrWhiteSpace(report.InstanceName) ? report.VersionName : report.InstanceName;
        versionName = report.VersionName;
        diagnosticCandidates = report.DiagnosticCandidates.Count > 0
            ? report.DiagnosticCandidates
            : report.PrimaryDiagnostic is { } primary
                ? [primary]
                : [];
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
        ViewReportCommand.NotifyCanExecuteChanged();
        ExportReportCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        ViewReportCommand.NotifyCanExecuteChanged();
        ExportReportCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanViewReport))]
    private void ViewReport()
    {
        foreach (var candidate in diagnosticCandidates)
        {
            if (instanceFolderService.TryOpenFile(candidate.Path))
                return;
        }

        statusService.Report(Strings.Status_OpenLaunchReportFailed);
    }

    [RelayCommand(CanExecute = nameof(CanExportReport))]
    private async Task ExportReportAsync()
    {
        var outputArchivePath = filePickerService.PickLaunchDiagnosticExportArchive(instanceName);
        if (string.IsNullOrWhiteSpace(outputArchivePath))
            return;

        IsExporting = true;
        try
        {
            var result = await diagnosticExportService.ExportAsync(new LaunchDiagnosticExportRequest(
                outputArchivePath,
                instanceName,
                versionName,
                diagnosticCandidates));
            if (result.IsSuccess)
            {
                statusService.Report(result.SkippedFileCount > 0
                    ? string.Format(
                        Strings.Status_LaunchReportExportPartialFormat,
                        result.ExportedFileCount,
                        result.SkippedFileCount)
                    : string.Format(
                        Strings.Status_LaunchReportExportSucceededFormat,
                        result.ExportedFileCount));
                return;
            }

            statusService.Report(result.FailureReason == LaunchDiagnosticExportFailureReason.NoReadableDiagnostics
                ? Strings.Status_LaunchReportExportNoReadableFiles
                : Strings.Status_LaunchReportExportFailed);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Launch diagnostic export command failed. InstanceName={InstanceName} VersionName={VersionName}",
                instanceName,
                versionName);
            statusService.Report(Strings.Status_LaunchReportExportFailed);
        }
        finally
        {
            IsExporting = false;
        }
    }

    partial void OnIsOpenChanged(bool value)
    {
        ViewReportCommand.NotifyCanExecuteChanged();
        ExportReportCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsExportingChanged(bool value) => ExportReportCommand.NotifyCanExecuteChanged();

    private static string GetTitle(LaunchFailureKind kind)
    {
        return kind switch
        {
            LaunchFailureKind.StartupProcessExited => Strings.Dialog_LaunchStatusExitedTitle,
            LaunchFailureKind.StartupAbnormalExit => Strings.Dialog_LaunchStatusInitializationFailedTitle,
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
