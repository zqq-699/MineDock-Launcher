/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.ViewModels.Home;
using Launcher.Application.Services;

namespace Launcher.Tests.ViewModels.Home;

public sealed class LaunchStatusDialogViewModelTests
{
    [Fact]
    public void ViewReportFallsBackThroughOrderedCandidates()
    {
        var folderService = new FakeInstanceFolderService(path => path.EndsWith("latest.log", StringComparison.Ordinal));
        var statusService = new FakeStatusService();
        var viewModel = CreateViewModel(folderService, statusService);
        viewModel.Show(CreateReport(
            new LaunchDiagnosticReference(LaunchDiagnosticType.MinecraftCrashReport, @"C:\logs\crash.txt"),
            new LaunchDiagnosticReference(LaunchDiagnosticType.JvmCrashReport, @"C:\logs\hs_err.log"),
            new LaunchDiagnosticReference(LaunchDiagnosticType.MinecraftLatestLog, @"C:\logs\latest.log"),
            new LaunchDiagnosticReference(LaunchDiagnosticType.LauncherDiagnostic, @"C:\logs\launcher.log")));

        viewModel.ViewReportCommand.Execute(null);

        Assert.Equal(
            [@"C:\logs\crash.txt", @"C:\logs\hs_err.log", @"C:\logs\latest.log"],
            folderService.OpenFileAttempts);
        Assert.Null(statusService.LastMessage);
    }

    [Fact]
    public void ViewReportShowsFriendlyErrorWhenEveryCandidateFails()
    {
        var folderService = new FakeInstanceFolderService(_ => false);
        var statusService = new FakeStatusService();
        var viewModel = CreateViewModel(folderService, statusService);
        viewModel.Show(CreateReport(
            new LaunchDiagnosticReference(LaunchDiagnosticType.CapturedOutput, @"C:\logs\output.log"),
            new LaunchDiagnosticReference(LaunchDiagnosticType.LauncherDiagnostic, @"C:\logs\launcher.log")));

        viewModel.ViewReportCommand.Execute(null);

        Assert.Equal(Strings.Status_OpenLaunchReportFailed, statusService.LastMessage);
        Assert.Equal(2, folderService.OpenFileAttempts.Count);
    }

    [Fact]
    public void StartupStageChangesTitleButNotDiagnosticOrder()
    {
        var folderService = new FakeInstanceFolderService(_ => false);
        var viewModel = CreateViewModel(folderService, new FakeStatusService());
        var report = CreateReport(
            new LaunchDiagnosticReference(LaunchDiagnosticType.MinecraftLatestLog, @"C:\logs\latest.log"),
            new LaunchDiagnosticReference(LaunchDiagnosticType.LauncherDiagnostic, @"C:\logs\launcher.log"))
            with { Kind = LaunchFailureKind.StartupAbnormalExit };

        viewModel.Show(report);
        viewModel.ViewReportCommand.Execute(null);

        Assert.Equal(Strings.Dialog_LaunchStatusInitializationFailedTitle, viewModel.Title);
        Assert.Equal([@"C:\logs\latest.log", @"C:\logs\launcher.log"], folderService.OpenFileAttempts);
    }

    [Fact]
    public void DialogShowsStructuredReasonAndLoaderEvidence()
    {
        var viewModel = CreateViewModel(new FakeInstanceFolderService(_ => false), new FakeStatusService());
        var analysis = new LaunchFailureAnalysis(
            LaunchFailureCategory.ModDependencyMissing,
            "mod_dependency_missing",
            "required_mod_dependency_missing",
            "install_missing_dependency")
        {
            Details =
            [
                new LaunchFailureDetail(
                    LaunchFailureDetailKind.MissingDependency,
                    ModName: "Iris",
                    ModVersion: "1.9.6+mc1.21.8",
                    DependencyName: "sodium",
                    RequiredVersion: "0.7.x",
                    OriginalReason: "Mod 'Iris' requires sodium, which is missing!",
                    OriginalSuggestion: "Install sodium, any 0.7.x version.")
            ]
        };

        viewModel.Show(CreateReport() with { Analysis = analysis });

        Assert.True(viewModel.HasAnalysisDetails);
        var detail = Assert.Single(viewModel.AnalysisDetails);
        Assert.Contains("Iris", detail.Summary);
        Assert.Contains("1.9.6+mc1.21.8", detail.Summary);
        Assert.Contains("sodium", detail.Summary);
        Assert.Contains("0.7.x", detail.Summary);
        Assert.Equal("Mod 'Iris' requires sodium, which is missing!", detail.OriginalReason);
        Assert.Equal("Install sodium, any 0.7.x version.", detail.OriginalSuggestion);
    }

