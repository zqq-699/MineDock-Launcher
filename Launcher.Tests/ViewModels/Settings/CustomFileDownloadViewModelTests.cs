/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Collections.Concurrent;
using Launcher.App.Services;
using Launcher.App.ViewModels.Download;
using Launcher.App.ViewModels.Settings;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Tests.ViewModels.Settings;

public sealed class CustomFileDownloadViewModelTests : TestTempDirectory
{
    [Theory]
    [InlineData("https://example.test/file.jar", true)]
    [InlineData(" http://example.test/file.zip?token=secret ", true)]
    [InlineData("file.jar", false)]
    [InlineData("ftp://example.test/file.jar", false)]
    [InlineData("https:///file.jar", false)]
    [InlineData("", false)]
    public void AddressValidationAcceptsOnlyAbsoluteHttpAddresses(string value, bool expected)
    {
        var valid = CustomFileDownloadViewModel.TryNormalizeHttpAddress(
            value,
            out var normalized,
            out _);

        Assert.Equal(expected, valid);
        Assert.Equal(expected, !string.IsNullOrWhiteSpace(normalized));
    }

    [Theory]
    [InlineData("https://example.test/files/client.jar?token=secret", "client.jar")]
    [InlineData("https://example.test/files/My%20Pack.zip", "My Pack.zip")]
    [InlineData("https://example.test/", "download")]
    public void DefaultFileNameComesFromUrlPath(string address, string expected)
    {
        Assert.Equal(expected, CustomFileDownloadViewModel.ResolveDefaultFileName(new Uri(address)));
    }

    [Fact]
    public void InvalidAddressShowsValidationAndEditingClearsIt()
    {
        var picker = new RecordingFilePickerService(null);
        var tasks = new DownloadTasksPageViewModel(TimeSpan.FromMinutes(1));
        var viewModel = CreateViewModel(new RecordingDownloadService(), picker, tasks);

        viewModel.OpenDialogCommand.Execute(null);
        viewModel.Address = "ftp://example.test/file.jar";
        viewModel.DownloadCommand.Execute(null);

        Assert.True(viewModel.IsDialogOpen);
        Assert.True(viewModel.HasAddressValidationError);
        Assert.Equal(0, picker.CustomDownloadPickCount);
        Assert.Empty(tasks.Tasks);

        viewModel.Address = "https://example.test/file.jar";

        Assert.False(viewModel.HasAddressValidationError);
    }

    [Fact]
    public void CancelingSavePickerKeepsAddressDialogOpen()
    {
        var picker = new RecordingFilePickerService(null);
        var tasks = new DownloadTasksPageViewModel(TimeSpan.FromMinutes(1));
        var viewModel = CreateViewModel(new RecordingDownloadService(), picker, tasks);
        viewModel.OpenDialogCommand.Execute(null);
        viewModel.Address = "https://example.test/files/client.jar";

        viewModel.DownloadCommand.Execute(null);

        Assert.True(viewModel.IsDialogOpen);
        Assert.Equal("client.jar", picker.LastDefaultFileName);
        Assert.Empty(tasks.Tasks);
    }

    [Fact]
    public async Task ConfirmedDestinationCreatesVisibleTaskAndCompletes()
    {
        var destination = Path.Combine(TempRoot, "client.jar");
        var picker = new RecordingFilePickerService(destination);
        var service = new RecordingDownloadService
        {
            Handler = (_, _, progress, _) =>
            {
                progress?.Report(new LauncherProgress("CustomFileDownload", string.Empty, 42));
                return Task.CompletedTask;
            }
        };
        var tasks = new DownloadTasksPageViewModel(TimeSpan.FromMinutes(1));
        var floatingMessages = new RecordingFloatingMessageService();
        var viewModel = CreateViewModel(service, picker, tasks, floatingMessages);
        viewModel.OpenDialogCommand.Execute(null);
        viewModel.Address = "https://example.test/client.jar?token=secret";

        viewModel.DownloadCommand.Execute(null);
        Assert.True(await tasks.WaitForTrackedBackgroundTasksAsync(TimeSpan.FromSeconds(5)));

        var task = Assert.Single(tasks.Tasks);
        Assert.False(viewModel.IsDialogOpen);
        Assert.Equal("client.jar", task.Title);
        Assert.Equal(TempRoot, task.Subtitle);
        Assert.Equal(DownloadTaskState.Completed, task.State);
        Assert.Equal(100, task.ProgressPercent);
        Assert.Equal(
            string.Format(
                Launcher.App.Resources.Strings.Status_CustomFileDownloadStartedFormat,
                "client.jar"),
            Assert.Single(floatingMessages.Messages));
        Assert.Equal("https://example.test/client.jar?token=secret", service.Requests.Single().SourceUrl);
        Assert.Equal(destination, service.Requests.Single().DestinationPath);
    }

