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
        var viewModel = new LaunchStatusDialogViewModel(folderService, statusService);
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
        var viewModel = new LaunchStatusDialogViewModel(folderService, statusService);
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
        var viewModel = new LaunchStatusDialogViewModel(folderService, new FakeStatusService());
        var report = CreateReport(
            new LaunchDiagnosticReference(LaunchDiagnosticType.MinecraftLatestLog, @"C:\logs\latest.log"),
            new LaunchDiagnosticReference(LaunchDiagnosticType.LauncherDiagnostic, @"C:\logs\launcher.log"))
            with { Kind = LaunchFailureKind.StartupAbnormalExit };

        viewModel.Show(report);
        viewModel.ViewReportCommand.Execute(null);

        Assert.Equal(Strings.Dialog_LaunchStatusInitializationFailedTitle, viewModel.Title);
        Assert.Equal([@"C:\logs\latest.log", @"C:\logs\launcher.log"], folderService.OpenFileAttempts);
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
}