    [Fact]
    public void DialogShowsOnlyFirstFiveAnalysisDetails()
    {
        var viewModel = CreateViewModel(new FakeInstanceFolderService(_ => false), new FakeStatusService());
        var analysis = new LaunchFailureAnalysis(
            LaunchFailureCategory.ModDependencyMissing,
            "mod_dependency_missing",
            "required_mod_dependency_missing",
            "install_missing_dependency")
        {
            Details = Enumerable.Range(1, 7)
                .Select(index => new LaunchFailureDetail(
                    LaunchFailureDetailKind.MissingDependency,
                    ModName: $"Mod {index}",
                    DependencyName: $"Dependency {index}",
                    OriginalReason: $"Reason {index}"))
                .ToArray()
        };

        viewModel.Show(CreateReport() with { Analysis = analysis });

        Assert.Equal(5, viewModel.AnalysisDetails.Count);
        Assert.True(viewModel.HasAdditionalAnalysisDetails);
        Assert.Contains("2", viewModel.AnalysisAdditionalDetailsHint);
        Assert.DoesNotContain(viewModel.AnalysisDetails, item => item.Summary.Contains("Mod 6", StringComparison.Ordinal));
    }

    [Fact]
    public void DialogKeepsGenericAnalysisWhenNoSpecificEvidenceExists()
    {
        var viewModel = CreateViewModel(new FakeInstanceFolderService(_ => false), new FakeStatusService());
        var analysis = new LaunchFailureAnalysis(
            LaunchFailureCategory.ModVersionIncompatible,
            "mod_version_incompatible",
            "mod_version_incompatible",
            "check_mod_versions");

        viewModel.Show(CreateReport() with { Analysis = analysis });

        Assert.True(viewModel.HasAnalysis);
        Assert.False(viewModel.HasAnalysisDetails);
        Assert.Empty(viewModel.AnalysisDetails);
        Assert.Contains(Strings.Dialog_LaunchAnalysisModVersionDetail, viewModel.Message);
    }

    [Fact]
    public async Task ExportReportDoesNothingWhenSaveDialogIsCanceled()
    {
        var exportService = new FakeLaunchDiagnosticExportService();
        var viewModel = CreateViewModel(
            new FakeInstanceFolderService(_ => false),
            new FakeStatusService(),
            new FakeFilePickerService(null),
            exportService);
        viewModel.Show(CreateReport(
            new LaunchDiagnosticReference(LaunchDiagnosticType.LauncherDiagnostic, @"C:\logs\launcher.log")));

        await viewModel.ExportReportCommand.ExecuteAsync(null);

        Assert.Equal(0, exportService.CallCount);
        Assert.True(viewModel.IsOpen);
    }

    [Fact]
    public async Task ExportReportUsesLauncherDiagnosticFallbackForLegacyReport()
    {
        var exportService = new FakeLaunchDiagnosticExportService();
        var viewModel = CreateViewModel(
            new FakeInstanceFolderService(_ => false),
            new FakeStatusService(),
            new FakeFilePickerService(@"C:\exports\report.zip"),
            exportService);
        viewModel.Show(CreateReport());

        await viewModel.ExportReportCommand.ExecuteAsync(null);

        var diagnostic = Assert.Single(exportService.LastRequest!.Diagnostics);
        Assert.Equal(LaunchDiagnosticType.LauncherDiagnostic, diagnostic.Type);
        Assert.Equal(@"C:\logs\launcher.log", diagnostic.Path);
    }