    [Fact]
    public async Task FailureUsesFriendlyTaskStatus()
    {
        var destination = Path.Combine(TempRoot, "failed.bin");
        var service = new RecordingDownloadService
        {
            Handler = (_, _, _, _) => Task.FromException(new HttpRequestException("sensitive server detail"))
        };
        var tasks = new DownloadTasksPageViewModel(TimeSpan.FromMinutes(1));
        var viewModel = CreateViewModel(
            service,
            new RecordingFilePickerService(destination),
            tasks);
        viewModel.OpenDialogCommand.Execute(null);
        viewModel.Address = "http://example.test/failed.bin";

        viewModel.DownloadCommand.Execute(null);
        Assert.True(await tasks.WaitForTrackedBackgroundTasksAsync(TimeSpan.FromSeconds(5)));

        var task = Assert.Single(tasks.Tasks);
        Assert.Equal(DownloadTaskState.Failed, task.State);
        Assert.Equal(Launcher.App.Resources.Strings.Status_CustomFileDownloadFailed, task.StatusMessage);
        Assert.DoesNotContain("sensitive", task.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancelTaskCancelsUnderlyingDownloadAndRemovesCard()
    {
        var destination = Path.Combine(TempRoot, "cancel.bin");
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new RecordingDownloadService
        {
            Handler = async (_, _, _, cancellationToken) =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    canceled.TrySetResult();
                    throw;
                }
            }
        };
        var tasks = new DownloadTasksPageViewModel(TimeSpan.FromMinutes(1));
        var viewModel = CreateViewModel(
            service,
            new RecordingFilePickerService(destination),
            tasks);
        viewModel.OpenDialogCommand.Execute(null);
        viewModel.Address = "https://example.test/cancel.bin";
        viewModel.DownloadCommand.Execute(null);
        var task = Assert.Single(tasks.Tasks);

        tasks.CancelTask(task);
        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(await tasks.WaitForTrackedBackgroundTasksAsync(TimeSpan.FromSeconds(5)));

        Assert.Empty(tasks.Tasks);
    }

    private static CustomFileDownloadViewModel CreateViewModel(
        ICustomFileDownloadService service,
        IFilePickerService picker,
        DownloadTasksPageViewModel tasks,
        IFloatingMessageService? floatingMessageService = null) =>
        new(
            service,
            picker,
            floatingMessageService ?? new RecordingFloatingMessageService(),
            tasks,
            NullLogger<CustomFileDownloadViewModel>.Instance);

    private sealed class RecordingDownloadService : ICustomFileDownloadService
    {
        public ConcurrentQueue<Request> Requests { get; } = new();

        public Func<string, string, IProgress<LauncherProgress>?, CancellationToken, Task>? Handler { get; init; }

        public Task DownloadAsync(
            string sourceUrl,
            string destinationPath,
            IProgress<LauncherProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Requests.Enqueue(new Request(sourceUrl, destinationPath));
            return Handler?.Invoke(sourceUrl, destinationPath, progress, cancellationToken)
                ?? Task.CompletedTask;
        }
    }

    private sealed record Request(string SourceUrl, string DestinationPath);

    private sealed class RecordingFloatingMessageService : IFloatingMessageService
    {
        public event Action<string>? MessageRequested;

        public List<string> Messages { get; } = [];

        public void Show(string message)
        {
            Messages.Add(message);
            MessageRequested?.Invoke(message);
        }
    }

    private sealed class RecordingFilePickerService(string? destination) : IFilePickerService
    {
        public int CustomDownloadPickCount { get; private set; }
        public string? LastDefaultFileName { get; private set; }

        public string? PickCustomDownloadDestination(string defaultFileName)
        {
            CustomDownloadPickCount++;
            LastDefaultFileName = defaultFileName;
            return destination;
        }

        public string? PickMinecraftSkin() => null;
        public string? PickJavaExecutable() => null;
        public string? PickLocalImportFile() => null;
        public string? PickModFile() => null;
        public string? PickSaveArchive() => null;
        public string? PickResourcePackArchive() => null;
        public string? PickShaderPackArchive() => null;
        public string? PickModpackExportArchive(string defaultFileName, ModpackExportKind kind) => null;
        public string? PickLaunchDiagnosticExportArchive(string instanceName) => null;
        public string? PickFolder(string title, string? initialDirectory = null) => null;
    }
}