    [Fact]
    public async Task ExportReportPassesSensitiveValuesAndDoesNotReuseThemForNextReport()
    {
        const string token = "sensitive-session-token";
        var exportService = new FakeLaunchDiagnosticExportService();
        var viewModel = CreateViewModel(
            new FakeInstanceFolderService(_ => false),
            new FakeStatusService(),
            new FakeFilePickerService(@"C:\exports\report.zip"),
            exportService);
        viewModel.Show(CreateReport(
            new LaunchDiagnosticReference(LaunchDiagnosticType.LauncherDiagnostic, @"C:\logs\launcher.log")) with
        {
            ExportSensitiveValues = [token]
        });

        await viewModel.ExportReportCommand.ExecuteAsync(null);

        Assert.Equal([token], exportService.LastRequest!.SensitiveValues);

        viewModel.CloseCommand.Execute(null);
        viewModel.Show(CreateReport(
            new LaunchDiagnosticReference(LaunchDiagnosticType.LauncherDiagnostic, @"C:\logs\launcher.log")));
        await viewModel.ExportReportCommand.ExecuteAsync(null);

        Assert.Empty(exportService.LastRequest!.SensitiveValues);
    }

    [Fact]
    public async Task ExportReportShowsPartialSuccessStatus()
    {
        var statusService = new FakeStatusService();
        var exportService = new FakeLaunchDiagnosticExportService
        {
            Result = new LaunchDiagnosticExportResult(
                true,
                ExportedFileCount: 3,
                SkippedFileCount: 1)
        };
        var viewModel = CreateViewModel(
            new FakeInstanceFolderService(_ => false),
            statusService,
            new FakeFilePickerService(@"C:\exports\report.zip"),
            exportService);
        viewModel.Show(CreateReport(
            new LaunchDiagnosticReference(LaunchDiagnosticType.LauncherDiagnostic, @"C:\logs\launcher.log")));

        await viewModel.ExportReportCommand.ExecuteAsync(null);

        Assert.Equal(1, exportService.CallCount);
        Assert.Equal(
            string.Format(Strings.Status_LaunchReportExportPartialFormat, 3, 1),
            statusService.LastMessage);
        Assert.False(viewModel.IsExporting);
        Assert.True(viewModel.IsOpen);
    }

    [Fact]
    public async Task ExportReportShowsCompleteSuccessStatus()
    {
        var statusService = new FakeStatusService();
        var exportService = new FakeLaunchDiagnosticExportService
        {
            Result = new LaunchDiagnosticExportResult(true, ExportedFileCount: 4)
        };
        var viewModel = CreateViewModel(
            new FakeInstanceFolderService(_ => false),
            statusService,
            new FakeFilePickerService(@"C:\exports\report.zip"),
            exportService);
        viewModel.Show(CreateReport(
            new LaunchDiagnosticReference(LaunchDiagnosticType.LauncherDiagnostic, @"C:\logs\launcher.log")));

        await viewModel.ExportReportCommand.ExecuteAsync(null);

        Assert.Equal(
            string.Format(Strings.Status_LaunchReportExportSucceededFormat, 4),
            statusService.LastMessage);
    }

    [Fact]
    public async Task ExportReportShowsNoReadableFilesStatus()
    {
        var statusService = new FakeStatusService();
        var exportService = new FakeLaunchDiagnosticExportService
        {
            Result = new LaunchDiagnosticExportResult(
                false,
                LaunchDiagnosticExportFailureReason.NoReadableDiagnostics)
        };
        var viewModel = CreateViewModel(
            new FakeInstanceFolderService(_ => false),
            statusService,
            new FakeFilePickerService(@"C:\exports\report.zip"),
            exportService);
        viewModel.Show(CreateReport(
            new LaunchDiagnosticReference(LaunchDiagnosticType.LauncherDiagnostic, @"C:\logs\launcher.log")));

        await viewModel.ExportReportCommand.ExecuteAsync(null);

        Assert.Equal(Strings.Status_LaunchReportExportNoReadableFiles, statusService.LastMessage);
    }

    [Fact]
    public async Task ExportReportCannotRunTwiceConcurrently()
    {
        var completion = new TaskCompletionSource<LaunchDiagnosticExportResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var exportService = new FakeLaunchDiagnosticExportService
        {
            Behavior = _ => completion.Task
        };
        var viewModel = CreateViewModel(
            new FakeInstanceFolderService(_ => false),
            new FakeStatusService(),
            new FakeFilePickerService(@"C:\exports\report.zip"),
            exportService);
        viewModel.Show(CreateReport(
            new LaunchDiagnosticReference(LaunchDiagnosticType.LauncherDiagnostic, @"C:\logs\launcher.log")));

        var exportTask = viewModel.ExportReportCommand.ExecuteAsync(null);
        await Task.Yield();

        Assert.True(viewModel.IsExporting);
        Assert.False(viewModel.ExportReportCommand.CanExecute(null));
        completion.SetResult(new LaunchDiagnosticExportResult(true, ExportedFileCount: 1));
        await exportTask;

        Assert.False(viewModel.IsExporting);
        Assert.True(viewModel.ExportReportCommand.CanExecute(null));
        Assert.Equal(1, exportService.CallCount);
    }

    [Fact]
    public async Task ExportReportConvertsUnexpectedExceptionToFriendlyStatus()
    {
        var statusService = new FakeStatusService();
        var exportService = new FakeLaunchDiagnosticExportService
        {
            Behavior = _ => throw new IOException("export failed")
        };
        var viewModel = CreateViewModel(
            new FakeInstanceFolderService(_ => false),
            statusService,
            new FakeFilePickerService(@"C:\exports\report.zip"),
            exportService);
        viewModel.Show(CreateReport(
            new LaunchDiagnosticReference(LaunchDiagnosticType.LauncherDiagnostic, @"C:\logs\launcher.log")));

        await viewModel.ExportReportCommand.ExecuteAsync(null);

        Assert.Equal(Strings.Status_LaunchReportExportFailed, statusService.LastMessage);
        Assert.False(viewModel.IsExporting);
    }

    private static LaunchStatusDialogViewModel CreateViewModel(
        IInstanceFolderService folderService,
        IStatusService statusService,
        IFilePickerService? filePickerService = null,
        ILaunchDiagnosticExportService? exportService = null)
    {
        return new LaunchStatusDialogViewModel(
            folderService,
            filePickerService ?? new FakeFilePickerService(null),
            exportService ?? new FakeLaunchDiagnosticExportService(),
            statusService);
    }

    private static LaunchFailureReport CreateReport(params LaunchDiagnosticReference[] candidates)
    {
        return new LaunchFailureReport(
            LaunchFailureKind.RuntimeAbnormalExit,
            "Example",
            "1.21.4",
            1,
            @"C:\logs\launcher.log",
            @"C:\logs")
        {
            DiagnosticCandidates = candidates
        };
    }

    private sealed class FakeInstanceFolderService(Func<string, bool> openFile) : IInstanceFolderService
    {
        public List<string> OpenFileAttempts { get; } = [];

        public bool DirectoryExists(string folderPath) => true;
        public string EnsureDirectoryExists(string folderPath) => folderPath;
        public bool TryOpen(string folderPath) => true;
        public bool TryRevealFile(string filePath) => false;

        public bool TryOpenFile(string filePath)
        {
            OpenFileAttempts.Add(filePath);
            return openFile(filePath);
        }
    }

    private sealed class FakeStatusService : IStatusService
    {
        public event Action<string>? MessageReported;
        public string? LastMessage { get; private set; }

        public void Report(string message)
        {
            LastMessage = message;
            MessageReported?.Invoke(message);
        }
    }

    private sealed class FakeFilePickerService(string? exportPath) : IFilePickerService
    {
        public string? PickMinecraftSkin() => null;
        public string? PickJavaExecutable() => null;
        public string? PickLocalImportFile() => null;
        public string? PickModFile() => null;
        public string? PickSaveArchive() => null;
        public string? PickResourcePackArchive() => null;
        public string? PickShaderPackArchive() => null;
        public string? PickModpackExportArchive(string defaultFileName, ModpackExportKind kind) => null;
        public string? PickLaunchDiagnosticExportArchive(string instanceName) => exportPath;
        public string? PickFolder(string title, string? initialDirectory = null) => null;
    }

    private sealed class FakeLaunchDiagnosticExportService : ILaunchDiagnosticExportService
    {
        public int CallCount { get; private set; }
        public LaunchDiagnosticExportRequest? LastRequest { get; private set; }
        public LaunchDiagnosticExportResult Result { get; init; } = new(true, ExportedFileCount: 1);
        public Func<LaunchDiagnosticExportRequest, Task<LaunchDiagnosticExportResult>>? Behavior { get; init; }

        public Task<LaunchDiagnosticExportResult> ExportAsync(
            LaunchDiagnosticExportRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            return Behavior?.Invoke(request) ?? Task.FromResult(Result);
        }
    }
}
